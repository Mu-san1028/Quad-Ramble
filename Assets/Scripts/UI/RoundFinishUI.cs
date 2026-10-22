using TMPro;
using UnityEngine;

/// <summary>同期されたラウンド終了に合わせ、右から中央へ終了表示を滑り込ませる。</summary>
public sealed class RoundFinishUI : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TMP_Text finishText;
    [SerializeField] private string message = "Finish！";
    [SerializeField, Min(0f)] private float slideDuration = 0.45f;

    private bool showing;
    private float elapsed;
    private float startX;
    private RoundManager soundRound;

    private void OnEnable()
    {
        showing = false;
        elapsed = 0f;

        if (panel != null && (panel == transform || transform.IsChildOf(panel)))
        {
            Debug.LogWarning("RoundFinishUI: 管理スクリプトは表示Panelの外側に配置してください。", this);
            enabled = false;
            return;
        }

        Hide();
    }

    private void Update()
    {
        if (!panel || !finishText) return;

        var round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || !round.IsRoundOver)
        {
            Hide();
            return;
        }

        if (!showing)
        {
            var parent = panel.parent as RectTransform;
            if (!parent) return;

            finishText.text = message;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            startX = (parent.rect.width + panel.rect.width) * 0.5f + 20f;
            panel.anchoredPosition = new Vector2(startX, 0f);
            panel.gameObject.SetActive(true);
            showing = true;
            elapsed = 0f;

            if (this.soundRound != round)
            {
                this.soundRound = round;
                SEAudio.Instance?.Play(SEType.RoundFinish);
            }

            return;
        }

        elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = slideDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / slideDuration);
        // 素早く入って、中央へ近づくほどゆっくり止まる。
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        panel.anchoredPosition = new Vector2(Mathf.Lerp(startX, 0f, eased), 0f);
    }

    private void Hide()
    {
        if (panel && panel != transform && !transform.IsChildOf(panel))
            panel.gameObject.SetActive(false);

        showing = false;
    }

    private void OnDisable()
    {
        Hide();
    }
}
