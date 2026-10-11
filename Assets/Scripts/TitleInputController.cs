using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

[RequireComponent(typeof(PlayerInput))]
public class TitleInputController : MonoBehaviour
{
    [SerializeField] private NetworkConnect networkConnect;
    [SerializeField] private CharacterSelectUI characterSelect;
    [SerializeField] private InputSystemUIInputModule uiInputModule;

    private PlayerInput playerInput;
    private Gamepad pairedPad;

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

        if (pad != this.pairedPad)
        {
            this.StopInput();
            this.pairedPad = pad;
            this.waitingForRelease = true;

            if (pad != null)
            {
                this.playerInput.SwitchCurrentControlScheme(
                    "Gamepad",
                    pad
                );

                // ペアリング後も、入力を許可するまでMapは止める。
                this.playerInput.actions.Disable();
            }
        }

        if (this.pairedPad == null)
            return;

        if (this.networkConnect.State
            == NetworkConnect.ConnectionState.Idle)
        {
            if (!this.titleInputEnabled)
                this.TryEnableTitleInput();

            return;
        }

        if (this.networkConnect.State
            != NetworkConnect.ConnectionState.Connected)
        {
            this.StopInput();
            return;
        }

        if (this.titleInputEnabled)
        {
            this.StopInput();
            this.waitingForRelease = true;
        }

        if (!this.characterInputEnabled)
            this.TryEnableCharacterInput();
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


    private bool CanAcceptTitleInput()
    {
        return this.titleInputEnabled
            && this.pairedPad != null
            && this.networkConnect.State
                == NetworkConnect.ConnectionState.Idle;
    }


    private void TryEnableTitleInput()
    {
        if (!this.CheckReleased())
            return;

        this.StopInput();

        this.playerInput.SwitchCurrentActionMap("Title");
        this.titleInputEnabled = true;
    }


    private void TryEnableCharacterInput()
    {
        if (!this.characterSelect.IsInputReady)
            return;

        if (!this.CheckReleased())
            return;

        this.StopInput();

        this.playerInput.SwitchCurrentActionMap("UI");
        this.uiInputModule.enabled = true;

        this.characterInputEnabled = true;
        this.characterSelect.SelectFirstAvailable();
    }


    private bool CheckReleased()
    {
        if (!this.waitingForRelease)
            return true;

        // 接続に使ったボタン・方向入力が残ったまま切り替えない。
        bool held =
            this.pairedPad.buttonSouth.isPressed
            || this.pairedPad.buttonEast.isPressed
            || this.pairedPad.dpad.ReadValue() != Vector2.zero
            || this.pairedPad.leftStick.ReadValue().sqrMagnitude > 0.04f;

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
    }


    private void OnDisable()
    {
        if (this.playerInput == null || this.uiInputModule == null)
            return;

        this.StopInput();
        this.waitingForRelease = true;
    }
}