using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>得点を競う追加ラウンドの進行、場外復帰、同点順位を共通化する。</summary>
public abstract class ArenaRoundController : RoundScoring, IStageTick
{
    [SerializeField] private Transform[] respawnPoints;
    [SerializeField] private Vector3 arenaCenter;
    [SerializeField] private Vector2 arenaHalfSize = new Vector2(22f, 22f);
    [SerializeField] private float fallHeight = -6f;
    private readonly Dictionary<ulong, float> respawnCooldowns = new();
    private bool started;
    private bool awarded;

    public abstract string Objective { get; }
    public abstract string GetStatus(ulong clientId);
    public virtual string PhaseLabel => string.Empty;
    public virtual Color Accent => new Color(0.2f, 0.9f, 1f);
    protected float Elapsed => RoundManager.Instance == null ? 0f
        : RoundManager.Instance.RoundDuration - RoundManager.Instance.RemainingTime;
    protected bool IsPlaying => IsSpawned && RoundManager.Instance != null
        && RoundManager.Instance.IsReady && !RoundManager.Instance.IsIntro
        && !RoundManager.Instance.IsRoundOver
        && RoundManager.Instance.CountdownRemaining <= RoundManager.StartDisplayDuration
        && !SceneTransitionUI.PresentationBlocked;

    public override void OnNetworkSpawn()
    {
        started = false;
        awarded = false;
        respawnCooldowns.Clear();
        StageDirector.Register(this);
    }

    public override void OnNetworkDespawn()
    {
        StageDirector.Unregister(this);
        respawnCooldowns.Clear();
    }

    public void Tick(float deltaTime)
    {
        if (!IsSpawned) return;
        if (IsServer && IsPlaying && PlayerManager.Instance != null)
        {
            if (!started)
            {
                started = true;
                ServerStarted();
            }
            RespawnOutsidePlayers();
            ServerTick(deltaTime);
        }
        VisualTick(deltaTime);
    }

    private void RespawnOutsidePlayers()
    {
        if (respawnPoints == null || respawnPoints.Length == 0) return;
        var players = PlayerManager.Instance.AllPlayers;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null || !player.IsSpawned || player.IsEliminated) continue;
            Vector3 offset = player.transform.position - arenaCenter;
            if (player.transform.position.y >= fallHeight
                && Mathf.Abs(offset.x) <= arenaHalfSize.x && Mathf.Abs(offset.z) <= arenaHalfSize.y)
                continue;
            if (respawnCooldowns.TryGetValue(player.OwnerClientId, out float until) && Elapsed < until)
                continue;
            Transform point = respawnPoints[i % respawnPoints.Length];
            if (point == null) continue;
            // 所有者のNetworkTransformが戻るまで多重テレポートを抑止する。
            respawnCooldowns[player.OwnerClientId] = Elapsed + 1f;
            player.Teleport(point.position, point.rotation);
            PlayerRespawned(player);
        }
    }

    protected virtual void ServerStarted() { }
    protected abstract void ServerTick(float deltaTime);
    protected virtual void PlayerRespawned(PlayerController player) { }
    protected virtual void VisualTick(float deltaTime) { }

    /// <summary>得点の多い順。同点は同順位とし、次の順位は人数分進める。</summary>
    public static int RankForScore(int score, IList<int> scores)
    {
        int rank = 1;
        for (int i = 0; i < scores.Count; i++)
            if (scores[i] > score) rank++;
        return rank;
    }

    public override void AwardPoints()
    {
        if (!IsServer || awarded || PlayerManager.Instance == null
            || GameManager.Instance == null || ConnectionContainer.Instance == null) return;
        awarded = true;
        var players = new List<PlayerController>();
        var scores = new List<int>();
        foreach (var player in PlayerManager.Instance.AllPlayers)
        {
            if (player == null || !player.IsSpawned) continue;
            players.Add(player);
            scores.Add(player.GetComponent<PlayerStatus>()?.RoundScore ?? 0);
        }
        for (int i = 0; i < players.Count; i++)
            ConnectionContainer.Instance.GetConnection(players[i].OwnerClientId)?.AddScore(
                GameManager.Instance.GetPointsForRank(RankForScore(scores[i], scores)));
    }
}
