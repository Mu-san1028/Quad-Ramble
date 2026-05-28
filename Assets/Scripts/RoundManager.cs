using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class RoundManager : MonoBehaviour
{
    public timerUI timerUI;         // タイマーと紐付け
    public string nextSceneName;      // 次のシーン名
    public int survivingPlayers = 4;  //初期プレイヤー数

    private bool _isRoundOver = false;

    void Update()
    {
        if (_isRoundOver) return;

        // 終了条件1:制限時間が終了
        if (timerUI != null && timerUI.GetCurrentTime() <= 0)
        {
            StartCoroutine(EndRoundRoutine("タイムアップ！"));
        }
    }

    // 終了条件2:プレイヤーが1人になる
    public void OnPlayerDeath()
    {
        if (_isRoundOver) return;

        survivingPlayers--;
        Debug.Log("残り人数: " + survivingPlayers);

        if (survivingPlayers <= 1)
        {
            StartCoroutine(EndRoundRoutine("サバイバル終了！"));
        }
    }

    // シーン切り替え
    IEnumerator EndRoundRoutine(string message)
    {
        _isRoundOver = true;
        Debug.Log(message);

        // 3秒待機
        yield return new WaitForSeconds(3f);

        // 次のシーンの読み込み
        SceneManager.LoadScene(nextSceneName);
    }
}