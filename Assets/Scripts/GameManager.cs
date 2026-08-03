using UnityEngine;
using Unity.Netcode;

public class GameManager : MonoBehaviour
{
    public GameState currentState = GameState.WaitingForPlayers;
    public static GameManager Instance { get; private set; }
    [SerializeField] private float timeLimit = 90f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
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

    // Update is called once per frame
    void Update()
    {
        if (currentState == GameState.WaitingForPlayers)
        {
            if (NetworkManager.Singleton.IsServer && ConnectionContainer.Instance.GetConnectionsNum() >= 4)
            {
                currentState = GameState.Countdown;
                NetworkManager.Singleton.SceneManager.LoadScene("StageA", UnityEngine.SceneManagement.LoadSceneMode.Single);
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
    }
}
public enum GameState
{
    WaitingForPlayers,
    Countdown,
    Playing,
    GameOver
}
