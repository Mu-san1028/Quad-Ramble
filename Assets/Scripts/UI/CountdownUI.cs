using UnityEngine;
using TMPro;

public class CountdownUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;

    void Update()
    {
        if (this.countdownText == null) return;

        if (RoundManager.Instance == null)
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

        // 最後の1秒は "Start!"、それより前は 3・2・1
        this.countdownText.text = remaining <= 1f
            ? "Start!"
            : Mathf.CeilToInt(remaining - 1f).ToString();
    }

    private void SetVisible(bool visible)
    {
        if (this.countdownText.gameObject.activeSelf != visible)
        {
            this.countdownText.gameObject.SetActive(visible);
        }
    }
}
