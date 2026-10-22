using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[RequireComponent(typeof(PlayerInput))]
public class TitleInputController : MonoBehaviour
{
    [SerializeField] private NetworkConnect networkConnect;
    [SerializeField] private CharacterSelectUI characterSelect;
    [SerializeField] private InputSystemUIInputModule uiInputModule;

    [Header("キャラの回転")]
    [SerializeField] private TitlePlayerPresentation playerPresentation;
    [SerializeField, Min(0f)] private float rotationSpeed = 120f;
    [SerializeField, Range(0f, 0.95f)] private float rotationDeadzone = 0.15f;

    private PlayerInput playerInput;
    private Gamepad pairedPad;
    private Keyboard pairedKeyboard;
    private Mouse pairedMouse;
    private bool inputInitialized;
    private bool useKeyboardMouse;

    private InputAction navigationAction;
    private readonly Dictionary<int, InputBinding> navigationOverrides = new();

    private bool titleInputEnabled;
    private bool characterInputEnabled;
    private bool waitingForRelease = true;


    private void Awake()
    {
        this.playerInput = GetComponent<PlayerInput>();

        this.playerInput.neverAutoSwitchControlSchemes = true;

        // 接続前は通常のUI操作を止める。
        this.uiInputModule.enabled = false;
    }


    private void Start()
    {
        // PlayerInputの初期有効化が終わったあと、一旦全Mapを止める。
        this.StopInput();
    }


    private void Update()
    {
        Gamepad pad = LocalGamepad.Current;

        // 現在のLocalGamepadは切断済み参照を保持することがある。
        if (pad != null && !pad.added)
            pad = null;

        // Relayでは各ウィンドウで参加コードを入力できるようにする。
        bool allowKeyboard = LocalGamepad.Seat == 0 || this.networkConnect.UsesRelay;
        Keyboard keyboard = allowKeyboard ? Keyboard.current : null;
        Mouse mouse = allowKeyboard ? Mouse.current : null;
        bool keyboardAvailable = keyboard != null && keyboard.added;
        if (!keyboardAvailable)
        {
            keyboard = null;
            mouse = null;
        }

        if (!this.inputInitialized || pad != this.pairedPad
            || keyboard != this.pairedKeyboard || mouse != this.pairedMouse)
        {
            this.StopInput();
            this.pairedPad = pad;
            this.pairedKeyboard = keyboard;
            this.pairedMouse = mouse;
            this.useKeyboardMouse = keyboardAvailable;
            this.inputInitialized = true;
            this.waitingForRelease = true;

            if (this.useKeyboardMouse)
            {
                var devices = new List<InputDevice> { keyboard };
                if (mouse != null && mouse.added) devices.Add(mouse);
                if (pad != null) devices.Add(pad);
                this.playerInput.SwitchCurrentControlScheme("Keyboard&Mouse", devices.ToArray());
            }
            else if (pad != null)
            {
                this.playerInput.SwitchCurrentControlScheme("Gamepad", pad);
            }

            // ペアリング後も、入力を許可するまでMapは止める。
            this.playerInput.actions.Disable();
        }

        if (this.pairedPad == null && !this.useKeyboardMouse)
            return;

        if (this.networkConnect.State
            == NetworkConnect.ConnectionState.Idle)
        {
            if (this.networkConnect.IsEditingJoinCode)
            {
                this.waitingForRelease = true;
                return;
            }

            // 入力欄を閉じたEnterなどを、ホスト開始操作として持ち越さない。
            if (this.titleInputEnabled)
                this.CheckReleased();

            if (!this.titleInputEnabled)
                this.TryEnableTitleInput();

            return;
        }

        if (this.networkConnect.State
            != NetworkConnect.ConnectionState.Connected)
        {
            this.StopInput();
            this.waitingForRelease = true;
            return;
        }

        if (this.titleInputEnabled)
        {
            this.StopInput();
            this.waitingForRelease = true;
        }

        if (!this.characterInputEnabled)
            this.TryEnableCharacterInput();

        if (this.characterInputEnabled && this.characterSelect.IsInputReady)
        {
            this.UpdateCharacterRotation();
        }
    }


    public void OnHost(InputValue value)
    {
        if (!value.isPressed || !this.CanAcceptTitleInput())
            return;

        // 接続処理を呼ぶ前に無効化して、同時入力を防ぐ。
        this.StopInput();
        this.waitingForRelease = true;

        this.networkConnect.CreateHost(
            this.networkConnect.SelectedPlayerCount
        );
    }


    public void OnJoin(InputValue value)
    {
        if (!value.isPressed || !this.CanAcceptTitleInput())
            return;

        this.StopInput();
        this.waitingForRelease = true;

        this.networkConnect.JoinHost();
    }


    public void OnCount(InputValue value)
    {
        if (!this.CanAcceptTitleInput())
            return;

        float direction = value.Get<float>();

        if (Mathf.Approximately(direction, 0f))
            return;

        this.networkConnect.ChangePlayerCount(
            direction > 0f ? 1 : -1
        );
    }


    public void OnCancel(InputValue value)
    {
        if (!value.isPressed || !this.characterInputEnabled
            || this.networkConnect.State != NetworkConnect.ConnectionState.Connected
            || !this.characterSelect.IsInputReady)
        {
            return;
        }

        this.characterSelect.CancelSelection();
    }


