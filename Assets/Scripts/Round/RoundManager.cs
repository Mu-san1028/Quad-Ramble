using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }
    public const float StartDisplayDuration = 1f;
    private bool playingStarted;
    private NetworkSceneManager loadingSceneManager;
    private readonly NetworkVariable<bool> spawnReady = new(false);
    public bool IsReady => this.spawnReady.Value;
    private readonly NetworkVariable<double> presentationStart = new(-1d);
    public bool PresentationStarted => this.IsReady && this.presentationStart.Value >= 0d
        && NetworkManager.ServerTime.Time >= this.presentationStart.Value
        && !SceneTransitionUI.PresentationBlocked;
    public float IntroElapsed => this.PresentationStarted
        ? (float)(NetworkManager.ServerTime.Time - this.presentationStart.Value) : 0f;

    [SerializeField] private bool tutorialMode;

    [Header("ラウンド設定")]
    [SerializeField] private float roundDuration = 90f;     // このステージの制限時間
    [SerializeField] private float countdownDuration = 4f;  // 3・2・1・Start! の合計

    [Header("このラウンドに出現するアイテム")]
    [SerializeField] private Items[] availableItems;
    [Tooltip("ItemBox_rareから抽選するアイテム。空の場合は取得できません。")]
    [SerializeField] private Items[] rareItems = new Items[0];
    [SerializeField] private Items[] blockDropItems;

    [Header("ラウンドの情報")]
    [SerializeField] private string stageName;
    [SerializeField] private string author;
    [SerializeField] private string description;

    public string StageName   => this.stageName;
    public string Author      => this.author;
    public string Description => this.description;

    [Header("このラウンドのBGM")]
    [SerializeField] private AudioClip introBgm;   // イントロ〜カウントダウン中の静かな層
    [SerializeField] private AudioClip mainBgm;    // Start! 以降の本編

    // 未設定なら RoundAudio 側の既定曲が使われる
    public AudioClip IntroBgm => this.introBgm;
    public AudioClip MainBgm  => this.mainBgm;

    public Items[] BlockDropItems => this.blockDropItems;
    public Items[] AvailableItems => this.availableItems;
    public Items[] RareItems => this.rareItems;

    private PlayerSpawner playerSpawner;
    private RoundScoring scoring;
    private readonly NetworkVariable<bool> isRoundOver = new(false);
    public bool IsRoundOver => isRoundOver.Value;
    [SerializeField] private float introDuration = 5f;
    private readonly NetworkVariable<float> introRemaining = new ();
    public float IntroRemaining => this.introRemaining.Value;
    public bool IsIntro => !this.PresentationStarted || this.introRemaining.Value > 0f;
    private readonly WaitForSeconds beforeSceneTransition = new (3);

    public RoundScoreEntry[] RoundResults { get; private set; }
    public double ResultStartTime { get; private set; }
    public Vector4 ResultTimings { get; private set; }
    public bool HideFinishForResults => this.RoundResults != null
        && NetworkManager.ServerTime.Time >= this.ResultStartTime + this.ResultTimings.x;

    // サーバーだけが書き込み、全員が読む
    private readonly NetworkVariable<float> remainingTime = new ();
    private readonly NetworkVariable<float> countdownRemaining = new ();

    public float RemainingTime => this.remainingTime.Value;
    public float RoundDuration => this.roundDuration;
    public float CountdownRemaining => this.countdownRemaining.Value;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (!IsServer) return;

        this.playingStarted = false;

        this.playerSpawner = FindAnyObjectByType<PlayerSpawner>();
        this.scoring = FindAnyObjectByType<RoundScoring>();

        GameManager.Instance.SetState(GameState.Countdown);

        // Titleが残っているクライアントへ、先にテレポートを送らない。
        if (NetworkManager.NetworkConfig.EnableSceneManagement && GameManager.Instance.CurrentRound > 0)
        {
            this.loadingSceneManager = NetworkManager.SceneManager;
            this.loadingSceneManager.OnLoadEventCompleted += this.HandleLoadCompleted;
            return;
        }

        // ステージを直接開いてホストを開始した場合はLoadイベントが発生しない。
        if (this.tutorialMode) StartCoroutine(this.InitializeTutorial());
        else this.InitializePlayers();
    }

    private IEnumerator InitializeTutorial()
    {
        while (IsSpawned && PlayerItem.Local == null) yield return null;
        if (IsSpawned) this.InitializePlayers();
    }

    private void HandleLoadCompleted(string sceneName, LoadSceneMode mode,
        List<ulong> completed, List<ulong> timedOut)
    {
        if (!IsServer || !IsSpawned || sceneName != gameObject.scene.name)
            return;

        this.UnsubscribeSceneLoading();

        // 読み込みに失敗した端末をTitleに残したまま参加させない。
        foreach (ulong clientId in timedOut)
        {
            Debug.LogWarning($"[RoundSpawn] Scene load timed out: ClientId={clientId}", this);
            if (clientId != NetworkManager.ServerClientId && NetworkManager.ConnectedClients.ContainsKey(clientId))
                NetworkManager.DisconnectClient(clientId);
        }

        this.InitializePlayers(completed);
    }

    private void InitializePlayers(List<ulong> loadedClients = null)
    {
        if (!IsServer || !IsSpawned || this.IsReady)
            return;

        PlayerManager.Instance.ResetPlayers();

        var activePlayers = PlayerManager.Instance.Players;
        activePlayers.RemoveAll(player => player == null || !player.IsSpawned
            || (loadedClients != null && !loadedClients.Contains(player.OwnerClientId)));

        for (int i = 0; i < activePlayers.Count; i++)
        {
            this.playerSpawner.SpawnPlayer(activePlayers[i], i);
            activePlayers[i].StopOrMovePlayer(false);
        }

        this.remainingTime.Value = this.roundDuration;
        this.countdownRemaining.Value = this.countdownDuration;
        var introCamera = FindAnyObjectByType<StageIntroCamera>();
        if (introCamera != null)
            this.introDuration = Mathf.Max(this.introDuration, introCamera.RouteDuration);
        this.introRemaining.Value = this.introDuration;
        this.spawnReady.Value = true;

        if (ConnectionContainer.Instance == null || !ConnectionContainer.Instance.IsTransitioning)
            this.BeginPresentation();
    }

    /// <summary>全端末の準備完了後、暗転解除と紹介の共通開始時刻を配信する。</summary>
    public void BeginPresentation(double startAt = -1d)
    {
        if (!IsServer || !this.IsReady || this.presentationStart.Value >= 0d) return;
        this.presentationStart.Value = startAt >= 0d ? startAt : NetworkManager.ServerTime.Time + 0.25d;
    }

    private void UnsubscribeSceneLoading()
    {
        if (this.loadingSceneManager == null)
            return;

        this.loadingSceneManager.OnLoadEventCompleted -= this.HandleLoadCompleted;
        this.loadingSceneManager = null;
    }

    public override void OnNetworkDespawn()
    {
        this.UnsubscribeSceneLoading();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsServer || !this.PresentationStarted || this.IsRoundOver) return;

        if (this.introRemaining.Value > 0f)
        {
            this.introRemaining.Value = Mathf.Max(0f, this.introDuration - this.IntroElapsed);
            return;
        }

        // 遅い端末の暗転解除が残っている間は試合を始めない。
        if (ConnectionContainer.Instance != null && ConnectionContainer.Instance.IsTransitioning) return;

        // 最後の1秒はStart表示を残したままプレイする。
        if (this.countdownRemaining.Value > 0f)
        {
            this.countdownRemaining.Value = Mathf.Max(0f,
                this.countdownRemaining.Value - Time.deltaTime);
        }

        if (this.countdownRemaining.Value > StartDisplayDuration)
            return;

        if (!this.playingStarted)
        {
            this.playingStarted = true;
            this.StartPlaying();
            return;
        }

        if (this.tutorialMode) return;

        this.remainingTime.Value -= Time.deltaTime;
        if (this.remainingTime.Value <= 0f)
        {
            this.remainingTime.Value = 0f;
            StartCoroutine(this.EndRoundRoutine("Time's up !"));
        }
    }

    private void StartPlaying()
    {
        foreach (var player in PlayerManager.Instance.Players)
        {
            player.StopOrMovePlayer(true);
        }
        GameManager.Instance.SetState(GameState.Playing);
    }
    
    public void EndRound()
    {
        if (this.tutorialMode) return;
        if (!IsServer || !this.IsReady || this.IsRoundOver) return;
        StartCoroutine(this.EndRoundRoutine("Finish !"));
    }

    // シーン切り替え
    IEnumerator EndRoundRoutine(string message)
    {
        if (!IsServer || this.IsRoundOver) yield break;

        this.isRoundOver.Value = true;
        foreach (var player in PlayerManager.Instance.AllPlayers)
        {
            if (player != null && player.IsSpawned)
                player.StopOrMovePlayer(false);
        }

        GameManager.Instance.SetState(GameState.Countdown);
        var results = new List<RoundScoreEntry>();
        var roster = ConnectionContainer.Instance;
        foreach (var player in PlayerManager.Instance.AllPlayers)
        {
            if (player == null) continue;
            var connection = roster != null ? roster.GetConnection(player.OwnerClientId) : null;
            if (connection == null) continue;

            results.Add(new RoundScoreEntry
            {
                ClientId = connection.ClientId,
                PlayerId = connection.Id,
                Before = connection.Score
            });
        }

        if (this.scoring != null) this.scoring.AwardPoints();
        else this.AwardPoints();

        for (int i = 0; i < results.Count; i++)
        {
            var entry = results[i];
            entry.After = roster.GetConnection(entry.ClientId)?.Score ?? entry.Before;
            results[i] = entry;
        }

        var resultUI = FindAnyObjectByType<RoundResultUI>();
        double resultEndTime = 0;
        if (resultUI != null && resultUI.isActiveAndEnabled && results.Count > 0)
        {
            Vector4 timings = resultUI.Timings;
            double startTime = NetworkManager.ServerTime.Time + 0.2;
            resultEndTime = startTime + timings.x + timings.y + timings.z + timings.w + resultUI.HoldDuration;
            this.ShowRoundResultsRpc(results.ToArray(), startTime, timings);
        }

        ConnectionContainer.Instance?.BroadcastScores();
        Debug.Log(message);

        if (resultEndTime > 0)
        {
            while (IsSpawned && NetworkManager.ServerTime.Time < resultEndTime)
                yield return null;
        }
        else
        {
            yield return this.beforeSceneTransition;
        }

        if (!IsServer || !IsSpawned) yield break;
        GameManager.Instance.LoadNextScene();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowRoundResultsRpc(RoundScoreEntry[] entries, double startTime, Vector4 timings)
    {
        this.RoundResults = entries;
        this.ResultStartTime = startTime;
        this.ResultTimings = timings;
    }

    private void AwardPoints()
    {
        if (!IsServer) return;
        var ranking = PlayerManager.Instance.GetRanking();
        int rank = 1;

        foreach (var players in ranking)
        {
            int points = GameManager.Instance.GetPointsForRank(rank);
            foreach (var player in players)
            {
                var connection = ConnectionContainer.Instance.GetConnection(player.OwnerClientId);
                connection?.AddScore(points);
            }
            rank += players.Count;
        }
    }
}
