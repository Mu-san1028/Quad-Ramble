using UnityEngine;
using Unity.Netcode;
using System.Collections;
using TMPro;

public class GameManager : MonoBehaviour
{
    public GameState currentState = GameState.WaitingForPlayers;
    public static GameManager Instance { get; private set; }
    [SerializeField] private float timeLimit = 90f;
    [SerializeField] private int[] rankPoints = new int[] { 10, 7, 5, 3 };
    [SerializeField] private string[] stageNames = new string[] { "StageA", "StageB", "StageC" };
    [SerializeField] private string resultSceneName = "ResultScene";
    // 開始済みラウンド数
    public int CurrentRound { get; private set; } = 0;
    public void SetState(GameState newState)
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う
        currentState = newState;
    }
    private float countdownTime = 3f;// カウントダウンの時間
    private int countdownCounter;// カウントダウンを画面に表示する用の整数
    [SerializeField] private TextMeshProUGUI countdownText; // カウントダウンを表示するテキスト
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        countdownCounter = (int)countdownTime;
        countdownText.text = countdownCounter.ToString();
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

    // Update is called once per frame
    void Update()
    {
        if (currentState == GameState.WaitingForPlayers)
        {
            if (NetworkManager.Singleton.IsServer 
                && ConnectionContainer.Instance != null
                && ConnectionContainer.Instance.GetConnectionsNum() >= 4)
            {
                currentState = GameState.Countdown;
                LoadNextScene();
            }
        }
        if (currentState == GameState.Playing)
        {
            timeLimit -= Time.deltaTime;
            if (timeLimit <= 0f)
            {
                // タイムアップの処理
            }
        }

        if (currentState == GameState.Countdown)
        {
            // カウントダウンの処理
            countdownTime -= Time.deltaTime;
            if (countdownTime <= 0f)
            {
                currentState = GameState.Playing;
            }
        }
    }

    public void LoadNextScene()
    {
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
}
public enum GameState
{
    WaitingForPlayers,
    Countdown,
    Playing,
    GameOver
}
