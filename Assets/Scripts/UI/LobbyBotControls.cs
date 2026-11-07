using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>ホスト専用のBot追加・削除UI。見た目はPrefabで編集する。</summary>
public class LobbyBotControls : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button addButton;
    [SerializeField] private Button removeButton;
    [SerializeField] private TMP_Text statusLabel;
    private BotParticipantManager manager;

    public void Bind(BotParticipantManager target)
    {
        manager = target;
        addButton.onClick.AddListener(manager.AddBot);
        removeButton.onClick.AddListener(manager.RemoveBot);
    }

    private void Update()
    {
        bool visible = manager != null && manager.CanEdit;
        panel.SetActive(visible);
        if (!visible) return;
        addButton.interactable = manager.CanAdd;
        removeButton.interactable = manager.CanRemove;
        statusLabel.text = manager.Status;

        // 既存の決定操作を使わず、Viewを押しながらLB/RBでも編集できる。
        var pad = LocalGamepad.Current;
        bool add = pad != null && pad.selectButton.isPressed && pad.rightShoulder.wasPressedThisFrame;
        bool remove = pad != null && pad.selectButton.isPressed && pad.leftShoulder.wasPressedThisFrame;
        var keyboard = LocalGamepad.Seat == 0 ? Keyboard.current : null;
        if (add || (keyboard?.f6Key.wasPressedThisFrame ?? false)) manager.AddBot();
        else if (remove || (keyboard?.f7Key.wasPressedThisFrame ?? false)) manager.RemoveBot();
    }
}
