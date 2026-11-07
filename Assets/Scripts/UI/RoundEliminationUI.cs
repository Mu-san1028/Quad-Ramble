using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>脱落の演出だけを担当する。得点とラウンド終了条件は変更しない。</summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(FatigueUI))]
public sealed class RoundEliminationUI : MonoBehaviour
{
    [SerializeField] private GameObject boomPrefab;
    [SerializeField] private RawImage[] boomImages;
    [SerializeField] private TMP_Text[] slotMessages;
    [SerializeField] private TMP_Text screenMessage;
    [SerializeField, Min(0.1f)] private float boomDuration = 1.1f;
    [SerializeField, Min(0.1f)] private float messageDuration = 2f;
    [SerializeField, Min(0.1f)] private float effectCameraSize = 1.6f;
    [SerializeField] private Color eliminatedColor = new(0.42f, 0.45f, 0.5f, 1f);

    private sealed class State
    {
        public PlayerController player;
        public bool eliminated;
        public float elapsed;
        public GameObject stage;
        public RenderTexture texture;
    }

    private FatigueUI hud;
    private State[] states;
    private float screenRemaining;

    private void Awake()
    {
        this.hud = GetComponent<FatigueUI>();
        this.states = new State[this.hud.Slots.Length];
        for (int i = 0; i < this.states.Length; i++) this.states[i] = new State();
        foreach (var text in this.slotMessages) StyleMessage(text);
        StyleMessage(this.screenMessage);
        this.HideMessages();
    }

    private static void StyleMessage(TMP_Text text)
    {
        if (text == null) return;
        text.text = "Eliminated!";
        text.outlineColor = new Color32(12, 17, 32, 255);
        text.outlineWidth = 0.22f;
    }

    private void LateUpdate()
    {
        bool results = RoundManager.Instance != null && RoundManager.Instance.HideFinishForResults;
        for (int i = 0; i < this.states.Length; i++)
        {
            var slot = this.hud.Slots[i];
            var state = this.states[i];
            var player = slot?.boundPlayer;
            if (player != state.player)
            {
                this.ReleaseEffect(i);
                state.player = player;
                state.eliminated = player != null && player.IsEliminated;
                state.elapsed = this.boomDuration;
            }

            bool eliminated = player != null && player.IsSpawned && player.IsEliminated;
            if (eliminated != state.eliminated)
            {
                state.eliminated = eliminated;
                state.elapsed = 0f;
                this.ReleaseEffect(i);
                if (eliminated)
                {
                    this.PlayEffect(i);
                    if (player.IsLocalHuman) this.screenRemaining = this.boomDuration + this.messageDuration;
                }
                else if (player != null && player.IsLocalHuman)
                {
                    this.screenRemaining = 0f;
                }
            }

            state.elapsed += Time.unscaledDeltaTime;
            if (state.elapsed >= this.boomDuration) this.ReleaseEffect(i);
            bool grey = eliminated && state.elapsed >= this.boomDuration;
            if (slot != null)
            {
                if (results)
                {
                    this.hud.RestorePlayerColor(slot);
                }
                else if (grey)
                {
                    if (slot.playerBadge != null) slot.playerBadge.color = this.eliminatedColor;
                    if (slot.frame != null)
                        foreach (var edge in slot.frame)
                            if (edge != null) edge.color = this.eliminatedColor;
                }
                // 波は非表示にして停止。復活時は元の疲労表示に戻す。
                if (slot.wave != null) slot.wave.gameObject.SetActive(!grey);
                bool showNumbers = !grey || results;
                if (slot.fatigueIcon != null) slot.fatigueIcon.gameObject.SetActive(showNumbers);
                if (slot.value != null) slot.value.gameObject.SetActive(showNumbers);
                if (slot.rank != null) slot.rank.gameObject.SetActive(showNumbers);
                if (slot.points != null) slot.points.gameObject.SetActive(showNumbers);
            }

            if (i < this.slotMessages.Length && this.slotMessages[i] != null)
                this.slotMessages[i].gameObject.SetActive(grey && !results);
        }

        if (this.screenMessage != null)
            this.screenMessage.gameObject.SetActive(this.screenRemaining > 0f
                && this.screenRemaining <= this.messageDuration && !results);
        this.screenRemaining = Mathf.Max(0f, this.screenRemaining - Time.unscaledDeltaTime);
    }

    private void PlayEffect(int index)
    {
        if (this.boomPrefab == null || index >= this.boomImages.Length || this.boomImages[index] == null) return;

        var state = this.states[index];
        state.stage = new GameObject($"HUD BOOM {index}");
        // ゲーム空間や他の枠の撮影領域から離し、通常カメラには映さない。
        state.stage.transform.position = new Vector3(0f, -10000f - index * 100f, 0f);
        var effect = Instantiate(this.boomPrefab, state.stage.transform);
        effect.transform.localPosition = Vector3.zero;
        effect.transform.localRotation = Quaternion.identity;
        foreach (var child in effect.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
        foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = particles.main;
            main.useUnscaledTime = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            particles.Play();
        }

        state.texture = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32);
        state.texture.name = $"HUD BOOM Texture {index}";
        state.texture.Create();
        var cameraObject = new GameObject("Effect Camera");
        cameraObject.transform.SetParent(state.stage.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = this.effectCameraSize;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 20f;
        camera.cullingMask = 1 << 31;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.targetTexture = state.texture;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = false;
        data.renderShadows = false;
        this.boomImages[index].texture = state.texture;
        this.boomImages[index].gameObject.SetActive(true);
    }

    private void ReleaseEffect(int index)
    {
        var state = this.states[index];
        if (index < this.boomImages.Length && this.boomImages[index] != null)
        {
            this.boomImages[index].gameObject.SetActive(false);
            this.boomImages[index].texture = null;
        }
        if (state.stage != null)
        {
            state.stage.SetActive(false);
            Destroy(state.stage);
            state.stage = null;
        }
        if (state.texture != null)
        {
            state.texture.Release();
            Destroy(state.texture);
            state.texture = null;
        }
    }

    private void HideMessages()
    {
        foreach (var text in this.slotMessages)
            if (text != null) text.gameObject.SetActive(false);
        foreach (var image in this.boomImages)
            if (image != null) image.gameObject.SetActive(false);
        if (this.screenMessage != null) this.screenMessage.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        this.screenRemaining = 0f;
        if (this.states != null)
            for (int i = 0; i < this.states.Length; i++)
            {
                this.ReleaseEffect(i);
                this.states[i].player = null;
            }
        this.HideMessages();
    }
}
