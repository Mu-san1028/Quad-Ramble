using System;
using TMPro;
using UnityEngine;

/// <summary>確定した疲労増加量を、攻撃者の画面だけに表示する。</summary>
public class DamagePopupUI : MonoBehaviour
{
    [Serializable]
    private class DamageColorStep
    {
        [Min(0f)] public float minimumDamage;
        public TMP_ColorGradient gradient;
        [Tooltip("この段階の文字サイズ。0ならDamageTextの基本サイズを使用します。")]
        [Min(0f)] public float fontSize;
    }

    [SerializeField] private TextMeshProUGUI label;
    [Tooltip("クリティカル時だけ表示。文字・色・配置はこのTMPオブジェクトで編集します。")]
    [SerializeField] private TextMeshProUGUI criticalLabel;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private DamageColorStep[] colorSteps;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.3f, 0f);
    [SerializeField] private Vector2 screenOffset;
    [SerializeField, Min(0.05f)] private float duration = 0.6f;
    [SerializeField, Range(0f, 0.95f)] private float fadeStart = 0.45f;
    [SerializeField] private float riseDistance = 65f;
    [SerializeField, Min(0f)] private float horizontalSpread = 24f;
    [SerializeField, Min(0.01f)] private float popDuration = 0.13f;
    [SerializeField, Min(0.01f)] private float startScale = 0.75f;
    [SerializeField, Min(0.01f)] private float peakScale = 1.15f;

    private Vector3 worldPosition;
    private Vector3 baseScale;
    private Camera viewCamera;
    private float elapsed;
    private float horizontalOffset;
    private int round;
    private bool playing;

    public void Play(float damage, Vector3 position, int roundNumber, int sequence, bool critical = false)
    {
        if (label == null || group == null || canvasRect == null)
        {
            Destroy(gameObject);
            return;
        }

        worldPosition = position + worldOffset;
        if (criticalLabel != null)
        {
            criticalLabel.raycastTarget = false;
            criticalLabel.gameObject.SetActive(critical);
        }
        round = roundNumber;
        viewCamera = Camera.main;
        baseScale = label.rectTransform.localScale;
        horizontalOffset = ((sequence % 3) - 1) * horizontalSpread;
        label.text = Mathf.FloorToInt(damage).ToString(System.Globalization.CultureInfo.InvariantCulture);
        label.raycastTarget = false;
        label.color = Color.white;
        label.enableVertexGradient = true;
        float selectedMinimum = float.NegativeInfinity;
        DamageColorStep selectedStep = null;
        if (colorSteps != null)
        {
            foreach (var step in colorSteps)
            {
                if (step == null || damage < step.minimumDamage
                    || step.minimumDamage < selectedMinimum) continue;

                selectedMinimum = step.minimumDamage;
                selectedStep = step;
            }
        }

        if (selectedStep != null)
        {
            if (selectedStep.gradient != null)
                label.colorGradientPreset = selectedStep.gradient;

            if (selectedStep.fontSize > 0f)
            {
                label.enableAutoSizing = false;
                label.fontSize = selectedStep.fontSize;
            }
        }

        group.interactable = false;
        group.blocksRaycasts = false;
        group.alpha = 0f;
        playing = true;
    }

    private void LateUpdate()
    {
        if (!playing) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= duration || (ConnectionContainer.Instance != null
            && ConnectionContainer.Instance.CurrentRound != round))
        {
            Destroy(gameObject);
            return;
        }

        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) { group.alpha = 0f; return; }

        Vector3 screen = viewCamera.WorldToScreenPoint(worldPosition);
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, duration));
        // カメラの後方や画面外の敵について、端に偽の命中位置を表示しない。
        if (screen.z <= 0f || !viewCamera.pixelRect.Contains(new Vector2(screen.x, screen.y)))
        {
            group.alpha = 0f;
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local);
        label.rectTransform.anchoredPosition = local + screenOffset
            + new Vector2(horizontalOffset, riseDistance * progress);
        float pop = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, popDuration));
        float scale = pop < 0.6f ? Mathf.Lerp(startScale, peakScale, pop / 0.6f)
            : Mathf.Lerp(peakScale, 1f, (pop - 0.6f) / 0.4f);
        label.rectTransform.localScale = baseScale * scale;
        group.alpha = 1f - Mathf.Clamp01((progress - fadeStart) / Mathf.Max(0.01f, 1f - fadeStart));
    }
}
