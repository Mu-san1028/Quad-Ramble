using UnityEngine;
using System.Collections;
using TMPro;

public class GameManager : MonoBehaviour
{
    private GameState currentState = GameState.WaitingForPlayers;
    public static GameManager Instance { get; private set; }
    [SerializeField] private float timeLimit = 90f;
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


}
public enum GameState
{
    WaitingForPlayers,
    Countdown,
    Playing,
    GameOver
}
