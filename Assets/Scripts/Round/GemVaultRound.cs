using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 宝石を持ち帰るラウンド。取得・納品はサーバーの位置判定で行い、
/// 宝石の出現状態と各プレイヤーの手持ちを全端末へ同期する。
/// </summary>
public sealed class GemVaultRound : ArenaRoundController
{
    [Header("宝石と金庫（マーカーは床面に配置）")]
    [SerializeField] private Transform[] gemNodes = Array.Empty<Transform>();
    [SerializeField] private Transform[] gemVisuals = Array.Empty<Transform>();
    [SerializeField] private Transform bank;
    [SerializeField, Min(1)] private int carryCapacity = 5;
    [SerializeField, Min(.1f)] private float pickupRadius = 1.35f;
    [SerializeField, Min(.1f)] private float bankRadius = 2.6f;
    [SerializeField, Min(.1f)] private float interactionHeight = 2.8f;
    [SerializeField, Min(.1f)] private float gemRespawnSeconds = 5f;

    private readonly NetworkVariable<uint> availableGems = new();
    private NetworkList<CarriedGems> carried;
    private readonly float[] nextAppearance = new float[32];
    private Vector3[] visualOrigins = Array.Empty<Vector3>();
    private Quaternion[] visualRotations = Array.Empty<Quaternion>();

    public override string Objective => $"宝石を最大{Capacity}個まで集め、中央の金庫へ！納品数で勝負。落下すると手持ちを失う。";
    public override string PhaseLabel => "宝石を集めて中央の金庫へ運ぼう";
    public override Color Accent => new Color(.2f, .95f, .85f);
    public uint AvailableGemMask => availableGems.Value;

    private int NodeCount => Mathf.Min(32, gemNodes == null ? 0 : gemNodes.Length);
    private int Capacity => Mathf.Max(1, carryCapacity);

    private void Awake()
    {
        carried = new NetworkList<CarriedGems>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheVisuals();
        if (!IsServer) return;
        carried.Clear();
        Array.Clear(nextAppearance, 0, nextAppearance.Length);
        availableGems.Value = InitialMask();
        if (gemNodes != null && gemNodes.Length > 32)
            Debug.LogWarning("GemVault: 宝石ノードは32個まで対応しています。", this);
        if (!bank || NodeCount == 0)
            Debug.LogError("GemVault: 金庫と宝石ノードを設定してください。", this);
    }

    protected override void ServerStarted()
    {
        carried.Clear();
        Array.Clear(nextAppearance, 0, nextAppearance.Length);
        availableGems.Value = InitialMask();
    }

    private uint InitialMask()
    {
        uint mask = 0;
        for (int i = 0; i < NodeCount; i++)
            if (gemNodes[i]) mask |= 1u << i;
        return mask;
    }

    protected override void ServerTick(float deltaTime)
    {
        if (PlayerManager.Instance == null || !bank) return;
        var players = PlayerManager.Instance.Players;
        uint mask = availableGems.Value;

        // 一つの宝石には最も近い一人だけが触れられる。同距離ならClientIdで固定。
        for (int i = 0; i < NodeCount; i++)
        {
            if (!gemNodes[i]) continue;
            uint bit = 1u << i;
            if ((mask & bit) == 0 && Elapsed >= nextAppearance[i]) mask |= bit;
            if ((mask & bit) == 0) continue;

            PlayerController winner = null;
            float nearestSquared = float.PositiveInfinity;
            foreach (PlayerController player in players)
            {
                if (!CanInteract(player) || GetCarried(player.OwnerClientId) >= Capacity) continue;
                Vector3 offset = player.transform.position - gemNodes[i].position;
                if (!IsWithinReach(offset, pickupRadius, interactionHeight)) continue;
                float distanceSquared = offset.x * offset.x + offset.z * offset.z;
                if (winner != null && (distanceSquared > nearestSquared
                    || (distanceSquared == nearestSquared && player.OwnerClientId >= winner.OwnerClientId))) continue;
                winner = player;
                nearestSquared = distanceSquared;
            }

            if (!winner) continue;
            SetCarried(winner.OwnerClientId, GetCarried(winner.OwnerClientId) + 1);
            mask &= ~bit;
            nextAppearance[i] = Elapsed + Mathf.Max(.1f, gemRespawnSeconds);
        }
        if (availableGems.Value != mask) availableGems.Value = mask;

        foreach (PlayerController player in players)
        {
            if (!CanInteract(player)) continue;
            int amount = GetCarried(player.OwnerClientId);
            if (amount <= 0 || !IsWithinReach(player.transform.position - bank.position,
                bankRadius, interactionHeight)) continue;
            if (!player.TryGetComponent<PlayerStatus>(out var status) || !status.IsSpawned) continue;
            status.AddRoundScore(amount);
            SetCarried(player.OwnerClientId, 0);
        }

        // 切断したプレイヤーの手持ちは次回の接続やラウンドに持ち越さない。
        for (int i = carried.Count - 1; i >= 0; i--)
        {
            bool present = false;
            foreach (PlayerController player in players)
            {
                if (player && player.IsSpawned && player.OwnerClientId == carried[i].ClientId)
                {
                    present = true;
                    break;
                }
            }
            if (!present) carried.RemoveAt(i);
        }
    }

