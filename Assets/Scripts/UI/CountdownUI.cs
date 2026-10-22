using UnityEngine;
using TMPro;

public class CountdownUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;
    private RoundManager observedRound;
    private bool countdownPlayed;

    void Update()
    {
        if (this.countdownText == null) return;

        var round = RoundManager.Instance;
        if (round != this.observedRound)
        {
            this.observedRound = round;
            this.countdownPlayed = false;
        }

        if (round == null || !round.IsSpawned || round.IsRoundOver)
        {
            this.SetVisible(false);
            return;
        }

        if (RoundManager.Instance.IsIntro)
        {
            this.SetVisible(false);
            return;
        }


        float remaining = RoundManager.Instance.CountdownRemaining;

        if (remaining <= 0f)
        {
            this.SetVisible(false);
            return;
        }

        this.SetVisible(true);

        // 3・2・1・Startをまとめた音源を、表示開始時に一度だけ再生する。
        if (!this.countdownPlayed)
        {
            this.countdownPlayed = true;
            SEAudio.Instance?.Play(SEType.Countdown);
        }

        // 最後の1秒は "Start!"、それより前は 3・2・1
        this.countdownText.text = remaining <= RoundManager.StartDisplayDuration
            ? "Start!"
            : Mathf.CeilToInt(remaining - RoundManager.StartDisplayDuration).ToString();
    }

    private void SetVisible(bool visible)
    {
        if (this.countdownText.gameObject.activeSelf != visible)
        {
            this.countdownText.gameObject.SetActive(visible);
        }
    }
}
