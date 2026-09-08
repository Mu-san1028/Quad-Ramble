using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public GameState currentState = GameState.WaitingForPlayers;
    public static GameManager Instance { get; private set; }
    [SerializeField] public int requiredPlayers = 4;
    [SerializeField] private int[] rankPoints = new int[] { 10, 7, 5, 3 };
    [SerializeField] private string[] stageNames = new string[] { "RoundA", "RoundB", "RoundC" };
    [SerializeField] private string titleSceneName = "Title";
    [SerializeField] private string resultSceneName = "ResultScene";
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
            && ConnectionContainer.Instance.IsEveryoneReady(requiredPlayers))
        {
            currentState = GameState.Countdown;
            LoadNextScene();
        }
    }


    public void LoadNextScene()
    {
        if (NetworkManager.Singleton == null) return; 
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う

        var sceneManager = NetworkManager.Singleton.SceneManager;
        if (sceneManager == null)
        {
            Debug.LogError("SceneManager が取得できませんでした。ネットワークが開始されていない可能性があります");
            return;
        }

        // 読み込めないステージ（Build Settings 未登録・名前違いなど）で
        // 止まらないよう、次のステージへ送る
        while (CurrentRound < stageNames.Length)
        {
            string nextScene = stageNames[CurrentRound];
            CurrentRound++;
            ConnectionContainer.Instance?.SetCurrentRound(CurrentRound);

            var status = sceneManager.LoadScene(nextScene, LoadSceneMode.Single);
            if (status == SceneEventProgressStatus.Started) return;

            Debug.LogError($"ステージ '{nextScene}' を読み込めませんでした ({status})。Build Settings のシーン一覧に登録されているか確認してください");
        }

        if (ConnectionContainer.Instance != null)
        {
            ConnectionContainer.Instance.BroadcastFinalScores();
        }
        // 全ラウンド終了後、結果画面に遷移
        var resultStatus = sceneManager.LoadScene(resultSceneName, LoadSceneMode.Single);
        if (resultStatus != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"結果画面 '{resultSceneName}' を読み込めませんでした ({resultStatus})");
        }
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
    Playing,
    GameOver
}
