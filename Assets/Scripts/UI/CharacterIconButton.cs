using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// キャラ選択画面のアイコン1個分。状態の出し分けだけを持つ。
/// </summary>
public class CharacterIconButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject selectedFrame;   // 自分が選んでいる印
    [SerializeField] private GameObject takenOverlay;    // 他人が確保済み
    [SerializeField] private TextMeshProUGUI ownerLabel;

    public Button Button => this.button;

    public void SetIcon(Sprite sprite)
    {
        if (this.iconImage != null) this.iconImage.sprite = sprite;
    }

    public void SetState(bool isMine, bool takenByOther, string ownerName)
    {
        if (this.selectedFrame != null) this.selectedFrame.SetActive(isMine);
        if (this.takenOverlay != null) this.takenOverlay.SetActive(takenByOther);
        if (this.button != null) this.button.interactable = !takenByOther;
        if (this.ownerLabel != null) this.ownerLabel.text = ownerName ?? string.Empty;
    }
}
