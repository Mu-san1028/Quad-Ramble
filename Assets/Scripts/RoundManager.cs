using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;

public class RoundManager : MonoBehaviour
{
    public timerUI timerUI;         // タイマーと紐付け
    public string nextSceneName;      // 次のシーン名
    public int survivingPlayers = 4;  // 生存プレイヤー数

    private bool _isRoundOver = false;

    void Update()
    {
        if (_isRoundOver) return;

        // タイマーが0になったらラウンド終了
        if (timerUI != null && timerUI.GetCurrentTime() <= 0)
        {
            StartCoroutine(EndRoundRoutine("サバイバル終了！"));
        }
    }

    // プレイヤーが0人になったらラウンド終了
    public void OnPlayerDeath()
    {
        if (_isRoundOver) return;

        survivingPlayers--;
        Debug.Log("残り人数: " + survivingPlayers);

        if (survivingPlayers <= 1)
        {
            StartCoroutine(EndRoundRoutine("タイムアップ！"));
        }
    }

    // シーン切り替え
    IEnumerator EndRoundRoutine(string message)
    {
        _isRoundOver = true;
        Debug.Log(message);

        yield return new WaitForSeconds(3f);

        // ネットワーク対応しました
        NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single); 
    }
}