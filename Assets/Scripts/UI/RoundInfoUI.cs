using UnityEngine;
using TMPro;

/// <summary>
/// ラウンド開始前に、ステージ名・制作者・説明を表示する。
/// 幕が開き切ってから左上へ滑り込み、紹介終了時に左の画面外へ戻る。
/// </summary>
public class RoundInfoUI : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI stageNameText;
    [SerializeField] private TextMeshProUGUI authorText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private RectTransform titleBanner;
    [SerializeField] private RectTransform descriptionBanner;

    [Header("スライド")]
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private float startOffset = -1.2f;   // 画面幅に対する開始位置。負が左の画面外
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Min(0f)] private float descriptionDelay = 0.2f;
    [SerializeField, Min(0.01f)] private float exitDuration = 0.4f;
    [SerializeField, Min(0f)] private float exitStagger = 0.1f;

    private float elapsed;
    private Vector2 titlePosition;
    private Vector2 descriptionPosition;
    private bool filled;

    private void Awake()
    {
        if (this.titleBanner != null) this.titlePosition = this.titleBanner.anchoredPosition;
        if (this.descriptionBanner != null) this.descriptionPosition = this.descriptionBanner.anchoredPosition;
        if (this.panel != null) this.panel.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        this.filled = false;
        this.elapsed = 0f;
    }

    private void Update()
    {
        var round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || !round.IsReady)
        {
            this.panel.gameObject.SetActive(false);
            return;
        }

        if (round.IsIntro)
        {
            if (!this.filled) this.Fill(round);
            this.Slide(round.IntroRemaining);
            return;
        }

        this.panel.gameObject.SetActive(false);
        this.enabled = false;
    }

    private void Fill(RoundManager round)
    {
        this.stageNameText.text   = round.StageName;
        this.descriptionText.text = round.Description;
        this.authorText.text      = $"created by {round.Author}";

        // 表示する前に画面外へ寄せておく。1フレームだけ中央に見えるのを防ぐ
        this.elapsed = 0f;
        this.filled = true;
        if (this.titleBanner != null && this.descriptionBanner != null)
        {
            this.MoveBanner(this.titleBanner, this.titlePosition, 0f, 0f);
            this.MoveBanner(this.descriptionBanner, this.descriptionPosition, 0f, 0f);
        }
        else this.SetOffset(this.startOffset);
        this.panel.gameObject.SetActive(true);
    }

    private void Slide(float remaining)
    {
        // 幕が開き切るまでは動かさない。幕の秒数をこちらに書き写さなくて済む
        if (SceneTransitionUI.Instance != null && SceneTransitionUI.Instance.IsCovering) return;

        // 読み込み直後の1フレームは deltaTime が跳ねるので上限を掛ける
        this.elapsed += Mathf.Min(Time.deltaTime, 0.05f);

        if (this.titleBanner != null && this.descriptionBanner != null)
        {
            float exitElapsed = this.exitDuration + this.exitStagger - remaining;
            this.MoveBanner(this.titleBanner, this.titlePosition,
                this.Progress(this.elapsed, this.slideDuration), this.Progress(exitElapsed, this.exitDuration));
            this.MoveBanner(this.descriptionBanner, this.descriptionPosition,
                this.Progress(this.elapsed - this.descriptionDelay, this.slideDuration),
                this.Progress(exitElapsed - this.exitStagger, this.exitDuration));
            return;
        }

        float t = this.slideDuration <= 0f ? 1f : Mathf.Clamp01(this.elapsed / this.slideDuration);
        float offset = Mathf.Lerp(this.startOffset, 0f, this.slideCurve.Evaluate(t));
        this.SetOffset(Mathf.Lerp(offset, this.startOffset,
            this.Progress(this.exitDuration - remaining, this.exitDuration)));
    }

    private float Progress(float time, float duration)
    {
        float t = Mathf.Clamp01(time / Mathf.Max(0.01f, duration));
        return Mathf.Clamp01(this.slideCurve.Evaluate(t));
    }

    private void MoveBanner(RectTransform banner, Vector2 resting, float enter, float exit)
    {
        var parent = (RectTransform)banner.parent;
        // アンカーと実際の縮尺から画面左外を求める。解像度変更でも左上の停止位置を保つ。
        float anchorX = Mathf.Lerp(parent.rect.xMin, parent.rect.xMax,
            Mathf.Lerp(banner.anchorMin.x, banner.anchorMax.x, banner.pivot.x));
        float rightExtent = banner.rect.width * Mathf.Abs(banner.localScale.x) * (1f - banner.pivot.x);
        float hiddenX = parent.rect.xMin - anchorX - rightExtent - 32f;
        float x = Mathf.Lerp(hiddenX, resting.x, enter);
        banner.anchoredPosition = new Vector2(Mathf.Lerp(x, hiddenX, exit), resting.y);
    }

    /// <summary>画面幅に対する横位置。0 が中央</summary>
    private void SetOffset(float normalized)
    {
        float width = ((RectTransform)this.panel.parent).rect.width;
        this.panel.anchoredPosition = new Vector2(normalized * width, 0f);
    }
}
