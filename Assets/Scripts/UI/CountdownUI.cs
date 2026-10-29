using UnityEngine;
using TMPro;

public class CountdownUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("ポップアニメーション（1表示あたりの秒数）")]
    [SerializeField] private AnimationCurve popScale = new(
        new Keyframe(0f, 0.18f, 0f, 0f),
        new Keyframe(0.16f, 1.18f, 0f, 0f),
        new Keyframe(0.29f, 0.96f, 0f, 0f),
        new Keyframe(0.4f, 1f, 0f, 0f),
        new Keyframe(1f, 1f, 0f, 0f));
    [SerializeField, Min(1f)] private float goScale = 1.3f;
    [SerializeField] private float entryTilt = -8f;
    [SerializeField, Range(0.5f, 0.95f)] private float fadeStart = 0.82f;

    [Header("文字の色")]
    [SerializeField] private Color numberTop = new(1f, 1f, 1f);
    [SerializeField] private Color numberBottom = new(0.25f, 0.8f, 1f);
    [SerializeField] private Color goTop = new(1f, 1f, 0.65f);
    [SerializeField] private Color goBottom = new(1f, 0.45f, 0.06f);

    [Header("縁取り・影")]
    [SerializeField] private Color outlineColor = new(0.04f, 0.12f, 0.26f, 1f);
    [SerializeField, Range(0f, 0.4f)] private float outlineWidth = 0.2f;
    [SerializeField] private Color shadowColor = new(0.01f, 0.03f, 0.1f, 0.8f);
    [SerializeField] private Vector2 shadowOffset = new(0.35f, -0.45f);

    private RoundManager observedRound;
    private bool countdownPlayed;
    private int displayedNumber = -1;
    private Vector3 restingScale;
    private Quaternion restingRotation;
    private Material originalMaterial;
    private Material displayMaterial;

    private void Awake()
    {
        if (this.countdownText == null) return;

        this.restingScale = this.countdownText.rectTransform.localScale;
        this.restingRotation = this.countdownText.rectTransform.localRotation;
        this.originalMaterial = this.countdownText.fontSharedMaterial;
        this.displayMaterial = new Material(this.originalMaterial);
        this.displayMaterial.name = "Countdown Pop (Instance)";
        this.displayMaterial.SetColor(ShaderUtilities.ID_OutlineColor, this.outlineColor);
        this.displayMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, this.outlineWidth);
        this.displayMaterial.EnableKeyword("UNDERLAY_ON");
        this.displayMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, this.shadowColor);
        this.displayMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, this.shadowOffset.x);
        this.displayMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, this.shadowOffset.y);
        this.displayMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.12f);
        this.displayMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.08f);
        this.countdownText.fontSharedMaterial = this.displayMaterial;
        this.countdownText.color = Color.white;
        this.countdownText.enableVertexGradient = true;
        this.countdownText.raycastTarget = false;
        this.countdownText.UpdateMeshPadding();
        this.SetVisible(false);
    }

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

        // サーバー由来の残り時間を使用し、低FPSでも数字と開始タイミングをずらさない。
        bool go = remaining <= RoundManager.StartDisplayDuration;
        int number = go ? 0 : Mathf.CeilToInt(remaining - RoundManager.StartDisplayDuration);
        if (number != this.displayedNumber)
        {
            this.displayedNumber = number;
            this.countdownText.text = go ? "GO!" : number.ToString();
            Color top = go ? this.goTop : this.numberTop;
            Color bottom = go ? this.goBottom : this.numberBottom;
            this.countdownText.colorGradient = new VertexGradient(top, top, bottom, bottom);
        }

        float phase = go ? 1f - remaining / RoundManager.StartDisplayDuration
            : number - (remaining - RoundManager.StartDisplayDuration);
        this.Animate(Mathf.Clamp01(phase), go);
    }

    private void Animate(float phase, bool go)
    {
        float scale = Mathf.Max(0.01f, this.popScale.Evaluate(phase)) * (go ? this.goScale : 1f);
        this.countdownText.rectTransform.localScale = this.restingScale * scale;
        float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / 0.4f));
        this.countdownText.rectTransform.localRotation = this.restingRotation
            * Quaternion.Euler(0f, 0f, Mathf.Lerp(this.entryTilt, 0f, settle));
        float appear = Mathf.Clamp01(phase / 0.055f);
        float disappear = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(this.fadeStart, 1f, phase));
        this.countdownText.alpha = appear * disappear;
    }

    private void SetVisible(bool visible)
    {
        if (this.countdownText.gameObject.activeSelf != visible)
        {
            this.countdownText.gameObject.SetActive(visible);
        }

        if (!visible) this.displayedNumber = -1;
    }

    private void OnDisable()
    {
        if (this.countdownText == null) return;

        this.SetVisible(false);
        this.countdownText.rectTransform.localScale = this.restingScale;
        this.countdownText.rectTransform.localRotation = this.restingRotation;
    }

    private void OnDestroy()
    {
        if (this.countdownText != null && this.displayMaterial != null)
            this.countdownText.fontSharedMaterial = this.originalMaterial;
        if (this.displayMaterial != null) Destroy(this.displayMaterial);
    }
}
