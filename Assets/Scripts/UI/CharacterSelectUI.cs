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
    [SerializeField] private Button leaveRoomButton;

    private readonly List<CharacterIconButton> icons = new();
    private int pendingCharacterIndex = -1;
    private bool hasPreviousSelection;
    private int previousCharacterIndex;
    private bool previousReady;

    private void Start()
    {
        if (this.panel != null) this.panel.SetActive(false);
        if (this.readyButton != null) this.readyButton.onClick.AddListener(this.ToggleReady);

        ConnectionContainer.OnInstanceReady += this.HandleContainerReady;
        if (ConnectionContainer.Instance != null) this.HandleContainerReady(ConnectionContainer.Instance);
    }

    public bool IsInputReady
    {
        get
        {
            return this.panel != null
                && this.panel.activeInHierarchy
                && this.icons.Count > 0;
        }
    }
    public void HidePanel()
    {
        if (this.panel != null)
            this.panel.SetActive(false);
    }


    public void SelectFirstAvailable()
    {
        if (EventSystem.current == null)
            return;

        foreach (CharacterIconButton icon in this.icons)
        {
            if (icon == null || icon.Button == null)
                continue;

            if (!icon.Button.gameObject.activeInHierarchy
                || !icon.Button.IsInteractable())
            {
                continue;
            }

            EventSystem.current.SetSelectedGameObject(
                icon.Button.gameObject
            );

            return;
        }
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
        this.hasPreviousSelection = false;
        this.pendingCharacterIndex = -1;

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
            icon.Button.onClick.AddListener(() => this.SelectCharacter(index));
            this.icons.Add(icon);
        }

        // パネルの表示とレイアウト確定後に選択する。
    }

    private void SelectCharacter(int index)
    {
        ConnectionContainer container = ConnectionContainer.Instance;
        if (container == null) return;

        // RPCの応答で選択が反映されてから、決定ボタンへ移動する。
        this.pendingCharacterIndex = index;
        container.SelectCharacterRpc(index);
    }

    private void Refresh(int _)
    {
        ConnectionContainer container = ConnectionContainer.Instance;
        if (container == null || NetworkManager.Singleton == null) return;

        ulong myId = NetworkManager.Singleton.LocalClientId;
        Connection me = container.GetConnection(myId);

        if (this.panel != null) this.panel.SetActive(me != null);
        if (me == null)
        {
            this.hasPreviousSelection = false;
            return;
        }

        // 自分の選択がサーバーで受理された後だけ鳴らす。初回同期は鳴らさない。
        if (this.hasPreviousSelection)
        {
            if (me.CharacterIndex != this.previousCharacterIndex && me.CharacterIndex >= 0)
                SEAudio.Instance?.Play(SEType.CharacterSelect);
            else if (me.IsReady != this.previousReady)
                SEAudio.Instance?.Play(me.IsReady ? SEType.Confirm : SEType.Cancel);
        }

        this.previousCharacterIndex = me.CharacterIndex;
        this.previousReady = me.IsReady;
        this.hasPreviousSelection = true;

        for (int i = 0; i < this.icons.Count; i++)
        {
            Connection owner = container.GetOwnerOf(i);
            bool isMine = owner != null && !owner.IsBot && owner.ClientId == myId;
            this.icons[i].SetState(isMine, owner != null && !isMine, owner?.Name);
        }

        if (this.readyButton != null) this.readyButton.interactable = me.CharacterIndex >= 0;
        if (this.readyLabel != null) this.readyLabel.text = me.IsReady ? "選択済み" : "決める！";

        this.UpdateNavigation();
        if (this.pendingCharacterIndex >= 0)
        {
            if (me.CharacterIndex == this.pendingCharacterIndex
                && IsAvailable(this.readyButton) && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(this.readyButton.gameObject);
            }

            this.pendingCharacterIndex = -1;
        }
        this.EnsureValidSelection();
    }

    private void LateUpdate()
    {
        if (this.IsInputReady)
            this.EnsureValidSelection();
    }

    private void EnsureValidSelection()
    {
        if (EventSystem.current == null)
            return;

        GameObject selected = EventSystem.current.currentSelectedGameObject;
        foreach (CharacterIconButton icon in this.icons)
        {
            if (IsAvailable(icon.Button) && selected == icon.Button.gameObject)
                return;
        }

        if (IsAvailable(this.readyButton) && selected == this.readyButton.gameObject)
            return;

        if (IsAvailable(this.leaveRoomButton) && selected == this.leaveRoomButton.gameObject)
            return;

        this.SelectFirstAvailable();
    }

    private static bool IsAvailable(Button button)
    {
        return button != null && button.IsActive() && button.IsInteractable();
    }

    private void UpdateNavigation()
    {
        // 非表示中に生成したアイコンも、実際の配置で移動先を決める。
        Canvas.ForceUpdateCanvases();
        foreach (CharacterIconButton icon in this.icons)
        {
            Button button = icon.Button;
            if (button == null) continue;

            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = this.FindIcon(button, Vector2.left),
                selectOnRight = this.FindIcon(button, Vector2.right),
                selectOnUp = this.FindIcon(button, Vector2.up)
                    ?? (IsAvailable(this.leaveRoomButton) ? this.leaveRoomButton : null),
                selectOnDown = this.FindIcon(button, Vector2.down)
                    ?? (IsAvailable(this.readyButton) ? this.readyButton : null)
            };
        }

        if (this.readyButton != null)
        {
            this.readyButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = this.FindIcon(this.readyButton, Vector2.up),
                selectOnRight = IsAvailable(this.leaveRoomButton) ? this.leaveRoomButton : null
            };
        }

        if (this.leaveRoomButton != null)
        {
            this.leaveRoomButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = IsAvailable(this.readyButton) ? this.readyButton
                    : this.FindIcon(this.leaveRoomButton, Vector2.left),
                selectOnUp = this.FindIcon(this.leaveRoomButton, Vector2.up),
                selectOnDown = this.FindIcon(this.leaveRoomButton, Vector2.left)
            };
        }
    }

    private Button FindIcon(Button source, Vector2 direction)
    {
        Button nearest = null;
        float bestScore = 0f;
        RectTransform sourceRect = (RectTransform)source.transform;
        Vector3 origin = sourceRect.TransformPoint(sourceRect.rect.center);
        Vector3 worldDirection = source.transform.TransformDirection(direction);

        foreach (CharacterIconButton icon in this.icons)
        {
            Button candidate = icon.Button;
            if (candidate == source || !IsAvailable(candidate)) continue;

            RectTransform rect = (RectTransform)candidate.transform;
            Vector3 offset = rect.TransformPoint(rect.rect.center) - origin;
            float forward = Vector3.Dot(worldDirection, offset);
            if (forward <= 0f) continue;
            if (source != this.readyButton && source != this.leaveRoomButton
                && (offset - worldDirection * forward).sqrMagnitude > 1f)
                continue;

            // 同じ行・列の近いアイコンを優先する。
            float score = forward / offset.sqrMagnitude;
            if (score <= bestScore) continue;
            bestScore = score;
            nearest = candidate;
        }

        return nearest;
    }

    public void CancelSelection()
    {
        ConnectionContainer container = ConnectionContainer.Instance;
        if (container == null || NetworkManager.Singleton == null) return;

        Connection me = container.GetConnection(NetworkManager.Singleton.LocalClientId);
        if (me == null) return;

        this.pendingCharacterIndex = -1;
        if (me.IsReady)
            container.SetReadyRpc(false);

        this.SelectFirstAvailable();
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
