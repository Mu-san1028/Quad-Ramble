using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuffRowUI : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private Image baseSegment;
    [SerializeField] private RectTransform segmentContainer;
    [SerializeField] private Image segmentPrefab;
    [SerializeField] private TextMeshProUGUI label;

    [Header("背景")]
    [SerializeField] private Image background;
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.08f, 0.18f, 0.6f);
    [SerializeField] private Vector2 backgroundPadding = new Vector2(10f, 5f);
    private readonly Vector3[] backgroundCorners = new Vector3[4];
    private bool backgroundDirty = true;

    [Header("表示")]
    [SerializeField] private Color rowColor = Color.white;

    [Header("減速通知")]
    [SerializeField] private Color slowTextColor = new Color(1f, 0.15f, 0.15f);
    private string labelWithoutSlow;
    private bool showingSlow;

    [Header("獲得時の強調")]
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.15f;
    [SerializeField, Min(0f)] private float highlightDuration = 0.2f;
    [SerializeField, Min(0.01f)] private float settleDuration = 0.3f;

    private Coroutine highlightCoroutine;

    [SerializeField, Range(0f, 1f)]
    private float normalAlpha = 0.65f;

    private readonly List<Image> segments = new();

    [Header("スライド表示")]
    [SerializeField] private RectTransform slideRoot;
    [SerializeField, Min(0f)] private float displayDuration = 2.5f;
    [SerializeField, Min(0.01f)] private float slideDuration = 0.25f;
    [SerializeField, Min(0f)] private float slideDistance = 600f;

    private Coroutine hideCoroutine;
    private Vector2 shownPosition;
    private bool positionInitialized;

    /// <summary>
    /// ベースの台形に加え、count個の平行四辺形を表示する。
    /// </summary>
    public void SetCount(int count)
    {
        this.StopHighlight();
        count = Mathf.Max(0, count);

        // 足りない区画だけ追加し、一度作ったものは使い回す。
        while (this.segments.Count < count)
        {
            Image segment = Instantiate(
                this.segmentPrefab,
                this.segmentContainer
            );

            segment.raycastTarget = false;
            this.segments.Add(segment);
        }

        Color displayColor = this.rowColor;
        displayColor.a = this.normalAlpha;

        this.baseSegment.color = displayColor;

        for (int i = 0; i < this.segments.Count; i++)
        {
            Image segment = this.segments[i];

            segment.gameObject.SetActive(i < count);
            segment.color = displayColor;
        }

        this.backgroundDirty = true;
    }

    public void Setup(string displayName, Color color, int count)
    {
        this.ClearSlow();
        this.label.text = displayName;
        this.rowColor = color;

        this.SetCount(count);
    }

    public void ShowGain(string displayName, Color color, int count)
    {
        if (this.slideRoot == null)
        {
            Debug.LogWarning(
                "BuffRowUI: Slide Rootが未設定です。Inspectorで子のSlideRootを割り当ててください。",
                this
            );
            return;
        }

        this.StopHide();

        if (!this.positionInitialized)
        {
            this.shownPosition = this.slideRoot.anchoredPosition;
            this.positionInitialized = true;
        }

        // 再取得時はスライド途中でも表示位置に戻して、表示時間を数え直す。
        this.slideRoot.anchoredPosition = this.shownPosition;
        this.gameObject.SetActive(true);

        this.Setup(displayName, color, count);

        if (!this.isActiveAndEnabled)
            return;

        if (count > 0)
        {
            Image newestSegment = this.segments[count - 1];

            this.highlightCoroutine = StartCoroutine(
                this.AnimateHighlight(newestSegment)
            );
        }

        this.hideCoroutine = StartCoroutine(this.HideAfterDelay());
    }


    /// <summary>強化段数を保持し、名前から半角スペース2つ分空けて減速率を表示する。</summary>
    public void ShowSlow(string displayName, Color color, int count, float speedMultiplier)
    {
        this.ShowGain(displayName, color, count);

        if (!this.isActiveAndEnabled || this.slideRoot == null)
            return;

        // 減速の被弾で、既存の強化区画を獲得時のように光らせない。
        this.SetCount(count);

        int percent = Mathf.RoundToInt((1f - Mathf.Clamp01(speedMultiplier)) * 100f);
        this.labelWithoutSlow = displayName;
        this.showingSlow = true;
        this.label.richText = true;
        string colorCode = ColorUtility.ToHtmlStringRGBA(this.slowTextColor);
        this.label.text = $"{displayName}  <color=#{colorCode}>-{percent}%</color>";
        this.backgroundDirty = true;
    }


    public void ClearSlow()
    {
        if (!this.showingSlow)
            return;

        this.label.text = this.labelWithoutSlow;
        this.showingSlow = false;
        this.backgroundDirty = true;
    }

    private void OnEnable()
    {
        this.backgroundDirty = true;
    }

    private void OnRectTransformDimensionsChange()
    {
        this.backgroundDirty = true;
    }

    private void OnValidate()
    {
        this.backgroundDirty = true;
    }

    private void LateUpdate()
    {
        if (!this.backgroundDirty || this.background == null || this.slideRoot == null)
            return;

        // 追加・非表示にした区画をレイアウトへ反映してから、実際の表示範囲を測る。
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.slideRoot);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        this.IncludeBackgroundRect(this.baseSegment.rectTransform, ref min, ref max);
        foreach (Image segment in this.segments)
        {
            if (segment.gameObject.activeSelf)
                this.IncludeBackgroundRect(segment.rectTransform, ref min, ref max);
        }

        // Labelは幅400の固定枠なので、枠全体ではなく実際の文字領域を含める。
        this.label.ForceMeshUpdate();
        Bounds textBounds = this.label.textBounds;
        Vector3 textMin = textBounds.min;
        Vector3 textMax = textBounds.max;
        this.IncludeBackgroundPoint(this.label.transform.TransformPoint(textMin), ref min, ref max);
        this.IncludeBackgroundPoint(this.label.transform.TransformPoint(textMax), ref min, ref max);

        Vector2 padding = new Vector2(Mathf.Max(0f, this.backgroundPadding.x), Mathf.Max(0f, this.backgroundPadding.y));
        RectTransform rect = this.background.rectTransform;
        rect.anchorMin = rect.anchorMax = this.slideRoot.pivot;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = min - padding;
        rect.sizeDelta = max - min + padding * 2f;
        this.background.color = this.backgroundColor;
        this.background.raycastTarget = false;
        this.backgroundDirty = false;
    }

    private void IncludeBackgroundRect(RectTransform rect, ref Vector2 min, ref Vector2 max)
    {
        rect.GetWorldCorners(this.backgroundCorners);
        foreach (Vector3 corner in this.backgroundCorners)
            this.IncludeBackgroundPoint(corner, ref min, ref max);
    }

    private void IncludeBackgroundPoint(Vector3 world, ref Vector2 min, ref Vector2 max)
    {
        Vector2 point = this.slideRoot.InverseTransformPoint(world);
        min = Vector2.Min(min, point);
        max = Vector2.Max(max, point);
    }


    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSecondsRealtime(this.displayDuration);

        Vector2 startPosition = this.shownPosition;
        Vector2 endPosition =
            startPosition + Vector2.left * this.slideDistance;

        float elapsed = 0f;

        while (elapsed < this.slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(elapsed / this.slideDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

            this.slideRoot.anchoredPosition = Vector2.Lerp(
                startPosition,
                endPosition,
                easedProgress
            );

            yield return null;
        }

        this.slideRoot.anchoredPosition = endPosition;
        this.hideCoroutine = null;

        this.gameObject.SetActive(false);
    }


    private void StopHide()
    {
        if (this.hideCoroutine == null)
            return;

        StopCoroutine(this.hideCoroutine);
        this.hideCoroutine = null;
    }


    private IEnumerator AnimateHighlight(Image segment)
    {
        yield return this.FadeAlpha(
            segment, 0f, 1f, this.fadeInDuration
        );

        yield return new WaitForSecondsRealtime(this.highlightDuration);

        yield return this.FadeAlpha(
            segment, 1f, this.normalAlpha, this.settleDuration
        );

        this.highlightCoroutine = null;
    }


    private IEnumerator FadeAlpha(
        Image segment,
        float from,
        float to,
        float duration)
    {
        float elapsed = 0f;
        Color color = segment.color;

        color.a = from;
        segment.color = color;

        while (elapsed < duration)
        {
            yield return null;

            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(elapsed / duration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);

            color.a = Mathf.Lerp(from, to, easedProgress);
            segment.color = color;
        }

        color.a = to;
        segment.color = color;
    }


    private void StopHighlight()
    {
        if (this.highlightCoroutine == null)
            return;

        StopCoroutine(this.highlightCoroutine);
        this.highlightCoroutine = null;
    }


    private void OnDisable()
    {
        this.StopHighlight();
        this.StopHide();
        this.ClearSlow();
    }

    [ContextMenu("表示確認/Power・1段")]
    private void PreviewPower()
    {
        if (!Application.isPlaying)
            return;

        this.Setup(
            "Power",
            new Color(1f, 0.25f, 0.15f),
            1
        );
    }

    [ContextMenu("表示確認/Power獲得・2段")]
    private void PreviewPowerGain()
    {
        if (!Application.isPlaying)
            return;

        this.ShowGain(
            "Power",
            new Color(1f, 0.25f, 0.15f),
            2
        );
    }

}
