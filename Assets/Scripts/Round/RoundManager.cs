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

    [Header("ラウンド設定")]
    [SerializeField] private float roundDuration = 90f;     // このステージの制限時間
    [SerializeField] private float countdownDuration = 4f;  // 3・2・1・Start! の合計

    [Header("このラウンドに出現するアイテム")]
    [SerializeField] private Items[] availableItems;
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

    private PlayerSpawner playerSpawner;
    private RoundScoring scoring;
    private readonly NetworkVariable<bool> isRoundOver = new(false);
    public bool IsRoundOver => isRoundOver.Value;
    [SerializeField] private float introDuration = 5f;
    private readonly NetworkVariable<float> introRemaining = new ();
    public float IntroRemaining => this.introRemaining.Value;
    public bool IsIntro => !this.IsReady || this.introRemaining.Value > 0f;
    private readonly WaitForSeconds beforeSceneTransition = new (3);

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
        this.InitializePlayers();
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
        this.introRemaining.Value = this.introDuration;
        this.spawnReady.Value = true;
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
        if (!IsServer || !this.IsReady || this.IsRoundOver) return;

        if (this.introRemaining.Value > 0f)
        {
            this.introRemaining.Value = Mathf.Max(0f, this.introRemaining.Value - Time.deltaTime);
            return;
        }

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
        if (this.scoring != null) this.scoring.AwardPoints();
        else this.AwardPoints();
        Debug.Log(message);

        yield return this.beforeSceneTransition;

        if (!IsServer) yield break;
        GameManager.Instance.LoadNextScene();
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
