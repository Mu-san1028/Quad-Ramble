using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public enum StageSelectionMode
    {
        Fixed,
        Random
    }

    public GameState currentState = GameState.WaitingForPlayers;
    public static GameManager Instance { get; private set; }
    [SerializeField] public int requiredPlayers = 4;
    [SerializeField] private int[] rankPoints = new int[] { 10, 7, 5, 3 };
    [SerializeField] private string[] stageNames = new string[] { "RoundA", "RoundB", "RoundC" };
    [SerializeField] private string titleSceneName = "Title";
    [SerializeField] private string resultSceneName = "ResultScene";

    [Header("ステージ選択")]
    [SerializeField] private StageSelectionMode stageSelectionMode = StageSelectionMode.Fixed;
    [SerializeField, Min(1)] private int randomStageCount = 3;

    private readonly List<string> selectedStages = new();
    private bool stagesPrepared;

    // 開始済みラウンド数
    public int CurrentRound { get; private set; } = 0;

    public void SetState(GameState newState)
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う
        currentState = newState;
    }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        if (currentState != GameState.WaitingForPlayers) return;
        if (NetworkManager.Singleton == null) return;

        if (NetworkManager.Singleton.IsServer
            && ConnectionContainer.Instance != null
            && ConnectionContainer.Instance.IsEveryoneReady(ConnectionContainer.Instance.RequiredPlayerCount))
        {
            currentState = GameState.Countdown;
            LoadNextScene();
        }
    }


    private void PrepareStages()
    {
        this.selectedStages.Clear();
        this.stagesPrepared = true;

        if (this.stageNames == null)
            return;

        if (this.stageSelectionMode == StageSelectionMode.Fixed)
        {
            this.selectedStages.AddRange(this.stageNames);
            return;
        }

        // 空欄と重複を除き、Inspectorの候補を変更せずに抽選する。
        var candidates = new List<string>();
        var uniqueNames = new HashSet<string>();

        foreach (string stageName in this.stageNames)
        {
            if (string.IsNullOrWhiteSpace(stageName))
                continue;

            string sceneName = stageName.Trim();

            if (uniqueNames.Add(sceneName))
                candidates.Add(sceneName);
        }

        // Fisher–Yatesで並べ替え、先頭から必要数だけ採用する。
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            string temporary = candidates[i];
            candidates[i] = candidates[randomIndex];
            candidates[randomIndex] = temporary;
        }

        int requestedCount = Mathf.Max(1, this.randomStageCount);
        int selectionCount = Mathf.Min(requestedCount, candidates.Count);

        this.selectedStages.AddRange(candidates.GetRange(0, selectionCount));

        if (selectionCount < requestedCount)
            Debug.LogWarning($"ステージ候補が足りないため、{selectionCount}ステージで進行します。", this);
    }


    public void LoadNextScene()
    {
        if (NetworkManager.Singleton == null) return; 
        if (!NetworkManager.Singleton.IsServer) return;

        var sceneManager = NetworkManager.Singleton.SceneManager;
        if (sceneManager == null)
            return;

        // ホストが最初のステージへ進むときに、一度だけ順番を確定する。
        if (!this.stagesPrepared)
            this.PrepareStages();

        // 読み込めないステージ（Build Settings 未登録・名前違いなど）で
        // 止まらないよう、次のステージへ送る
        while (CurrentRound < this.selectedStages.Count)
        {
            string nextScene = this.selectedStages[CurrentRound];
            CurrentRound++;
            ConnectionContainer.Instance?.SetCurrentRound(CurrentRound);

            var status = sceneManager.LoadScene(nextScene, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started) return;
        }

        if (ConnectionContainer.Instance != null)
        {
            ConnectionContainer.Instance.BroadcastFinalScores();
        }
        // 全ラウンド終了後、結果画面に遷移
        sceneManager.LoadScene(resultSceneName, LoadSceneMode.Single);
    }

    public int GetPointsForRank(int rank)
    {
        int index = rank - 1;
        if (index < 0 || index >= rankPoints.Length)
        {
            return 0;
        }
        return rankPoints[index];
    }

    public void ResetForNewGame()
    {
        CurrentRound = 0;
        currentState = GameState.WaitingForPlayers;

        this.selectedStages.Clear();
        this.stagesPrepared = false;
    }

    public void ReturnToTitle()
    {
        StartCoroutine(this.ReturnToTitleRoutine());
    }

    private IEnumerator ReturnToTitleRoutine()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
            while (NetworkManager.Singleton.ShutdownInProgress) yield return null;
        }

        this.ResetForNewGame();
        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
    }
}

public enum GameState
{
    WaitingForPlayers,
    Countdown,
    Playing
}
