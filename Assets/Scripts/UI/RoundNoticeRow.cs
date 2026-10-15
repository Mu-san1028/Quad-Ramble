using TMPro;
using UnityEngine;

/// <summary>通知1行分の文章・色・透明度を設定する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class RoundNoticeRow : MonoBehaviour
{
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private CanvasGroup canvasGroup;

    public bool IsConfigured => messageText != null && canvasGroup != null;

    private void Reset()
    {
        messageText = GetComponentInChildren<TMP_Text>(true);
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        if (messageText)
            messageText.raycastTarget = false;
    }

    public void SetMessage(string message, Color color)
    {
        if (!messageText) return;

        messageText.text = message ?? string.Empty;
        messageText.color = color;
    }

    public void SetAlpha(float alpha)
    {
        if (canvasGroup)
            canvasGroup.alpha = Mathf.Clamp01(alpha);
    }
}
