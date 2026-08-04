using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;

public class RoundManager : MonoBehaviour
{
    public static RoundManager Instance { get; private set; }
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
        float remaining = 3f;
        while (remaining > 0f)
        {
            // ここがカウントダウンUI担当の人の差し込み口（残り秒数を配信する）
            remaining -= Time.deltaTime;
            yield return null;
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
