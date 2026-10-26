using TMPro;
using UnityEngine;

/// <summary>観戦者本人だけに生成する、観戦相手の名前と操作案内。</summary>
public class SpectatorUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private string namePrefix = "観戦中: ";
    private string displayedName;

    public void Show(string playerName)
    {
        if (this.nameLabel != null && this.displayedName != playerName)
        {
            this.nameLabel.text = this.namePrefix + playerName;
            this.displayedName = playerName;
        }

        this.gameObject.SetActive(true);
    }

    public void Hide()
    {
        this.gameObject.SetActive(false);
    }
}
