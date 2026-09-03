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
        public Image fill;  // ゲージ
    }

    private const int SelfIndex = 0;

    [SerializeField] private SlotView[] slots = new SlotView[4];
    [SerializeField] private float fullFatigue = 200f;   // ゲージが満タンになる疲労値
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selfColor = Color.yellow;

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

        bool active = player != null;
        if (slot.root.activeSelf != active) slot.root.SetActive(active);
        if (!active) return;

        var status = player.GetComponent<PlayerStatus>();
        float fatigue = status != null ? status.Fatigue : 0f;
        string name = ConnectionContainer.Instance != null
            ? ConnectionContainer.Instance.GetName(player.OwnerClientId)
            : null;

        if (slot.label != null) slot.label.text = string.IsNullOrEmpty(name) ? "Player" : name;
        if (slot.value != null) slot.value.text = Mathf.RoundToInt(fatigue).ToString();
        if (slot.fill != null)
        {
            slot.fill.fillAmount = Mathf.Clamp01(fatigue / fullFatigue);
            slot.fill.color = player.IsOwner ? this.selfColor : this.normalColor;
        }
    }
}
