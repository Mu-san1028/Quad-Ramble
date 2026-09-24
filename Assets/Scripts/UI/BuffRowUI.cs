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

    [Header("表示")]
    [SerializeField] private Color rowColor = Color.white;

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
    }

    public void Setup(string displayName, Color color, int count)
    {
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
