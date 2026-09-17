using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Title のキャラ選択。
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;           // 接続前は隠す
    [SerializeField] private Transform iconParent;       // Grid Layout Group
    [SerializeField] private CharacterIconButton iconPrefab;
    [SerializeField] private Button readyButton;
    [SerializeField] private TextMeshProUGUI readyLabel;

    private readonly List<CharacterIconButton> icons = new();

    private void Start()
    {
        if (this.panel != null) this.panel.SetActive(false);
        if (this.readyButton != null) this.readyButton.onClick.AddListener(this.ToggleReady);

        ConnectionContainer.OnInstanceReady += this.HandleContainerReady;
        if (ConnectionContainer.Instance != null) this.HandleContainerReady(ConnectionContainer.Instance);
    }

    private void OnDestroy()
    {
        ConnectionContainer.OnInstanceReady -= this.HandleContainerReady;
        if (ConnectionContainer.Instance != null)
        {
            ConnectionContainer.Instance.OnConnectionsChanged -= this.Refresh;
        }
    }

    private void HandleContainerReady(ConnectionContainer container)
    {
        container.OnConnectionsChanged -= this.Refresh;
        container.OnConnectionsChanged += this.Refresh;

        this.Build();
        this.Refresh(0);
    }

    private void Build()
    {
        if (this.icons.Count > 0 || CharacterDatabase.Instance == null) return;

        for (int i = 0; i < CharacterDatabase.Instance.Count; i++)
        {
            int index = i;   // クロージャがループ変数を掴まないように
            CharacterIconButton icon = Instantiate(this.iconPrefab, this.iconParent);
            icon.SetIcon(CharacterDatabase.Instance.Get(index).icon);
            icon.Button.onClick.AddListener(() => ConnectionContainer.Instance?.SelectCharacterRpc(index));
            this.icons.Add(icon);
        }

        // パッド操作用。最初の選択対象を渡さないとスティックでカーソルが動かない
        if (this.icons.Count > 0 && EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(this.icons[0].gameObject);
        }
    }

    private void Refresh(int _)
    {
        ConnectionContainer container = ConnectionContainer.Instance;
        if (container == null || NetworkManager.Singleton == null) return;

        ulong myId = NetworkManager.Singleton.LocalClientId;
        Connection me = container.GetConnection(myId);

        if (this.panel != null) this.panel.SetActive(me != null);
        if (me == null) return;

        for (int i = 0; i < this.icons.Count; i++)
        {
            Connection owner = container.GetOwnerOf(i);
            bool isMine = owner != null && owner.ClientId == myId;
            this.icons[i].SetState(isMine, owner != null && !isMine, owner?.Name);
        }

        if (this.readyButton != null) this.readyButton.interactable = me.CharacterIndex >= 0;
        if (this.readyLabel != null) this.readyLabel.text = me.IsReady ? "決定済み" : "決定";
    }

    private void ToggleReady()
    {
        ConnectionContainer container = ConnectionContainer.Instance;
        if (container == null || NetworkManager.Singleton == null) return;

        Connection me = container.GetConnection(NetworkManager.Singleton.LocalClientId);
        if (me == null) return;

        container.SetReadyRpc(!me.IsReady);
    }
}
