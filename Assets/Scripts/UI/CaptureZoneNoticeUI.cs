using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>予告を共通通知UIへ送り、エリアの矢印・倍率を表示する。</summary>
public sealed class CaptureZoneNoticeUI : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [Tooltip("未設定なら同じシーンのRoundEventUIを自動取得")]
    [SerializeField] private RoundEventUI roundEventUI;
    [Tooltip("日本語を含むFont Asset。未設定なら既存タイマーのフォントを使用")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Min(0f)] private float markerHeight = 2f;

    private sealed class Marker
    {
        public CaptureZone zone;
        public RectTransform root;
        public GameObject arrow;
        public TextMeshProUGUI bonus;
    }

    private CaptureZoneDirector director;
    private Canvas canvas;
    private RectTransform canvasRect;
    private string lastAnnouncement;
    private double lastAnnouncementPhase = double.NaN;
    private bool wasRunning;
    private readonly List<Marker> markers = new();

    private void Start()
    {
        director = GetComponent<CaptureZoneDirector>();
        if (!director || (Application.isBatchMode && director.NetworkManager && !director.NetworkManager.IsClient))
            return;

        if (!roundEventUI)
        {
            foreach (RoundEventUI candidate in FindObjectsByType<RoundEventUI>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == gameObject.scene)
                {
                    roundEventUI = candidate;
                    break;
                }
            }
        }

        if (!roundEventUI)
            Debug.LogWarning("CaptureZoneNoticeUI: 同じシーンにRoundEventUIを配置してください。エリア変更の予告を表示できません。", this);

        if (!font)
        {
            TimerUI timer = FindAnyObjectByType<TimerUI>();
            font = timer && timer.timerText ? timer.timerText.font : TMP_Settings.defaultFontAsset;
        }

        var root = new GameObject("Capture Zone Notices", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        canvasRect = root.GetComponent<RectTransform>();

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;

        foreach (CaptureZone zone in director.Zones)
        {
            var markerRoot = new GameObject(zone.name + " Marker", typeof(RectTransform)).GetComponent<RectTransform>();
            markerRoot.SetParent(canvasRect, false);
            markerRoot.sizeDelta = new Vector2(90, 100);

            var arrow = new GameObject("Down Arrow", typeof(RectTransform));
            arrow.transform.SetParent(markerRoot, false);
            // フォントに矢印文字がなくても表示できるように、3本のUI画像で描く。
            MakeStroke(arrow.transform, new Vector2(0, 15), new Vector2(7, 34), 0);
            MakeStroke(arrow.transform, new Vector2(-8, 0), new Vector2(7, 24), 45);
            MakeStroke(arrow.transform, new Vector2(8, 0), new Vector2(7, 24), -45);

            TextMeshProUGUI bonus = MakeText("Score Multiplier", markerRoot, 24);
            bonus.rectTransform.sizeDelta = new Vector2(100, 36);
            bonus.rectTransform.anchoredPosition = new Vector2(0, -33);
            bonus.color = new Color(1f, .85f, .4f);

            markers.Add(new Marker { zone = zone, root = markerRoot, arrow = arrow, bonus = bonus });
            markerRoot.gameObject.SetActive(false);
        }

    }

    private void LateUpdate()
    {
        if (!canvas)
            return;

        bool running = director && director.IsRunning;
        canvas.enabled = running;
        if (!running)
        {
            ResetAnnouncements();
            return;
        }

        wasRunning = true;

        string announcement = director.PendingAnnouncement();
        if (string.IsNullOrWhiteSpace(announcement))
        {
            lastAnnouncement = null;
        }
        else if (roundEventUI && roundEventUI.isActiveAndEnabled &&
            (announcement != lastAnnouncement || director.ChangedAt != lastAnnouncementPhase))
        {
            roundEventUI.AddNotice(announcement, NoticeType.Territory);
            lastAnnouncement = announcement;
            lastAnnouncementPhase = director.ChangedAt;
        }

        if (!targetCamera || !targetCamera.isActiveAndEnabled)
            targetCamera = Camera.main;

        foreach (Marker marker in markers)
        {
            bool active = marker.zone && marker.zone.IsSpawned && marker.zone.IsAreaActive;
            bool arrow = active && director.ShowArrowFor(marker.zone);
            float multiplier = active ? marker.zone.ScoreMultiplier : 0;
            bool showBonus = multiplier > 1f;

            Vector3 screen = targetCamera && active
                ? targetCamera.WorldToScreenPoint(marker.zone.transform.position + Vector3.up * markerHeight)
                : new Vector3(0, 0, -1);

            bool inView = targetCamera && screen.z > 0 && targetCamera.pixelRect.Contains(new Vector2(screen.x, screen.y));
            marker.root.gameObject.SetActive(inView && (arrow || showBonus));
            if (!inView)
                continue;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            marker.root.anchoredPosition = local;
            marker.arrow.SetActive(arrow);
            marker.bonus.gameObject.SetActive(showBonus);
            marker.bonus.text = showBonus ? "×" + multiplier.ToString("0.#") : string.Empty;
        }
    }

    private TextMeshProUGUI MakeText(string name, Transform parent, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        go.AddComponent<Shadow>().effectDistance = new Vector2(1, -2);
        return text;
    }

    private static void MakeStroke(Transform parent, Vector2 position, Vector2 size, float angle)
    {
        var go = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0, 0, angle);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        go.AddComponent<Shadow>().effectDistance = new Vector2(1, -2);
    }

    private void OnDisable()
    {
        ResetAnnouncements();

        if (canvas)
            canvas.enabled = false;
    }

    private void ResetAnnouncements()
    {
        // 終了時だけクリアし、開始前のUI動作確認は妨げない。
        if (wasRunning && roundEventUI)
            roundEventUI.ClearNotices();

        wasRunning = false;
        lastAnnouncement = null;
        lastAnnouncementPhase = double.NaN;
    }

    private void OnDestroy()
    {
        if (canvas)
            Destroy(canvas.gameObject);
    }
}
