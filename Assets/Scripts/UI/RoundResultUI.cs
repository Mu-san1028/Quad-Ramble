using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>確定済みの得点を表示するだけで、ゲームの得点は変更しない。</summary>
[RequireComponent(typeof(FatigueUI))]
public sealed class RoundResultUI : MonoBehaviour
{
    [Header("演出時間（ホストの設定を全端末で使用）")]
    [SerializeField, Min(0f)] private float finishDelay = 0.7f;
    [SerializeField, Min(0.01f)] private float gatherDuration = 0.55f;
    [SerializeField, Min(0.01f)] private float countDuration = 1.4f;
    [SerializeField, Min(0.01f)] private float reorderDuration = 0.7f;
    [SerializeField, Min(0f)] private float holdDuration = 1.5f;
    [Header("中央の配置（Canvas上のサイズ）")]
    [SerializeField] private Vector2 cardSize = new(500f, 140f);
    [SerializeField] private Vector2 centerOffset = Vector2.zero;
    [SerializeField, Min(0f)] private float rowGap = 16f;
    [SerializeField, Min(0f)] private float passingOffset = 100f;

    [Header("右側から加算するポイント表示")]
    [SerializeField, Min(0f)] private float gainGap = 24f;
    [SerializeField, Min(0f)] private float gainEntryDistance = 80f;

    public Vector4 Timings => new(Mathf.Max(0f, this.finishDelay),
        Mathf.Max(0.01f, this.gatherDuration), Mathf.Max(0.01f, this.countDuration),
        Mathf.Max(0.01f, this.reorderDuration));
    public float HoldDuration => Mathf.Max(0f, this.holdDuration);

    private sealed class Card
    {
        public FatigueUI.SlotView slot;
        public RoundScoreEntry score;
        public RectTransform rect;
        public Vector2 anchorMin, anchorMax, pivot, position, sizeDelta;
        public Vector2 startPosition, startSize;
        public int oldIndex, newIndex;
    }

    private FatigueUI hud;
    private RoundManager round;
    private readonly List<Card> cards = new();
    private bool prepared;
    private bool pointAddSoundHandled;
    private Vector2 resultSize;
    private float spacing;
    private float resultScale = 1f;

    private void Awake()
    {
        this.hud = GetComponent<FatigueUI>();
    }

    private void LateUpdate()
    {
        var current = RoundManager.Instance;
        if (this.cards.Count > 0 && current != this.round) this.Restore();
        if (current == null || !current.IsSpawned || current.RoundResults == null) return;

        if (this.round != current)
        {
            this.Restore();
            this.round = current;
            this.Prepare();
        }

        if (!this.prepared) return;

        float elapsed = (float)(NetworkManager.Singleton.ServerTime.Time - current.ResultStartTime);
        Vector4 timing = current.ResultTimings;
        float gather = Progress(elapsed - timing.x, timing.y);
        float gainTime = Mathf.Clamp01((elapsed - timing.x - timing.y) / Mathf.Max(0.01f, timing.z));
        float count = Progress(gainTime - 0.5f, 0.5f);
        float reorder = Progress(elapsed - timing.x - timing.y - timing.z, timing.w);
        bool showGain = elapsed >= timing.x + timing.y;

        this.PlayPointAddSound(elapsed, timing);

        foreach (var card in this.cards)
        {
            if (card.rect == null) continue;

            Vector2 oldPosition = this.RowPosition(card.oldIndex);
            Vector2 newPosition = this.RowPosition(card.newIndex);
            Vector2 position = Vector2.Lerp(card.startPosition, oldPosition, gather);
            if (reorder > 0f)
            {
                position = Vector2.Lerp(oldPosition, newPosition, reorder);
                // 上昇する枠と下降する枠を左右へ分け、交差を追いやすくする。
                int direction = System.Math.Sign(card.oldIndex - card.newIndex);
                position.x += direction * Mathf.Sin(reorder * Mathf.PI) * this.passingOffset * this.resultScale;
            }

            card.rect.anchoredPosition = position;
            card.rect.sizeDelta = Vector2.Lerp(card.startSize, this.resultSize, gather);

            int displayedScore = count >= 1f ? card.score.After
                : Mathf.RoundToInt(Mathf.Lerp(card.score.Before, card.score.After, count));
            this.hud.SetResultScore(card.slot, displayedScore,
                this.GetRank(card.score, reorder >= 0.5f));

            if (card.slot.earnedPoints != null)
            {
                this.AnimateGain(card, gainTime, showGain && gainTime < 1f);
            }
        }
    }

    private void PlayPointAddSound(float elapsed, Vector4 timing)
    {
        // 右側の獲得ポイントが吸収され、累計のカウントアップが始まる瞬間。
        float start = timing.x + timing.y + timing.z * 0.5f;
        if (this.pointAddSoundHandled || elapsed < start) return;

        this.pointAddSoundHandled = true;

        // 途中参加などで加算演出が終了している場合は、遅れて再生しない。
        if (elapsed >= timing.x + timing.y + timing.z) return;

        foreach (var card in this.cards)
        {
            if (card.rect == null || card.score.After <= card.score.Before) continue;

            SEAudio.Instance?.Play(SEType.RoundPointAdd);
            break; // 4人分を同時に重ねず、演出全体で1回だけ再生する。
        }
    }

