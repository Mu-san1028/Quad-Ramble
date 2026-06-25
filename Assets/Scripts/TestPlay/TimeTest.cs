using UnityEngine;

public class TimeTest : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        // ゲーム開始からの経過時間（Time.time）を表示
        // タイムスケールが0のときは増加しません
        Debug.Log($"経過時間: {Time.time}");

        // タイムスケールに関わらず、純粋な実時間を確認したい場合
        Debug.Log($"実際の経過時間: {Time.unscaledTime}");
    }
}