using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class FatigueUI : MonoBehaviour
{
    [System.Serializable]
    public class SlotView
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI value;   // 疲労の数値
        public Image icon;
        public Image fatigueIcon;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI points;
        public TextMeshProUGUI earnedPoints;
        public Image[] frame;
        public Image playerBadge;
        public FatigueWaveUI wave;
        public RectTransform shakeVisual;

        [System.NonSerialized] public PlayerController boundPlayer;
        [System.NonSerialized] public float previousFatigue;
        [System.NonSerialized] public float shakeRemaining;
        [System.NonSerialized] public float shakePhase;
    }

    private const int SelfIndex = 0;
    private bool presentingResults;
    public SlotView[] Slots => this.slots;

    [System.Serializable]
    private class RankPalette
    {
        public Color top;
        public Color bottom;

        public RankPalette(Color top, Color bottom)
        {
            this.top = top;
            this.bottom = bottom;
        }
    }

    [SerializeField] private SlotView[] slots = new SlotView[4];
    [SerializeField] private Color[] playerColors =
    {
        new Color(1f, 0.24f, 0.32f),
        new Color(0.18f, 0.57f, 1f),
        new Color(0.2f, 0.85f, 0.47f),
        new Color(1f, 0.83f, 0.2f)
    };

    [Header("順位の色（上端 → 下端）")]
    [SerializeField] private RankPalette gold = new RankPalette(
        new Color(1f, 0.98f, 0.68f), new Color(1f, 0.55f, 0.04f));
    [SerializeField] private RankPalette silver = new RankPalette(
        new Color(0.96f, 0.99f, 1f), new Color(0.48f, 0.58f, 0.7f));
    [SerializeField] private RankPalette bronze = new RankPalette(
        new Color(1f, 0.77f, 0.5f), new Color(0.65f, 0.3f, 0.12f));
    [SerializeField] private RankPalette verdigris = new RankPalette(
        new Color(0.59f, 0.95f, 0.91f), new Color(0.12f, 0.43f, 0.49f));

    [Header("被弾時の揺れ（Canvas上の距離）")]
    [SerializeField, Min(0f)] private float shakeDuration = 0.25f;
    [SerializeField] private Vector2 shakeStrength = new(8f, 3f);
    [SerializeField, Min(0f)] private float shakeFrequency = 24f;

    private void OnEnable()
    {
        foreach (var slot in this.slots)
        {
            if (slot == null) continue;
            slot.boundPlayer = null;
            ResetShake(slot);
        }
    }

    private void Awake()
    {
        foreach (var slot in this.slots)
        {
            if (slot == null) continue;
            StyleText(slot.label);
            StyleText(slot.value);
            StyleText(slot.rank);
            StyleText(slot.points);
            StyleText(slot.earnedPoints);
            if (slot.earnedPoints != null) slot.earnedPoints.gameObject.SetActive(false);

            if (slot.rank != null)
            {
                slot.rank.richText = true;
                slot.rank.outlineColor = new Color32(36, 22, 5, 255);
                slot.rank.outlineWidth = 0.24f;
            }
        }
    }

    private static void StyleText(TextMeshProUGUI text)
    {
        if (text == null) return;

        text.outlineColor = new Color32(12, 17, 32, 255);
        text.outlineWidth = 0.16f;
    }

    void Update()
    {
        if (this.presentingResults) return;
        var round = RoundManager.Instance;
        if (round != null && (round.IsRoundOver || round.RoundResults != null)) return;

        this.RefreshSlots();
    }

    private void RefreshSlots()
    {
        if (this.slots.Length <= SelfIndex) return;

        var players = PlayerManager.Instance != null ? PlayerManager.Instance.AllPlayers : null;

        PlayerController self = null;
        int next = SelfIndex + 1;

        if (players != null)
        {
            foreach (var player in players)
            {
                // 破棄済みの参照が残ることがあるので null チェックする
                if (player == null) continue;

                if (self == null && player.IsLocalHuman) { self = player; continue; }
                if (next < this.slots.Length) this.Render(this.slots[next++], player);
            }
        }

        this.Render(this.slots[SelfIndex], self);

        // 人数が4人未満のときに余る枠を消す
        for (int i = next; i < this.slots.Length; i++) this.Render(this.slots[i], null);
    }

    public void BeginResults()
    {
        this.RefreshSlots();
        this.presentingResults = true;
        this.ResetAllShakes();
    }

    public void EndResults()
    {
        this.presentingResults = false;
    }

    public void SetResultScore(SlotView slot, int score, int rank)
    {
        if (slot.points != null) slot.points.text = $"{score}pt";
        if (slot.rank == null) return;

        slot.rank.text = FormatRank(rank);
        this.ApplyRankColor(slot.rank, rank);
    }

    /// <summary>player が null なら枠ごと隠す</summary>
    private void Render(SlotView slot, PlayerController player)
    {
        if (slot == null || slot.root == null) return;

        bool active = player != null && player.IsSpawned;
        if (slot.root.activeSelf != active) slot.root.SetActive(active);
        if (!active)
        {
            slot.boundPlayer = null;
            ResetShake(slot);
            return;
        }

        var status = player.GetComponent<PlayerStatus>();
        float fatigue = status != null ? status.Fatigue : 0f;
        var roster = ConnectionContainer.Instance;
        var connection = roster != null ? roster.GetConnectionByPlayerId(player.ParticipantId) : null;
        bool reset = slot.boundPlayer != player;
        slot.boundPlayer = player;

        if (reset || fatigue < slot.previousFatigue)
            ResetShake(slot);
        else if (fatigue > slot.previousFatigue && !player.IsEliminated)
            slot.shakeRemaining = Mathf.Max(0f, this.shakeDuration);

        slot.previousFatigue = fatigue;

        if (slot.label != null)
            slot.label.text = connection != null ? $"{connection.Id + 1}P" : "--";

        if (slot.icon != null)
        {
            var character = connection != null && CharacterDatabase.Instance != null
                ? CharacterDatabase.Instance.Get(connection.CharacterIndex) : null;
            slot.icon.sprite = character?.icon;
            slot.icon.enabled = slot.icon.sprite != null;
        }

        if (slot.value != null) slot.value.text = Mathf.RoundToInt(fatigue).ToString();
        if (slot.points != null) slot.points.text = connection != null ? $"{connection.Score}pt" : "--pt";
        if (slot.rank != null)
        {
            int rank = connection != null ? roster.GetRank(connection.Id) : 0;
            slot.rank.text = FormatRank(rank);
            ApplyRankColor(slot.rank, rank);
        }

        this.ApplyPlayerColor(slot, connection);

        if (slot.wave != null) slot.wave.SetFatigue(fatigue, reset);
    }

    public void RestorePlayerColor(SlotView slot)
    {
        if (slot == null || slot.boundPlayer == null) return;
        var connection = ConnectionContainer.Instance?.GetConnectionByPlayerId(slot.boundPlayer.ParticipantId);
        this.ApplyPlayerColor(slot, connection);
    }

    private void ApplyPlayerColor(SlotView slot, Connection connection)
    {
        Color color = connection != null && this.playerColors != null
            && connection.Id < (ulong)this.playerColors.Length
            ? this.playerColors[(int)connection.Id] : Color.gray;

        if (slot.playerBadge != null) slot.playerBadge.color = color;
        if (slot.frame != null)
        {
            foreach (Image edge in slot.frame)
                if (edge != null) edge.color = color;
        }
    }

    private void LateUpdate()
    {
        var round = RoundManager.Instance;
        if (this.presentingResults || (round != null && (round.IsRoundOver || round.RoundResults != null)))
        {
            this.ResetAllShakes();
            return;
        }

        foreach (var slot in this.slots)
        {
            if (slot == null || slot.shakeVisual == null) continue;
            if (slot.boundPlayer != null && slot.boundPlayer.IsEliminated)
            {
                ResetShake(slot);
                continue;
            }
            if (slot.shakeRemaining <= 0f || this.shakeDuration <= 0f)
            {
                ResetShake(slot);
                continue;
            }

            float strength = Mathf.Clamp01(slot.shakeRemaining / this.shakeDuration);
            slot.shakePhase = Mathf.Repeat(slot.shakePhase
                + Time.unscaledDeltaTime * Mathf.Max(0f, this.shakeFrequency) * Mathf.PI * 2f, Mathf.PI * 20f);
            slot.shakeVisual.anchoredPosition = new Vector2(
                Mathf.Sin(slot.shakePhase) * this.shakeStrength.x,
                Mathf.Sin(slot.shakePhase * 0.7f) * this.shakeStrength.y) * strength * strength;
            slot.shakeRemaining = Mathf.Max(0f, slot.shakeRemaining - Time.unscaledDeltaTime);
        }
    }

    private static void ResetShake(SlotView slot)
    {
        slot.shakeRemaining = 0f;
        slot.shakePhase = 0f;
        if (slot.shakeVisual != null) slot.shakeVisual.anchoredPosition = Vector2.zero;
    }

    private void ResetAllShakes()
    {
        foreach (var slot in this.slots)
            if (slot != null) ResetShake(slot);
    }

    private void OnDisable()
    {
        this.ResetAllShakes();
    }

    private void ApplyRankColor(TextMeshProUGUI text, int rank)
    {
        RankPalette palette = rank switch
        {
            1 => this.gold,
            2 => this.silver,
            3 => this.bronze,
            _ => this.verdigris
        };

        Color top = rank > 0 && palette != null ? palette.top : Color.white;
        Color bottom = rank > 0 && palette != null ? palette.bottom : Color.gray;
        text.color = Color.white;
        text.enableVertexGradient = true;
        text.colorGradient = new VertexGradient(top, top, bottom, bottom);
    }

    private static string FormatRank(int rank)
    {
        if (rank <= 0) return "--";
        string suffix = rank % 100 >= 11 && rank % 100 <= 13
            ? "th"
            : (rank % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{rank}<size=45%>{suffix}</size>";
    }
}