    private void AnimateGain(Card card, float time, bool visible)
    {
        var text = card.slot.earnedPoints;
        text.gameObject.SetActive(visible);
        if (!visible) return;

        var rect = text.rectTransform;
        var parent = rect.parent as RectTransform;
        if (parent == null) return;

        // 右辺中央アンカーから、累計ポイントの中央までの座標を求める。
        Vector2 target = Vector2.zero;
        if (card.slot.points != null)
        {
            var points = card.slot.points.rectTransform;
            target = (Vector2)parent.InverseTransformPoint(points.TransformPoint(points.rect.center))
                - new Vector2(parent.rect.xMax, parent.rect.center.y);
        }

        float enter = Progress(time, 0.25f);
        float absorb = Progress(time - 0.5f, 0.5f);
        Vector2 outside = new Vector2((this.gainGap + rect.rect.width * 0.5f) * this.resultScale, target.y);
        Vector2 start = outside + Vector2.right * this.gainEntryDistance * this.resultScale;
        rect.anchoredPosition = Vector2.Lerp(Vector2.Lerp(start, outside, enter), target, absorb);
        rect.localScale = Vector3.one * this.resultScale * Mathf.Lerp(1f, 0.55f, absorb);
        text.alpha = enter * (1f - Progress(time - 0.8f, 0.2f));
    }

    private void Prepare()
    {
        this.hud.BeginResults();
        Canvas.ForceUpdateCanvases();

        foreach (var slot in this.hud.Slots)
        {
            if (slot == null || slot.root == null || slot.boundPlayer == null) continue;
            foreach (var entry in this.round.RoundResults)
            {
                if (entry.ParticipantId != slot.boundPlayer.ParticipantId) continue;
                var rect = slot.root.transform as RectTransform;
                var parent = rect != null ? rect.parent as RectTransform : null;
                if (parent == null) continue;

                var card = new Card
                {
                    slot = slot, score = entry, rect = rect,
                    anchorMin = rect.anchorMin, anchorMax = rect.anchorMax,
                    pivot = rect.pivot, position = rect.anchoredPosition, sizeDelta = rect.sizeDelta,
                    startPosition = (Vector2)parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center)) - parent.rect.center,
                    startSize = rect.rect.size
                };
                this.cards.Add(card);

                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = card.startSize;
                rect.anchoredPosition = card.startPosition;
                if (slot.earnedPoints != null)
                {
                    int delta = entry.After - entry.Before;
                    slot.earnedPoints.text = $"{(delta >= 0 ? "+" : "")}{delta}pt";
                    slot.earnedPoints.gameObject.SetActive(false);
                }
                break;
            }
        }

        this.cards.Sort((a, b) =>
        {
            int score = b.score.Before.CompareTo(a.score.Before);
            return score != 0 ? score : a.score.PlayerId.CompareTo(b.score.PlayerId);
        });
        for (int i = 0; i < this.cards.Count; i++) this.cards[i].oldIndex = i;

        var after = new List<Card>(this.cards);
        after.Sort((a, b) =>
        {
            int score = b.score.After.CompareTo(a.score.After);
            return score != 0 ? score : a.oldIndex.CompareTo(b.oldIndex);
        });
        for (int i = 0; i < after.Count; i++) after[i].newIndex = i;

        if (this.cards.Count == 0) return;
        var area = (RectTransform)this.cards[0].rect.parent;
        float gap = Mathf.Max(0f, this.rowGap);
        float height = Mathf.Max(1f, this.cardSize.y) * this.cards.Count + gap * (this.cards.Count - 1);
        float gainWidth = 0f;
        foreach (var card in this.cards)
            if (card.slot.earnedPoints != null)
                gainWidth = Mathf.Max(gainWidth, card.slot.earnedPoints.rectTransform.rect.width);
        float sideRoom = Mathf.Max(this.passingOffset, gainWidth + this.gainGap + this.gainEntryDistance);
        float scale = Mathf.Clamp01(Mathf.Min(
            area.rect.width * 0.9f / Mathf.Max(1f, this.cardSize.x + 2f * sideRoom),
            area.rect.height * 0.9f / height));
        this.resultScale = scale;
        this.resultSize = new Vector2(Mathf.Max(1f, this.cardSize.x), Mathf.Max(1f, this.cardSize.y)) * scale;
        this.spacing = this.resultSize.y + gap * scale;
        this.prepared = true;
    }

    private Vector2 RowPosition(int index)
    {
        return this.centerOffset + new Vector2(0f, ((this.cards.Count - 1) * 0.5f - index) * this.spacing);
    }

    private int GetRank(RoundScoreEntry entry, bool after)
    {
        int score = after ? entry.After : entry.Before;
        int rank = 1;
        foreach (var other in this.round.RoundResults)
            if ((after ? other.After : other.Before) > score) rank++;
        return rank;
    }

    private static float Progress(float elapsed, float duration)
    {
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        return t * t * (3f - 2f * t);
    }

    private void Restore()
    {
        foreach (var card in this.cards)
        {
            if (card.rect == null) continue;
            card.rect.anchorMin = card.anchorMin;
            card.rect.anchorMax = card.anchorMax;
            card.rect.pivot = card.pivot;
            card.rect.anchoredPosition = card.position;
            card.rect.sizeDelta = card.sizeDelta;
            if (card.slot.earnedPoints != null)
            {
                card.slot.earnedPoints.gameObject.SetActive(false);
                card.slot.earnedPoints.rectTransform.localScale = Vector3.one;
                card.slot.earnedPoints.rectTransform.anchoredPosition = Vector2.zero;
                card.slot.earnedPoints.alpha = 1f;
            }
        }

        this.cards.Clear();
        this.prepared = false;
        this.pointAddSoundHandled = false;
        this.round = null;
        if (this.hud != null) this.hud.EndResults();
    }

    private void OnDisable()
    {
        this.Restore();
    }
}
