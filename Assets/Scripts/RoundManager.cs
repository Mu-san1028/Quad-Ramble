using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;
using TMPro;

public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance { get; private set; }
    private TextMeshProUGUI countdownText; // カウントダウン表示用のテキスト
    private TimerUI timerUI;         // タイマーと紐付け
    private PlayerSpawner playerSpawner; // プレイヤーをスポーンさせるためのスクリプト
    private bool isRoundOver = false;
    private readonly WaitForSeconds beforeSceneTransition = new (3);

    void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("RoundManager follows singleton pattern!");
            Destroy(gameObject);
        }

        this.playerSpawner = FindAnyObjectByType<PlayerSpawner>();
        this.timerUI = FindAnyObjectByType<TimerUI>();
        if (this.countdownText == null)// CountdownTextがまだ割り当てられていない場合、シーン内で探す
        {
            var textObj = GameObject.Find("CountdownText");
            if (textObj != null)
            {
                this.countdownText = textObj.GetComponent<TextMeshProUGUI>();
            }
        }

        //Titleのsceneにあるときはカウントダウンしなくてよいのでその場合の処理
        if (SceneManager.GetActiveScene().name == "Title")
        {
            return;
        }


        if(!NetworkManager.Singleton.IsServer) return;
        GameManager.Instance.SetState(GameState.Countdown);
        PlayerManager.Instance.ResetPlayers();
        var activePlayers = PlayerManager.Instance.Players;
        for (int i = 0; i < activePlayers.Count; i++)
        {
            this.playerSpawner.SpawnPlayer(activePlayers[i], i);
        }

        StartCoroutine(this.CountdownRoutine());
    }

    void Update()
    {
        if (this.isRoundOver) return;

        // タイマーが0になったらラウンド終了
        // サーバー側でのみ処理する
        if (timerUI != null && timerUI.GetCurrentTime() <= 0 && NetworkManager.Singleton.IsServer) 
        {
            StartCoroutine(EndRoundRoutine("タイムアップ！"));
        }
    }

    public void EndRound()
    {
        if (this.isRoundOver) return;
        StartCoroutine(this.EndRoundRoutine("サバイバル終了！"));
    }

    // シーン切り替え
    IEnumerator EndRoundRoutine(string message)
    {
        this.isRoundOver = true;
        GameManager.Instance.SetState(GameState.Countdown);
        AwardPoints();
        Debug.Log(message);

        yield return this.beforeSceneTransition;

        if (!NetworkManager.Singleton.IsServer) yield break;
        GameManager.Instance.LoadNextScene();
    }

    private IEnumerator CountdownRoutine()
    {
        foreach (var player in PlayerManager.Instance.Players)// プレイヤーを一時的に停止させる
        {
            player.StopOrMovePlayer(false);
        }

        if (this.countdownText != null)
        {
            this.countdownText.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("CountdownText is not assigned.");
        }

        float remaining = 3f;
        while (remaining > 0f)
        {
            // ここがカウントダウンUI担当の人の差し込み口（残り秒数を配信する）
            remaining -= Time.deltaTime;
            this.countdownText.text = Mathf.CeilToInt(remaining).ToString();
            yield return null;
        }

        this.countdownText.text = "Start!";
        yield return new WaitForSeconds(1f);
        this.countdownText.gameObject.SetActive(false);

        foreach (var player in PlayerManager.Instance.Players)// プレイヤーを再開させる
        {
            player.StopOrMovePlayer(true);
        }

        GameManager.Instance.SetState(GameState.Playing);
    }

    private void AwardPoints()
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う
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
