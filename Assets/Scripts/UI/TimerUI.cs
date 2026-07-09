using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TimerUI : MonoBehaviour
{
    [Header("UIコンポーネント")]
    public TextMeshProUGUI timerText; // 残り時間の数字
    public Image timerImage;     // タイマーの背景画像

    [Header("タイマー設定")]
    public float totalTime = 90f;     // 制限時間(秒)
    private float _currentTime;

    [Header("カラー設定")]
    public Color normalColor = Color.gray;  // 通常時
    public Color warningColor = Color.yellow; // 半分以下
    public Color criticalColor = Color.red;    // 10秒以下

    void Start()
    {
        _currentTime = totalTime;   // 初期時間のセット
    }

    void Update()
    {
        if (_currentTime > 0)
        {
            _currentTime -= Time.deltaTime;
        }
        else
        {
            _currentTime = 0;
            // タイムアップ時の処理とか
        }

        DisplayTime(_currentTime);
        UpdateUIColor(_currentTime);
    }

    // 時間の表示形式
    void DisplayTime(float timeToDisplay)
    {
        int seconds = Mathf.CeilToInt(timeToDisplay);
        timerText.text = seconds.ToString();
    }

    // 残り時間に合わせて色の変更
    void UpdateUIColor(float time)
    {
        Color targetColor;

        if (time <= 10f)
        {
            targetColor = criticalColor;
        }
        else if (time <= totalTime / 3)
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

    public float GetCurrentTime()
    {
        return _currentTime;
    }
}
