using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class FatigueUI : MonoBehaviour
{
    [System.Serializable]
    private class SlotView
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public TextMeshProUGUI value;   // 疲労の数値
        public Image icon;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI points;
        public Image[] frame;
        public Image playerBadge;
        public FatigueWaveUI wave;

        [System.NonSerialized] public PlayerController boundPlayer;
    }

    private const int SelfIndex = 0;

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

    private void OnEnable()
    {
        foreach (var slot in this.slots)
            if (slot != null) slot.boundPlayer = null;
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

                if (self == null && player.IsOwner) { self = player; continue; }
                if (next < this.slots.Length) this.Render(this.slots[next++], player);
            }
        }

        this.Render(this.slots[SelfIndex], self);

        // 人数が4人未満のときに余る枠を消す
        for (int i = next; i < this.slots.Length; i++) this.Render(this.slots[i], null);
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
            return;
        }

        var status = player.GetComponent<PlayerStatus>();
        float fatigue = status != null ? status.Fatigue : 0f;
        var roster = ConnectionContainer.Instance;
        var connection = roster != null ? roster.GetConnection(player.OwnerClientId) : null;
        bool reset = slot.boundPlayer != player;
        slot.boundPlayer = player;

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

        Color color = connection != null && this.playerColors != null
            && connection.Id < (ulong)this.playerColors.Length
            ? this.playerColors[(int)connection.Id] : Color.gray;

        if (slot.playerBadge != null) slot.playerBadge.color = color;
        if (slot.frame != null)
        {
            foreach (Image edge in slot.frame)
                if (edge != null) edge.color = color;
        }

        if (slot.wave != null) slot.wave.SetFatigue(fatigue, reset);
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
