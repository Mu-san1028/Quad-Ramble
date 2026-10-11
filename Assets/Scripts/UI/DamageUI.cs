using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class DamageFeedbackUI : MonoBehaviour
{
    [Header("被ダメージ演出")]
    [SerializeField, Range(0f, 1f)]
    private float peakAlpha = 0.25f;

    [SerializeField, Min(0.01f)]
    private float fadeInDuration = 0.05f;

    [SerializeField, Min(0.01f)]
    private float fadeOutDuration = 0.4f;

    private Image overlay;
    private PlayerStatus trackedStatus;
    private float previousFatigue;

    private bool animating;
    private float elapsed;
    private float startAlpha;


    private void Awake()
    {
        this.overlay = GetComponent<Image>();
        this.overlay.raycastTarget = false;

        this.ResetFeedback();
    }


    private void Update()
    {
        PlayerStatus status = PlayerStatus.Local;

        // 自分のプレイヤーがいない間は表示しない。
        if (status == null || !status.IsSpawned)
        {
            this.trackedStatus = null;
            this.ResetFeedback();
            return;
        }

        // 初回取得・プレイヤー交代では、現在値を記録するだけ。
        if (this.trackedStatus != status)
        {
            this.trackedStatus = status;
            this.previousFatigue = status.Fatigue;

            this.ResetFeedback();
            return;
        }

        float fatigue = status.Fatigue;

        if (fatigue > this.previousFatigue)
        {
            this.PlayFeedback();
        }
        else if (fatigue < this.previousFatigue)
        {
            // 回復やラウンドリセット時には演出を消す。
            this.ResetFeedback();
        }

        this.previousFatigue = fatigue;

        this.UpdateAnimation();
    }


    private void PlayFeedback()
    {
        // 連続被弾時は透明に戻さず、現在の濃さから再開する。
        this.startAlpha = this.overlay.color.a;
        this.elapsed = 0f;
        this.animating = true;
    }


    private void UpdateAnimation()
    {
        if (!this.animating)
            return;

        this.elapsed += Time.unscaledDeltaTime;

        float fadeIn = Mathf.Max(0.01f, this.fadeInDuration);
        float fadeOut = Mathf.Max(0.01f, this.fadeOutDuration);

        if (this.elapsed < fadeIn)
        {
            float progress = this.elapsed / fadeIn;
            float eased = Mathf.SmoothStep(0f, 1f, progress);

            this.SetAlpha(
                Mathf.Lerp(this.startAlpha, this.peakAlpha, eased)
            );

            return;
        }

        float fadeOutProgress =
            (this.elapsed - fadeIn) / fadeOut;

        if (fadeOutProgress >= 1f)
        {
            this.ResetFeedback();
            return;
        }

        this.SetAlpha(
            Mathf.Lerp(
                this.peakAlpha,
                0f,
                Mathf.SmoothStep(0f, 1f, fadeOutProgress)
            )
        );
    }


    private void SetAlpha(float alpha)
    {
        Color color = this.overlay.color;
        color.a = alpha;

        this.overlay.color = color;
    }


    private void ResetFeedback()
    {
        this.animating = false;
        this.elapsed = 0f;

        if (this.overlay != null)
            this.SetAlpha(0f);
    }


    private void OnDisable()
    {
        this.trackedStatus = null;
        this.ResetFeedback();
    }
}