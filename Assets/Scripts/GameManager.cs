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
            && ConnectionContainer.Instance.GetConnectionsNum() >= requiredPlayers)
        {
            currentState = GameState.Countdown;
            LoadNextScene();
        }
    }


    public void LoadNextScene()
    {
        if (NetworkManager.Singleton == null) return; 
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う

        if (CurrentRound < stageNames.Length)
        {
            string nextScene = stageNames[CurrentRound];
            CurrentRound++;
            NetworkManager.Singleton.SceneManager.LoadScene(nextScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            if (ConnectionContainer.Instance != null)
            {
                ConnectionContainer.Instance.BroadcastFinalScores();
            }
            // 全ラウンド終了後、結果画面に遷移
            NetworkManager.Singleton.SceneManager.LoadScene(resultSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
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
