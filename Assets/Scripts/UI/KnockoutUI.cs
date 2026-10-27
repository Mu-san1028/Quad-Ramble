using UnityEngine;
using UnityEngine.UI;

/// <summary>ImpactToonを専用RenderTextureでUIの背後へ合成する。</summary>
public class KnockoutUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform title;
    [SerializeField] private RawImage impactImage;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private AudioClip knockoutSE;
    [SerializeField, Range(0,1)] private float volume = .85f;
    [SerializeField, Min(.01f)] private float popDuration = .18f;
    [SerializeField, Min(0)] private float holdDuration = .45f;
    [SerializeField, Min(.01f)] private float fadeDuration = .25f;
    [SerializeField, Min(.1f)] private float impactCameraSize = 2.5f;

    [Header("文字のポップ（PrefabのScaleを基準にした倍率）")]
    [SerializeField, Min(.01f)] private float startScale = .65f;
    [SerializeField, Min(.01f)] private float peakScale = 1.12f;
    [SerializeField, Range(.05f,.95f)] private float peakTiming = .7f;

    [Header("VFXの配置（専用カメラ内）")]
    [SerializeField] private Vector3 impactOffset = new Vector3(0,0,10);
    [SerializeField] private Vector3 impactRotation;
    [SerializeField] private Vector3 impactScale = Vector3.one;

    private RenderTexture texture;
    private GameObject rig;
    private GameObject impact;
    private float elapsed;
    private int round;
    private Vector3 titleBaseScale;

    private void Awake()
    {
        titleBaseScale = title.localScale;
        texture = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
        texture.Create();
        impactImage.texture = texture;
        // 本編カメラから離し、専用カメラだけで描画する。画面比率に依存しない。
        rig = new GameObject("KO VFX Camera");
        rig.transform.position = new Vector3(0, -10000, 0);
        var camera = rig.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.orthographicSize = impactCameraSize;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 20;
        camera.cullingMask = 1 << 31;
        camera.targetTexture = texture;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    public void Play()
    {
        elapsed = 0;
        round = ConnectionContainer.Instance != null ? ConnectionContainer.Instance.CurrentRound : 0;
        group.alpha = 1;
        title.localScale = titleBaseScale * startScale;
        if (impact != null) Destroy(impact);
        if (impactPrefab != null)
        {
            impact = Instantiate(impactPrefab, rig.transform);
            impact.transform.localPosition = impactOffset;
            impact.transform.localRotation = Quaternion.Euler(impactRotation) * impact.transform.localRotation;
            impact.transform.localScale = Vector3.Scale(impact.transform.localScale, impactScale);
            foreach (Transform t in impact.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            foreach (var ps in impact.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.useUnscaledTime = true;
                ps.Play();
            }
        }
        SEAudio.Instance?.Play(knockoutSE, volume);
    }

    private void Update()
    {
        if (ConnectionContainer.Instance != null && ConnectionContainer.Instance.CurrentRound != round)
        {
            Destroy(gameObject);
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, popDuration));
        float peak = Mathf.Clamp(peakTiming, .05f, .95f);
        float scale = t < peak ? Mathf.Lerp(startScale, peakScale, Mathf.SmoothStep(0,1,t/peak))
            : Mathf.Lerp(peakScale, 1, Mathf.SmoothStep(0,1,(t-peak)/(1-peak)));
        title.localScale = titleBaseScale * scale;
        group.alpha = 1 - Mathf.Clamp01((elapsed - popDuration - holdDuration) / fadeDuration);
        if (elapsed >= popDuration + holdDuration + fadeDuration) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (rig != null) Destroy(rig);
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