    private static bool CanInteract(PlayerController player)
    {
        return player && player.IsSpawned && player.isActiveAndEnabled && !player.IsEliminated;
    }

    /// <summary>足元で円柱判定する。低いジャンプを許容し、床下や高すぎる位置は除外する。</summary>
    public static bool IsWithinReach(Vector3 offset, float radius, float height)
    {
        return offset.y >= -.35f && offset.y <= Mathf.Max(.1f, height)
            && offset.x * offset.x + offset.z * offset.z <= Mathf.Max(.1f, radius) * Mathf.Max(.1f, radius);
    }

    protected override void PlayerRespawned(PlayerController player)
    {
        if (player) SetCarried(player.OwnerClientId, 0);
    }

    public int GetCarried(ulong clientId)
    {
        if (carried == null) return 0;
        for (int i = 0; i < carried.Count; i++)
            if (carried[i].ClientId == clientId) return carried[i].Count;
        return 0;
    }

    private void SetCarried(ulong clientId, int amount)
    {
        amount = Mathf.Clamp(amount, 0, Capacity);
        for (int i = 0; i < carried.Count; i++)
        {
            if (carried[i].ClientId != clientId) continue;
            if (carried[i].Count != amount) carried[i] = new CarriedGems(clientId, amount);
            return;
        }
        if (amount > 0) carried.Add(new CarriedGems(clientId, amount));
    }

    public override string GetStatus(ulong clientId)
    {
        int score = 0;
        if (PlayerManager.Instance != null)
        {
            foreach (PlayerController player in PlayerManager.Instance.AllPlayers)
                if (player && player.OwnerClientId == clientId && player.TryGetComponent<PlayerStatus>(out var status))
                {
                    score = status.RoundScore;
                    break;
                }
        }
        int amount = GetCarried(clientId);
        return $"納品 {score} 個  |  手持ち {amount}/{Capacity}"
            + (amount >= Capacity ? "  満杯！金庫へ" : "");
    }

    private void CacheVisuals()
    {
        int count = gemVisuals == null ? 0 : gemVisuals.Length;
        visualOrigins = new Vector3[count];
        visualRotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            if (!gemVisuals[i]) continue;
            visualOrigins[i] = gemVisuals[i].localPosition;
            visualRotations[i] = gemVisuals[i].localRotation;
        }
    }

    protected override void VisualTick(float deltaTime)
    {
        if (gemVisuals == null || visualOrigins.Length != gemVisuals.Length) return;
        float time = IsSpawned ? (float)NetworkManager.ServerTime.Time : Time.time;
        for (int i = 0; i < gemVisuals.Length; i++)
        {
            Transform visual = gemVisuals[i];
            if (!visual || i >= NodeCount || visual == gemNodes[i]) continue;
            bool visible = !IsSpawned || (availableGems.Value & (1u << i)) != 0;
            if (visual.gameObject.activeSelf != visible) visual.gameObject.SetActive(visible);
            if (!visible) continue;
            visual.localPosition = visualOrigins[i] + Vector3.up * (.16f * Mathf.Sin(time * 2.6f + i));
            visual.localRotation = Quaternion.Euler(0f, time * 65f + i * 25f, 0f) * visualRotations[i];
        }
    }

    public struct CarriedGems : INetworkSerializable, IEquatable<CarriedGems>
    {
        public ulong ClientId;
        public int Count;
        public CarriedGems(ulong clientId, int count) { ClientId = clientId; Count = count; }
        public bool Equals(CarriedGems other) => ClientId == other.ClientId && Count == other.Count;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Count);
        }
    }
}
