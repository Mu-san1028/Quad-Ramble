using UnityEngine;
using TMPro;

/// <summary>
/// ラウンド開始前に、ステージ名・制作者・説明を表示する。
/// 幕が開き切ってから、左の画面外を出発して中央へ滑り込む。
/// </summary>
public class RoundInfoUI : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI stageNameText;
    [SerializeField] private TextMeshProUGUI authorText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("スライド")]
    [SerializeField] private float slideDuration = 0.4f;
    [SerializeField] private float startOffset = -1.2f;   // 画面幅に対する開始位置。負が左の画面外
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private float elapsed;

    private void Update()
    {
        var round = RoundManager.Instance;
        if (round == null) return;

        if (round.IsIntro)
        {
            if (!this.panel.gameObject.activeSelf) this.Fill(round);
            this.Slide();
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
        this.SetOffset(this.startOffset);
        this.panel.gameObject.SetActive(true);
    }

    private void Slide()
    {
        // 幕が開き切るまでは動かさない。幕の秒数をこちらに書き写さなくて済む
        if (SceneTransitionUI.Instance != null && SceneTransitionUI.Instance.IsCovering) return;

        // 読み込み直後の1フレームは deltaTime が跳ねるので上限を掛ける
        this.elapsed += Mathf.Min(Time.deltaTime, 0.05f);

        float t = this.slideDuration <= 0f ? 1f : Mathf.Clamp01(this.elapsed / this.slideDuration);
        this.SetOffset(Mathf.Lerp(this.startOffset, 0f, this.slideCurve.Evaluate(t)));
    }

    /// <summary>画面幅に対する横位置。0 が中央</summary>
    private void SetOffset(float normalized)
    {
        float width = ((RectTransform)this.panel.parent).rect.width;
        this.panel.anchoredPosition = new Vector2(normalized * width, 0f);
    }
}
