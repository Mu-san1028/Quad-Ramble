using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// プレイヤー1人分の、名前と所持しているアイテムアイコンを描画・管理
public class PlayerInventoryUI : MonoBehaviour
{
    [Header("UI要素の参照")]
    [SerializeField] private TextMeshProUGUI playerNameText; // プレイヤー名を表示
    [SerializeField] private Transform iconContainer;        // アイコンを並べる

    [Header("生成用プレハブ")]
    [SerializeField] private GameObject iconSlotPrefab;      // アイコンを1つ表示するスロット

    // 画面に表示されるスロットのオブジェクトを管理
    private List<GameObject> _activeSlots = new List<GameObject>();

    public void UpdateInventoryUI(string playerName, List<ItemSlot> heldSlots)
    {
        if (playerNameText != null)
        {
            playerNameText.text = playerName;
        }

        // スロットをリセット
        foreach (GameObject slot in _activeSlots)
        {
            if (slot != null)
            {
                Destroy(slot);
            }
        }
        _activeSlots.Clear();

        if (heldSlots == null || iconSlotPrefab == null || iconContainer == null) return;

        // 所持アイテムデータからアイコンを並べる
        foreach (ItemSlot slot in heldSlots)
        {
            if (slot == null || slot.itemData == null) continue;

            GameObject newSlotObj = Instantiate(iconSlotPrefab, iconContainer);
            _activeSlots.Add(newSlotObj);

            // Imageコンポーネントを取得
            Image slotImage = newSlotObj.GetComponent<Image>();
            if (slotImage != null)
            {
                // ItemBaseに設定された2D画像を差し替える
                if (slot.itemData.itemIcon != null)
                {
                    slotImage.sprite = slot.itemData.itemIcon;
                    slotImage.enabled = true;
                }
                else
                {
                    slotImage.sprite = null;
                    slotImage.color = Color.white;
                }
            }
        }
    }
}