using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerUIController : MonoBehaviour
{
    [Header("UI設定")]
    public TextMeshProUGUI nameDisplay; // プレイヤー名
    public TextMeshProUGUI rankDisplay; // 順位
    public Image itemIcon;            // アイテム

    [Header("プレイヤーデータ")]
    public string playerName = "Player";
    public int currentRank = 1;
    public Sprite currentItemSprite;   // アイテムの画像

    // 名前を設定
    void Start()
    {
        if (nameDisplay != null)
            nameDisplay.text = playerName;
    }

    void Update()
    {
        // 順位の表示
        if (rankDisplay != null)
            rankDisplay.text = currentRank.ToString();

        // アイテム画像の表示
        if (itemIcon != null)
        {
            if (currentItemSprite != null)
            {
                itemIcon.sprite = currentItemSprite;
                itemIcon.enabled = true;
            }
            else
            {
                itemIcon.enabled = false;
            }
        }
    }
}