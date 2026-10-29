using UnityEngine;
using UnityEngine.UI;

/// <summary>疲労背景の表示だけを担当する。ゲームの疲労値は変更しない。</summary>
[RequireComponent(typeof(Image))]
public sealed class FatigueWaveUI : MonoBehaviour
{
    [SerializeField] private Material waveMaterial;
    [Header("疲労と色")]
    [SerializeField, Min(1f)] private float fillScale = 110f;
    [SerializeField, Min(1f)] private float redAtFatigue = 250f;
    [SerializeField] private Gradient fatigueColors = CreateColors();
    [SerializeField, Range(0f, 1f)] private float opacity = 0.72f;
    [SerializeField, Min(0.01f)] private float followTime = 0.35f;
    [Header("波の形（疲労度では変化しない）")]
    [Tooltip("情報欄の幅に対する振幅の割合")]
    [SerializeField, Min(0f)] private float amplitude = 0.03f;
    [Tooltip("情報欄の高さを1とした波長。0.5で2周期、1で1周期")]
    [SerializeField, Min(0.05f)] private float wavelength = 0.75f;
    [Header("波の速度（ラジアン／秒）")]
    [Tooltip("Xが低疲労時、Yが高疲労時の速度")]
    [SerializeField] private Vector2 speedRange = new(0.6f, 3f);
    [SerializeField, Min(0f)] private float maxSpeed = 4f;
    [SerializeField, Min(0.01f)] private float speedFollowTime = 0.5f;
    [Header("被弾時の揺れ")]
    [SerializeField, Min(0f)] private float hitAmplitude = 0.045f;
    [SerializeField, Min(0.01f)] private float hitDecayTime = 0.35f;
    [SerializeField, Range(0.001f, 0.2f)] private float leftFade = 0.06f;

    private Image image;
    private Material instance;
    private Material originalMaterial;
    private float targetFatigue;
    private float shownFatigue;
    private float smoothVelocity;
    private float hitPulse;
    private float phase;
    private float currentSpeed;

    private static Gradient CreateColors()
    {
        var colors = new Gradient();
        colors.SetKeys(new[]
        {
            new GradientColorKey(new Color(0.12f, 0.48f, 1f), 0f),
            new GradientColorKey(new Color(0.15f, 0.8f, 0.5f), 0.25f),
            new GradientColorKey(new Color(1f, 0.84f, 0.15f), 0.5f),
            new GradientColorKey(new Color(1f, 0.4f, 0.08f), 0.75f),
            new GradientColorKey(new Color(0.95f, 0.12f, 0.2f), 1f)
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return colors;
    }

    private void Awake()
    {
        this.image = GetComponent<Image>();
        this.originalMaterial = this.image.material;
        this.image.raycastTarget = false;

        if (this.waveMaterial == null)
        {
            Debug.LogError("FatigueWaveUI: Wave Materialを設定してください。", this);
            this.image.enabled = false;
            this.enabled = false;
            return;
        }

        this.instance = new Material(this.waveMaterial);
        this.image.material = this.instance;
        this.SetFatigue(0f, true);
    }

    public void SetFatigue(float fatigue, bool reset)
    {
        fatigue = Mathf.Max(0f, fatigue);

        if (reset || fatigue < this.targetFatigue)
        {
            this.shownFatigue = fatigue;
            this.smoothVelocity = 0f;
            this.hitPulse = 0f;
            // 疲労が下がっても位相と速度は維持し、波が飛ばないようにする。
            if (reset)
            {
                this.phase = 0f;
                this.currentSpeed = this.GetTargetSpeed(fatigue);
            }
        }
        else if (fatigue > this.targetFatigue)
        {
            this.hitPulse = 1f;
        }

        this.targetFatigue = fatigue;
        this.UpdateMaterial();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        this.shownFatigue = Mathf.SmoothDamp(this.shownFatigue, this.targetFatigue,
            ref this.smoothVelocity, Mathf.Max(0.01f, this.followTime), Mathf.Infinity, dt);
        float targetSpeed = this.GetTargetSpeed(this.shownFatigue);
        float blend = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, this.speedFollowTime));
        this.currentSpeed = Mathf.Clamp(Mathf.Lerp(this.currentSpeed, targetSpeed, blend),
            0f, Mathf.Max(0f, this.maxSpeed));
        // 単一の波を連続的に進める。2πで折り返しても同じ形になる。
        this.phase = Mathf.Repeat(this.phase + dt * this.currentSpeed, Mathf.PI * 2f);
        this.hitPulse *= Mathf.Exp(-dt / Mathf.Max(0.01f, this.hitDecayTime));
        this.UpdateMaterial();
    }

    private float GetTargetSpeed(float fatigue)
    {
        float danger = Mathf.Clamp01(fatigue / Mathf.Max(1f, this.redAtFatigue));
        float low = Mathf.Max(0f, this.speedRange.x);
        float high = Mathf.Max(low, this.speedRange.y);
        return Mathf.Clamp(Mathf.Lerp(low, high, danger), 0f, Mathf.Max(0f, this.maxSpeed));
    }

    private void UpdateMaterial()
    {
        if (this.instance == null) return;

        float fill = 1f - Mathf.Exp(-this.shownFatigue / Mathf.Max(1f, this.fillScale));
        float danger = Mathf.Clamp01(this.shownFatigue / Mathf.Max(1f, this.redAtFatigue));
        Color color = this.fatigueColors.Evaluate(danger);
        color.a *= this.opacity;
        float amplitude = Mathf.Max(0f, this.amplitude)
            + this.hitPulse * Mathf.Max(0f, this.hitAmplitude);
        this.SetProperties(this.instance, fill, color, amplitude);

        // Maskが作ったステンシル用Materialにも、その枠の値を反映する。
        Material rendered = this.image.materialForRendering;
        if (rendered != this.instance)
            this.SetProperties(rendered, fill, color, amplitude);
    }

    private void SetProperties(Material material, float fill, Color color, float amplitude)
    {
        material.SetFloat("_Fill", fill);
        material.SetColor("_FillColor", color);
        material.SetFloat("_Amplitude", amplitude);
        material.SetFloat("_Phase", this.phase);
        material.SetFloat("_Wavelength", Mathf.Max(0.05f, this.wavelength));
        material.SetFloat("_LeftFade", this.leftFade);
    }

    private void OnDestroy()
    {
        if (this.image != null) this.image.material = this.originalMaterial;
        if (this.instance != null) Destroy(this.instance);
    }
}
