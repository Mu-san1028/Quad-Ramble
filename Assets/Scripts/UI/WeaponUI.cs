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
        public Image cooldown;                // クールダウンの円形フィル
        // public GameObject selection;       // 選択中を示す四隅／外枠。今は不透明度で代用している
    }

    [Header("スロット表示（左から順に）")]
    [SerializeField] private SlotView[] slots = new SlotView[3];

    [Header("枠の色")]
    [SerializeField] private Color equippedColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color emptyColor = new Color(0f, 0f, 0f, 0.2f);

    [Header("選択の表示")]
    [Range(0f, 1f)]
    [SerializeField] private float dimAlpha = 0.35f;   // 選択中でないスロットの中身の不透明度

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

        for (int i = 0; i < this.slots.Length; i++)
        {
            ItemSlot data = i < item.Weapons.Length ? item.Weapons[i] : null;
            this.Render(this.slots[i], data, i == item.CurrentWeaponIndex);
        }
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

        // 選択中でないスロットは中身を薄くする。
        // 背景色は将来レアリティの表示に使う予定なので、区別はそちらに頼らない
        float alpha = isEquipped ? 1f : this.dimAlpha;
        SetAlpha(view.icon, alpha);
        SetAlpha(view.fallbackText, alpha);
        SetAlpha(view.ammoText, alpha);

        // クールダウンはスロットごと。fillAmount 0 は何も描かれないので SetActive は不要
        if (view.cooldown != null)
        {
            view.cooldown.fillAmount = hasItem ? slot.CooldownRemaining : 0f;
        }

        // 四隅の枠で選択を示す案。暫定対応が済んだら戻す
        // bool showSelection = isEquipped && hasItem;
        // if (view.selection != null && view.selection.activeSelf != showSelection)
        // {
        //     view.selection.SetActive(showSelection);
        // }
    }

    /// <summary>rgb は保ったまま、不透明度だけ差し替える</summary>
    private static void SetAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null) return;

        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }
}