    private bool CanAcceptTitleInput()
    {
        return this.titleInputEnabled
            && !this.networkConnect.IsEditingJoinCode
            && !this.waitingForRelease
            && (this.pairedPad != null || this.useKeyboardMouse)
            && this.networkConnect.State
                == NetworkConnect.ConnectionState.Idle;
    }


    private void TryEnableTitleInput()
    {
        if (!this.CheckReleased())
            return;

        this.StopInput();

        this.playerInput.SwitchCurrentActionMap("Title");
        // マウスで入力欄と既存ボタンを操作できるようにする。
        // 決定・キャンセル・方向キーはTitleのショートカットと二重に流さない。
        this.uiInputModule.enabled = true;
        this.uiInputModule.move?.action?.Disable();
        this.uiInputModule.submit?.action?.Disable();
        this.uiInputModule.cancel?.action?.Disable();
        this.titleInputEnabled = true;
    }


    private void TryEnableCharacterInput()
    {
        if (!this.characterSelect.IsInputReady)
            return;

        if (!this.CheckReleased())
            return;

        this.StopInput();

        this.RestrictNavigationToDpad();
        this.playerInput.SwitchCurrentActionMap("UI");
        this.uiInputModule.enabled = true;

        this.characterInputEnabled = true;
        this.characterSelect.SelectFirstAvailable();
    }


    private void UpdateCharacterRotation()
    {
        if (this.playerPresentation == null)
            return;

        float horizontal = (this.pairedPad != null
            ? this.pairedPad.rightStick.ReadValue().x : 0f)
            + (this.pairedKeyboard != null
                ? (this.pairedKeyboard.lKey.isPressed ? 1f : 0f)
                    - (this.pairedKeyboard.jKey.isPressed ? 1f : 0f)
                : 0f);
        float strength = Mathf.InverseLerp(this.rotationDeadzone, 1f, Mathf.Abs(horizontal));

        if (strength <= 0f)
            return;

        float degrees = Mathf.Sign(horizontal) * strength
            * this.rotationSpeed * Time.unscaledDeltaTime;

        this.playerPresentation.RotateLocalModel(degrees);
    }


    private void RestrictNavigationToDpad()
    {
        // PlayerInputが使う実行時のActionだけを変更し、共有アセットは編集しない。
        this.navigationAction = this.playerInput.actions.FindAction("UI/Navigate", true);

        for (int i = 0; i < this.navigationAction.bindings.Count; i++)
        {
            InputBinding binding = this.navigationAction.bindings[i];
            string path = binding.path;

            if (string.IsNullOrEmpty(path)
                || !path.StartsWith("<Gamepad>/", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isStick =
                path.IndexOf("/leftStick", System.StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("/rightStick", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isStick)
                continue;

            this.navigationOverrides.Add(i, binding);
            this.navigationAction.ApplyBindingOverride(i, new InputBinding { overridePath = "" });
        }
    }


    private void RestoreNavigationBindings()
    {
        if (this.navigationAction == null)
            return;

        foreach (var entry in this.navigationOverrides)
        {
            this.navigationAction.RemoveBindingOverride(entry.Key);
            this.navigationAction.ApplyBindingOverride(entry.Key, entry.Value);
        }

        this.navigationOverrides.Clear();
        this.navigationAction = null;
    }


    private bool CheckReleased()
    {
        if (!this.waitingForRelease)
            return true;

        // 接続に使ったボタン・方向入力が残ったまま切り替えない。
        bool held;
        if (this.useKeyboardMouse)
        {
            if (this.pairedKeyboard == null)
                return false;

            held = (this.pairedMouse != null && this.pairedMouse.leftButton.isPressed)
                || this.pairedKeyboard.wKey.isPressed
                || this.pairedKeyboard.aKey.isPressed
                || this.pairedKeyboard.sKey.isPressed
                || this.pairedKeyboard.dKey.isPressed
                || this.pairedKeyboard.upArrowKey.isPressed
                || this.pairedKeyboard.downArrowKey.isPressed
                || this.pairedKeyboard.bKey.isPressed
                || this.pairedKeyboard.leftArrowKey.isPressed
                || this.pairedKeyboard.rightArrowKey.isPressed
                || this.pairedKeyboard.enterKey.isPressed
                || this.pairedKeyboard.numpadEnterKey.isPressed
                || this.pairedKeyboard.spaceKey.isPressed;
        }
        else
        {
            if (this.pairedPad == null)
                return false;

            held = this.pairedPad.buttonSouth.isPressed
                || this.pairedPad.buttonEast.isPressed
                || this.pairedPad.dpad.ReadValue() != Vector2.zero
                || this.pairedPad.leftStick.ReadValue().sqrMagnitude > 0.04f
                || this.pairedPad.rightStick.ReadValue().sqrMagnitude > 0.04f;
        }

        if (this.pairedPad != null)
        {
            held |= this.pairedPad.buttonSouth.isPressed
                || this.pairedPad.buttonEast.isPressed
                || this.pairedPad.dpad.ReadValue() != Vector2.zero;
        }

        if (held)
            return false;

        this.waitingForRelease = false;
        return true;
    }


    private void StopInput()
    {
        this.titleInputEnabled = false;
        this.characterInputEnabled = false;

        this.uiInputModule.enabled = false;
        this.playerInput.actions.Disable();
        this.RestoreNavigationBindings();
    }


    private void OnDisable()
    {
        if (this.playerInput == null || this.uiInputModule == null)
            return;

        this.StopInput();
        this.waitingForRelease = true;
        this.inputInitialized = false;
    }
}
