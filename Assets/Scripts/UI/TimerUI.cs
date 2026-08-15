using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TimerUI : MonoBehaviour
{
    [Header("UIコンポーネント")]
    public TextMeshProUGUI timerText; // 残り時間の数字
    public Image timerImage;     // タイマーの背景画像

    [Header("カラー設定")]
    public Color normalColor = Color.gray;  // 通常時
    public Color warningColor = Color.yellow; // 半分以下
    public Color criticalColor = Color.red;    // 10秒以下


    void Update()
    {
        if (RoundManager.Instance == null) return;

        float currentTime = RoundManager.Instance.RemainingTime;
        this.DisplayTime(currentTime);
        this.UpdateUIColor(currentTime, RoundManager.Instance.RoundDuration);
    }

    // 時間の表示形式
    void DisplayTime(float timeToDisplay)
    {
        timerText.text = Mathf.CeilToInt(timeToDisplay).ToString();
    }

    // 残り時間に合わせて色の変更
    void UpdateUIColor(float time, float totalTime)
    {
        Color targetColor;

        if (time <= 10f)
        {
            targetColor = criticalColor;
        }
        else if (time <= totalTime / 3f)
        {
            targetColor = warningColor;
        }
        else
        {
            targetColor = normalColor;
        }

        // テキストと背景画像の両方の色を更新
        if (timerText != null) timerText.color = targetColor;
    }
}
