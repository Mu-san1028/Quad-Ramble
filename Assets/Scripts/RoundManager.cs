using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }

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

    public Items[] BlockDropItems => this.blockDropItems;
    public Items[] AvailableItems => this.availableItems;

    private PlayerSpawner playerSpawner;
    private RoundScoring scoring;
    private bool isRoundOver = false;
    [SerializeField] private float introDuration = 5f;
    private readonly NetworkVariable<float> introRemaining = new ();
    public float IntroRemaining => this.introRemaining.Value;
    public bool IsIntro => this.introRemaining.Value > 0f;
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

        this.playerSpawner = FindAnyObjectByType<PlayerSpawner>();
        this.scoring = FindAnyObjectByType<RoundScoring>();

        GameManager.Instance.SetState(GameState.Countdown);
        PlayerManager.Instance.ResetPlayers();

        var activePlayers = PlayerManager.Instance.Players;
        for (int i = 0; i < activePlayers.Count; i++)
        {
            this.playerSpawner.SpawnPlayer(activePlayers[i], i);
            activePlayers[i].StopOrMovePlayer(false);
        }

        this.remainingTime.Value = this.roundDuration;
        this.countdownRemaining.Value = this.countdownDuration;
        this.introRemaining.Value = this.introDuration;

    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (!IsServer || this.isRoundOver) return;

        if (this.introRemaining.Value > 0f)
        {
            this.introRemaining.Value = Mathf.Max(0f, this.introRemaining.Value - Time.deltaTime);
            return;
        }

        // カウントダウン中はラウンド時間を減らさない
        if (this.countdownRemaining.Value > 0f)
        {
            this.countdownRemaining.Value -= Time.deltaTime;
            if (this.countdownRemaining.Value <= 0f)
            {
                this.countdownRemaining.Value = 0f;
                this.StartPlaying();
            }
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
        if (this.isRoundOver) return;
        StartCoroutine(this.EndRoundRoutine("Finish !"));
    }

    // シーン切り替え
    IEnumerator EndRoundRoutine(string message)
    {
        this.isRoundOver = true;
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
