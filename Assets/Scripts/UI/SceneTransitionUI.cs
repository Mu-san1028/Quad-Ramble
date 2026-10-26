using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>暗転とタイトル表示。開幕の許可は参加者全員の準備を確認したConnectionContainerが出す。</summary>
public class SceneTransitionUI : MonoBehaviour
{
    public static SceneTransitionUI Instance { get; private set; }
    public static bool InputBlocked => Instance != null && Instance.blockInput;
    public static bool PresentationBlocked => Instance != null && Instance.IsCovering && !Instance.IsRevealing;
    public bool IsCovering => this.phase == Phase.Closing || this.phase == Phase.Covered || this.phase == Phase.Opening;
    public bool IsFullyCovered => this.phase == Phase.Covered;
    public bool IsVisuallyOpen => this.phase == Phase.Idle || this.phase == Phase.Corner;
    public bool IsRevealing => this.phase == Phase.Opening;
    public bool MinimumHoldComplete => this.IsFullyCovered && this.titleFinished
        && this.stageElapsed >= this.stageFadeDuration + this.minCoveredTime;

    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI roundText;
    [SerializeField] private TextMeshProUGUI stageText;
    [Header("演出時間（秒）")]
    [SerializeField, Min(0.01f)] private float closeDuration = 0.25f;
    [SerializeField, Min(0.01f)] private float titleFadeDuration = 0.3f;
    [SerializeField, Min(0.01f)] private float characterDuration = 0.23f;
    [SerializeField, Min(0f)] private float characterStagger = 0.06f;
    [SerializeField, Min(0.01f)] private float stageFadeDuration = 0.25f;
    [SerializeField, Min(0f)] private float minCoveredTime = 0.4f;
    [SerializeField, Min(0.01f)] private float openDuration = 0.55f;
    [SerializeField, Min(0f)] private float cornerHoldDuration = 1.5f;
    [SerializeField, Min(0.01f)] private float cornerFadeDuration = 0.3f;
    [Header("デザイン")]
    [SerializeField] private Color backgroundColor = new(0.01f, 0.012f, 0.02f, 1f);
    [SerializeField] private Color titleColor = Color.white;
    [SerializeField] private Color stageColor = new(0.8f, 0.9f, 1f, 1f);
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private TMP_FontAsset stageFont;
    [SerializeField, Min(1f)] private float fontSize = 105f;
    [SerializeField, Min(1f)] private float stageFontSize = 52f;
    [SerializeField, Min(0f)] private float characterLift = 14f;
    [SerializeField, Range(0f, 0.3f)] private float characterScale = 0.08f;
    [SerializeField] private float titleRise = 60f;
    [SerializeField] private float stageOffsetY = -55f;
    [Tooltip("有効にすると暗転解除時にROUNDを右上へ移動。無効ならその場でフェードアウト。")]
    [SerializeField] private bool moveTitleToCorner = false;
    [SerializeField, Range(0.1f, 1f)] private float cornerScale = 0.36f;
    [SerializeField] private Vector2 cornerMargin = new(48f, 36f);
    [Header("SE（任意）")]
    [SerializeField] private AudioClip coverSE;
    [SerializeField] private AudioClip openSE;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.6f;

    private enum Phase { Idle, Closing, Covered, Opening, Corner }
    private Phase phase;
    private bool blockInput, needsStage, stageReady, titleFinished;
    private float elapsed, stageElapsed, closingAlpha, bounceDuration;
    private RectTransform canvasRect;
    private Image background, inputShield;
    private CanvasGroup titleAlpha, stageAlpha;
    private AudioSource audioSource;
    private TMP_MeshInfo[] originalMesh;
    private readonly Dictionary<EventSystem, GameObject> blockedEventSystems = new();

    public static SceneTransitionUI EnsureInstance()
    {
        if (Instance != null) return Instance;
        return new GameObject("SceneTransition", typeof(RectTransform)).AddComponent<SceneTransitionUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (this.panel != null) this.panel.gameObject.SetActive(false);
        if (this.roundText != null) this.roundText.gameObject.SetActive(false);
        if (this.stageText != null) this.stageText.gameObject.SetActive(false);
        this.BuildVisuals();
        this.panel.gameObject.SetActive(false);
        this.roundText.gameObject.SetActive(false);
        this.stageText.gameObject.SetActive(false);
        this.inputShield.gameObject.SetActive(false);
    }

