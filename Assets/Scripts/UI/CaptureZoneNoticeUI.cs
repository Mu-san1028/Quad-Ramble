using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>予告文と5秒間の矢印。画面外の方向誘導や距離表示は行わない。</summary>
public sealed class CaptureZoneNoticeUI : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
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
    private TextMeshProUGUI notice;
    private readonly List<Marker> markers = new();

    private void Start()
    {
        director = GetComponent<CaptureZoneDirector>();
        if (!director || (Application.isBatchMode && director.NetworkManager && !director.NetworkManager.IsClient))
            return;

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

        notice = MakeText("Announcement", canvasRect, 30);
        notice.rectTransform.anchorMin = notice.rectTransform.anchorMax = new Vector2(.5f, .82f);
        notice.rectTransform.sizeDelta = new Vector2(1000, 70);

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

        notice.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!canvas)
            return;

        bool running = director && director.IsRunning;
        canvas.enabled = running;
        if (!running)
            return;

        string announcement = director.PendingAnnouncement();
        notice.gameObject.SetActive(!string.IsNullOrEmpty(announcement));
        notice.text = announcement ?? string.Empty;

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
        if (canvas)
            canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (canvas)
            Destroy(canvas.gameObject);
    }
}
