using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 自分が持っている武器スロットを画面左下に表示する（ローカル表示専用）
/// このスクリプトは Canvas を持つルートオブジェクトに付けること
/// </summary>
public class WeaponUI : MonoBehaviour
{
    [System.Serializable]
    private class SlotView
    {
        public Image background;              // 枠。装備中の強調に使う
        public Image icon;                    // 武器アイコン
        public TextMeshProUGUI fallbackText;  // アイコン未設定のときだけ武器名を出す
        public TextMeshProUGUI ammoText;      // 残弾数
    }

    [Header("スロット表示（左から順に）")]
    [SerializeField] private SlotView[] slots = new SlotView[3];

    [Header("枠の色")]
    [SerializeField] private Color equippedColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color emptyColor = new Color(0f, 0f, 0f, 0.2f);

    private Canvas canvas;

    void Awake()
    {
        this.canvas = GetComponent<Canvas>();
    }

    void Update()
    {
        PlayerItem item = PlayerItem.Local;

        bool visible = item != null;
        if (this.canvas.enabled != visible) this.canvas.enabled = visible;
        if (!visible) return;

        int left  = this.FindNeighbor(item, -1);
        int right = this.FindNeighbor(item, +1);

        if (right == left) right = -1; // 左右が同じスロットを指すときは右側を非表示にする

        // 左：Lで持ち替わる武器 / 中央：装備中 / 右：Rで持ち替わる武器
        this.RenderAt(0, item, left, false);
        this.RenderAt(1, item, item.CurrentWeaponIndex, true);
        this.RenderAt(2, item, right, false);
    }

    private void RenderAt(int position, PlayerItem item, int weaponIndex, bool isEquipped)
    {
        if (position >= this.slots.Length) return;

        ItemSlot data = weaponIndex >= 0 ? item.Weapons[weaponIndex] : null;
        this.Render(this.slots[position], data, isEquipped);
    }

    /// <summary>
    /// 装備中から direction 方向へ、中身のあるスロットを1つ辿る。
    /// PlayerItem.SwitchWeapon と同じ規則（空スロットは飛ばす）。見つからなければ -1
    /// </summary>
    private int FindNeighbor(PlayerItem item, int direction)
    {
        int count = item.Weapons.Length;
        int step = direction > 0 ? 1 : -1;

        // i < count にすることで、自分自身は候補に入らない
        for (int i = 1; i < count; i++)
        {
            int index = ((item.CurrentWeaponIndex + i * step) % count + count) % count;
            ItemSlot slot = item.Weapons[index];
            if (slot != null && slot.itemData != null) return index;
        }
        return -1;
    }


    private void Render(SlotView view, ItemSlot slot, bool isEquipped)
    {
        if (view == null) return;

        bool hasItem = slot != null && slot.itemData != null;

        if (view.background != null)
        {
            view.background.color = !hasItem   ? this.emptyColor
                                  : isEquipped ? this.equippedColor
                                               : this.idleColor;
        }

        Sprite sprite = hasItem ? slot.itemData.itemIcon : null;

        if (view.icon != null)
        {
            view.icon.sprite = sprite;
            view.icon.enabled = sprite != null;
        }

        // アイコンが未設定のときだけ武器名でしのぐ
        if (view.fallbackText != null)
        {
            bool useText = hasItem && sprite == null;
            view.fallbackText.enabled = useText;
            if (useText) view.fallbackText.text = slot.itemData.itemName;
        }

        if (view.ammoText != null)
        {
            view.ammoText.enabled = hasItem;
            if (hasItem) view.ammoText.text = slot.currentAmount.ToString();
        }
    }
}