    private void BuildVisuals()
    {
        // UnityEngine.Objectの未取得参照にはUnityのnull判定を使用する。
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32760;
        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        this.canvasRect = (RectTransform)transform;

        this.inputShield = NewRect("InputShield", transform).gameObject.AddComponent<Image>();
        this.inputShield.gameObject.SetActive(false);
        Stretch(this.inputShield.rectTransform);
        this.inputShield.color = Color.clear;
        this.inputShield.raycastTarget = true;
        if (this.panel == null) this.panel = NewRect("Blackout", transform);
        this.panel.gameObject.SetActive(false);
        this.panel.SetAsLastSibling();
        Stretch(this.panel);
        this.panel.localScale = Vector3.one;
        this.background = this.panel.GetComponent<Image>();
        if (this.background == null) this.background = this.panel.gameObject.AddComponent<Image>();
        this.background.enabled = true;
        this.background.sprite = null;
        this.background.material = null;
        this.background.raycastTarget = false;

        if (this.font == null && this.roundText != null) this.font = this.roundText.font;
        if (this.roundText == null) this.roundText = NewRect("RoundTitle", transform).gameObject.AddComponent<TextMeshProUGUI>();
        this.roundText.gameObject.SetActive(false);
        this.ConfigureText(this.roundText, this.font, this.fontSize, this.titleColor);
        this.titleAlpha = this.roundText.GetComponent<CanvasGroup>();
        if (this.titleAlpha == null) this.titleAlpha = this.roundText.gameObject.AddComponent<CanvasGroup>();
        if (this.stageText == null) this.stageText = NewRect("StageName", transform).gameObject.AddComponent<TextMeshProUGUI>();
        this.stageText.gameObject.SetActive(false);
        this.ConfigureText(this.stageText, this.stageFont != null ? this.stageFont : this.font, this.stageFontSize, this.stageColor);
        this.stageAlpha = this.stageText.GetComponent<CanvasGroup>();
        if (this.stageAlpha == null) this.stageAlpha = this.stageText.gameObject.AddComponent<CanvasGroup>();
        this.audioSource = GetComponent<AudioSource>();
        if (this.audioSource == null) this.audioSource = gameObject.AddComponent<AudioSource>();
        this.audioSource.playOnAwake = false;
        this.audioSource.spatialBlend = 0f;
        this.audioSource.ignoreListenerPause = true;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private void ConfigureText(TextMeshProUGUI text, TMP_FontAsset asset, float size, Color color)
    {
        text.transform.SetParent(transform, false);
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1100f, 160f);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        if (asset != null) text.font = asset;
        text.fontStyle = FontStyles.Bold;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.5f;
        text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.enableVertexGradient = false;
        text.richText = false;
        text.raycastTarget = false;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void Close(int round, bool results, bool title = false)
    {
        // 二重開始は無視する。通信失敗によるタイトル復帰のみ上書きを許可。
        if (!title && (this.phase == Phase.Closing || this.IsFullyCovered)) return;
        this.closingAlpha = this.panel.gameObject.activeSelf ? this.background.color.a : 0f;
        this.blockInput = true;
        this.inputShield.gameObject.SetActive(true);
        this.panel.gameObject.SetActive(true);
        this.roundText.gameObject.SetActive(true);
        this.stageText.gameObject.SetActive(true);
        this.roundText.text = title ? "QUAD RAMBLE" : results ? "RESULTS" : $"ROUND {round}";
        this.stageText.text = "";
        this.needsStage = !title && !results;
        this.stageReady = !this.needsStage;
        this.titleFinished = false;
        this.titleAlpha.alpha = this.stageAlpha.alpha = 0f;
        this.originalMesh = null;
        this.phase = Phase.Closing;
        this.elapsed = this.stageElapsed = 0f;
        this.PlaceTitle(0f, 0f);
        this.SetBlackout(this.closingAlpha);
        this.BlockEventSystem();
    }

    public void SetStageName(string stageName)
    {
        // 新シーンのRoundManagerを確認した呼び出し元から受け取る。
        if (!this.needsStage || this.stageReady) return;
        this.stageText.text = stageName ?? "";
        this.stageReady = true;
    }

    public void Open()
    {
        if (!this.MinimumHoldComplete) return;
        this.RestoreCharacters();
        this.phase = Phase.Opening;
        this.elapsed = 0f;
        if (this.openSE != null) this.audioSource.PlayOneShot(this.openSE, this.soundVolume);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void SetBlackout(float alpha)
    {
        var color = this.backgroundColor;
        color.a = Mathf.Clamp01(alpha);
        this.background.color = color;
    }

    private void Update()
    {
        if (this.phase == Phase.Idle) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        this.elapsed += dt;

        if (this.phase == Phase.Closing)
        {
            float t = this.elapsed / Mathf.Max(0.01f, this.closeDuration);
            this.SetBlackout(Mathf.Lerp(this.closingAlpha, 1f, Ease(t)));
            if (t < 1f) return;
            this.phase = Phase.Covered;
            this.elapsed = 0f;
            this.roundText.ForceMeshUpdate();
            this.originalMesh = this.roundText.textInfo.CopyMeshInfoVertexData();
            int visible = 0;
            for (int i = 0; i < this.roundText.textInfo.characterCount; i++)
                if (this.roundText.textInfo.characterInfo[i].isVisible) visible++;
            this.bounceDuration = this.characterDuration + Mathf.Max(0, visible - 1) * this.characterStagger;
            if (this.coverSE != null) this.audioSource.PlayOneShot(this.coverSE, this.soundVolume);
        }

        if (this.phase == Phase.Covered)
        {
            this.titleAlpha.alpha = Ease(this.elapsed / Mathf.Max(0.01f, this.titleFadeDuration));
            float bounceTime = this.elapsed - this.titleFadeDuration;
            if (bounceTime >= 0f && !this.titleFinished)
            {
                this.AnimateCharacters(bounceTime);
                if (bounceTime >= this.bounceDuration)
                {
                    this.RestoreCharacters();
                    this.titleFinished = true;
                }
            }

            if (this.titleFinished && this.stageReady) this.stageElapsed += dt;
            float stageProgress = this.needsStage ? Ease(this.stageElapsed / Mathf.Max(0.01f, this.stageFadeDuration)) : 0f;
            this.stageAlpha.alpha = stageProgress;
            this.PlaceTitle(stageProgress, 0f);
        }
        else if (this.phase == Phase.Opening)
        {
            bool useCorner = this.needsStage && this.moveTitleToCorner;
            float t = this.elapsed / Mathf.Max(0.01f, this.openDuration);
            this.SetBlackout(1f - Ease(t));
            this.stageAlpha.alpha = this.needsStage ? 1f - Ease(this.elapsed / Mathf.Max(0.01f, this.stageFadeDuration)) : 0f;
            this.PlaceTitle(this.needsStage ? 1f : 0f, useCorner ? Ease(t) : 0f);
            if (!useCorner) this.titleAlpha.alpha = 1f - Ease(t);
            if (t < 1f) return;
            this.panel.gameObject.SetActive(false);
            this.stageText.gameObject.SetActive(false);
            this.phase = useCorner ? Phase.Corner : Phase.Idle;
            this.elapsed = 0f;
            if (!useCorner) this.roundText.gameObject.SetActive(false);
        }
        else if (this.phase == Phase.Corner)
        {
            this.PlaceTitle(1f, 1f);
            float t = (this.elapsed - this.cornerHoldDuration) / Mathf.Max(0.01f, this.cornerFadeDuration);
            var round = RoundManager.Instance;
            if (round == null || !round.IsIntro) t = 1f;
            this.titleAlpha.alpha = 1f - Ease(t);
            if (t >= 1f)
            {
                this.roundText.gameObject.SetActive(false);
                this.phase = Phase.Idle;
            }
        }
    }

    private void PlaceTitle(float raised, float corner)
    {
        float fit = Mathf.Min(1f, this.canvasRect.rect.width / 1200f);
        var rect = this.roundText.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.Lerp(new Vector2(0.5f, 0.5f), Vector2.one, corner);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one * fit * Mathf.Lerp(1f, this.cornerScale, corner);
        Vector2 end = -this.cornerMargin - rect.sizeDelta * (fit * this.cornerScale * 0.5f);
        rect.anchoredPosition = Vector2.Lerp(new Vector2(0f, this.titleRise * raised), end, corner);
        this.stageText.rectTransform.localScale = Vector3.one * fit;
        this.stageText.rectTransform.anchoredPosition = new Vector2(0f, this.stageOffsetY);
    }

    private void AnimateCharacters(float time)
    {
        if (this.originalMesh == null) return;
        var info = this.roundText.textInfo;
        int order = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            var character = info.characterInfo[i];
            if (!character.isVisible) continue;
            float t = (time - order++ * this.characterStagger) / Mathf.Max(0.01f, this.characterDuration);
            float pop = t > 0f && t < 1f ? Mathf.Sin(t * Mathf.PI) : 0f;
            int material = character.materialReferenceIndex, index = character.vertexIndex;
            var baseline = this.originalMesh[material].vertices;
            var vertices = info.meshInfo[material].vertices;
            Vector3 center = (baseline[index] + baseline[index + 2]) * 0.5f;
            for (int j = 0; j < 4; j++)
                vertices[index + j] = center + (baseline[index + j] - center) * (1f + pop * this.characterScale)
                    + Vector3.up * (pop * this.characterLift);
        }
        this.roundText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    private void RestoreCharacters()
    {
        if (this.originalMesh == null) return;
        this.AnimateCharacters(float.MaxValue);
        this.originalMesh = null;
    }

    private void LateUpdate()
    {
        if (this.blockInput) this.BlockEventSystem();
    }

    private void BlockEventSystem()
    {
        var system = EventSystem.current;
        if (system == null || !system.enabled) return;
        if (!this.blockedEventSystems.ContainsKey(system))
            this.blockedEventSystems.Add(system, system.currentSelectedGameObject);
        system.SetSelectedGameObject(null);
        system.enabled = false;
    }

    public void ReleaseInput()
    {
        this.blockInput = false;
        if (this.inputShield != null) this.inputShield.gameObject.SetActive(false);
        foreach (var entry in this.blockedEventSystems)
        {
            var system = entry.Key;
            if (system == null) continue;
            system.enabled = true;
            var selected = entry.Value != null && entry.Value.activeInHierarchy
                ? entry.Value : system.firstSelectedGameObject;
            if (selected != null && selected.activeInHierarchy) system.SetSelectedGameObject(selected);
        }
        this.blockedEventSystems.Clear();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        this.ReleaseInput();
        Instance = null;
    }
}
