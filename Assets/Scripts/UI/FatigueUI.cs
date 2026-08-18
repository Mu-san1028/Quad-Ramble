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

    [SerializeField] private SlotView[] slots = new SlotView[4];
    [SerializeField] private float fullFatigue = 200f;   // ゲージが満タンになる疲労値
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color selfColor = Color.yellow;

    void Update()
    {
        var players = PlayerManager.Instance != null ? PlayerManager.Instance.AllPlayers : null;

        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.root == null) continue;

            // 破棄済みの参照が残ることがあるので null チェックする
            var player = (players != null && i < players.Count) ? players[i] : null;
            bool active = player != null;

            if (slot.root.activeSelf != active) slot.root.SetActive(active);
            if (!active) continue;

            var status = player.GetComponent<PlayerStatus>();
            float fatigue = status != null ? status.Fatigue : 0f;
            string name = ConnectionContainer.Instance != null
                ? ConnectionContainer.Instance.GetName(player.OwnerClientId)
                : null;

            if (slot.label != null)
                slot.label.text = string.IsNullOrEmpty(name) ? $"P{i + 1}" : name;
            if (slot.value != null) slot.value.text = Mathf.RoundToInt(fatigue).ToString();
            if (slot.fill != null)
            {
                slot.fill.fillAmount = Mathf.Clamp01(fatigue / fullFatigue);
                slot.fill.color = player.IsOwner ? selfColor : normalColor;
            }
        }

    }
}
