using System.Collections.Generic;
using UnityEngine;

public enum NoticeType
{
    Territory,
    Weapon,
    Upgrade
}

/// <summary>最大2件の通知を表示し、追加された通知を下から上へ流す。</summary>
[DisallowMultipleComponent]
public sealed class RoundEventUI : MonoBehaviour
{
    public static RoundEventUI Active { get; private set; }
    public bool IsConfigured => viewport && rowPrefab && rowPrefab.IsConfigured
        && rowPrefab.transform is RectTransform;

    [Header("通知行と表示範囲")]
    [SerializeField] private RectTransform viewport;
    [SerializeField] private RoundNoticeRow rowPrefab;
    [SerializeField, Min(1f)] private float rowHeight = 50f;
    [SerializeField, Min(0f)] private float spacing = 8f;

    [Header("表示時間（秒）")]
    [SerializeField, Min(0f)] private float displayDuration = 4f;
    [SerializeField, Min(0f)] private float scrollDuration = 0.2f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.3f;

    [Header("通知の文字色")]
    [SerializeField] private Color territoryColor = new Color(1f, 0.85f, 0.25f);
    [SerializeField] private Color weaponColor = new Color(0.25f, 0.8f, 1f);
    [SerializeField] private Color upgradeColor = new Color(0.4f, 1f, 0.4f);

    private sealed class Entry
    {
        public RoundNoticeRow row;
        public RectTransform rect;
        public float age;
        public float moveElapsed;
        public float fromY;
        public float targetY;
        public bool retiring;
        public float retireElapsed;
        public float retireAlpha;
        public float alpha = 1f;
    }

    // 古い順。表示中2件と退場中1件まで保持する。
    private readonly List<Entry> entries = new List<Entry>(3);
    private bool wasPlaying;

    private void OnEnable()
    {
        Active = this;
        PlayerItem.LocalItemNotice += OnLocalItemNotice;
        wasPlaying = false;
    }

    private void OnLocalItemNotice(string message, NoticeType type)
    {
        if (Active == this)
            AddNotice(message, type);
    }

    public void AddNotice(string message, NoticeType type)
    {
        if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(message)) return;

        if (!viewport || !rowPrefab || !rowPrefab.IsConfigured ||
            !(rowPrefab.transform is RectTransform))
        {
            Debug.LogWarning("RoundEventUI: Viewport、Row Prefabと通知行のMessage Text・Canvas Groupを設定してください。", this);
            return;
        }

        // 連続追加でも退場中の行が溜まらないようにする。
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].retiring)
                RemoveEntry(i);
        }

        float step = rowHeight + spacing;

        if (entries.Count == 2)
        {
            Entry oldest = entries[0];
            oldest.retiring = true;
            oldest.retireElapsed = 0f;
            oldest.retireAlpha = oldest.alpha;
            MoveTo(oldest, step * 2f);
        }

        foreach (Entry entry in entries)
        {
            if (!entry.retiring)
                MoveTo(entry, step);
        }

        RoundNoticeRow row = Instantiate(rowPrefab, viewport, false);
        var rect = (RectTransform)row.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(0f, rowHeight);
        rect.anchoredPosition = new Vector2(0f, -step);

        row.SetMessage(message, GetColor(type));
        row.SetAlpha(1f);
        row.gameObject.SetActive(true);

        var added = new Entry { row = row, rect = rect };
        MoveTo(added, 0f);
        entries.Add(added);
    }

    private void Update()
    {
        bool playing = GameManager.Instance && GameManager.Instance.currentState == GameState.Playing;
        if (wasPlaying && !playing)
            ClearNotices();

        wasPlaying = playing;
        float delta = Time.unscaledDeltaTime;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            if (!entry.row)
            {
                entries.RemoveAt(i);
                continue;
            }

            entry.age += delta;
            entry.moveElapsed += delta;
            float progress = scrollDuration <= 0f ? 1f : Mathf.Clamp01(entry.moveElapsed / scrollDuration);
            float y = Mathf.Lerp(entry.fromY, entry.targetY, Mathf.SmoothStep(0f, 1f, progress));
            entry.rect.anchoredPosition = new Vector2(0f, y);

            if (entry.retiring)
            {
                entry.retireElapsed += delta;
                float duration = Mathf.Max(scrollDuration, fadeDuration);
                entry.alpha = duration <= 0f ? 0f
                    : entry.retireAlpha * (1f - Mathf.Clamp01(entry.retireElapsed / duration));
            }
            else
            {
                entry.alpha = entry.age < displayDuration ? 1f
                    : fadeDuration <= 0f ? 0f
                    : 1f - Mathf.Clamp01((entry.age - displayDuration) / fadeDuration);
            }

            entry.row.SetAlpha(entry.alpha);

            if (entry.alpha <= 0f)
                RemoveEntry(i);
        }
    }

    private static void MoveTo(Entry entry, float targetY)
    {
        entry.fromY = entry.rect.anchoredPosition.y;
        entry.targetY = targetY;
        entry.moveElapsed = 0f;
    }

    private Color GetColor(NoticeType type)
    {
        switch (type)
        {
            case NoticeType.Territory: return territoryColor;
            case NoticeType.Weapon: return weaponColor;
            case NoticeType.Upgrade: return upgradeColor;
            default: return Color.white;
        }
    }

    private void RemoveEntry(int index)
    {
        if (entries[index].row)
        {
            entries[index].row.gameObject.SetActive(false);
            Destroy(entries[index].row.gameObject);
        }

        entries.RemoveAt(index);
    }

    public void ClearNotices()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            RemoveEntry(i);
    }

    private void OnDisable()
    {
        PlayerItem.LocalItemNotice -= OnLocalItemNotice;
        if (Active == this)
            Active = null;

        wasPlaying = false;
        ClearNotices();
    }

    [ContextMenu("動作確認/Territory通知を追加")]
    private void TestTerritory()
    {
        if (Application.isPlaying)
            AddNotice("エリアが切り替わります！", NoticeType.Territory);
    }

    [ContextMenu("動作確認/武器取得通知を追加")]
    private void TestWeapon()
    {
        if (Application.isPlaying)
            AddNotice("剣を拾った", NoticeType.Weapon);
    }

    [ContextMenu("動作確認/強化取得通知を追加")]
    private void TestUpgrade()
    {
        if (Application.isPlaying)
            AddNotice("移動速度がアップした", NoticeType.Upgrade);
    }

    [ContextMenu("動作確認/全通知をクリア")]
    private void TestClear()
    {
        if (Application.isPlaying)
            ClearNotices();
    }
}
