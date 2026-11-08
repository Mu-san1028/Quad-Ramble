using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 人間のPlayerInput（SendMessages）を受け取り、既存の操作処理へ渡す。
/// 移動・攻撃のルールやネットワーク処理はPlayerControllerが担当する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerActionInput))]
public sealed class HumanPlayerInput : MonoBehaviour
{
    private PlayerController player;
    private PlayerInput input;
    private PlayerActionInput actionInput;
    private InputAction fireReleaseAction;

    private bool CanReceiveInput => isActiveAndEnabled
        && player != null && player.CanReceiveHumanInput;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        input = GetComponent<PlayerInput>();
        actionInput = GetComponent<PlayerActionInput>();
    }

    private void OnMove(InputValue value)
    {
        if (CanReceiveInput) actionInput.SetMoveInput(value.Get<Vector2>());
    }

    private void OnLook(InputValue value)
    {
        if (CanReceiveInput) actionInput.SetLookInput(value.Get<Vector2>());
    }

    private void OnAim(InputValue value)
    {
        if (CanReceiveInput) actionInput.SetAimHeld(value.isPressed);
    }

    private void OnJump(InputValue value)
    {
        if (CanReceiveInput) actionInput.RequestJump(value.isPressed);
    }

    private void OnNextWeapon(InputValue value)
    {
        if (CanReceiveInput && value.isPressed) actionInput.SwitchWeapon(1);
    }

    private void OnPreviousWeapon(InputValue value)
    {
        if (CanReceiveInput && value.isPressed) actionInput.SwitchWeapon(-1);
    }

    private void OnFire(InputValue value)
    {
        if (!CanReceiveInput) return;

        // ButtonのcanceledはSendMessagesに届かない。既存どおり解除だけ直接購読する。
        // PlayerInputが複製した、このプレイヤー用のActionを使用する。
        InputAction action = input.actions.FindAction("Fire", false);
        if (fireReleaseAction != action)
        {
            UnsubscribeFireRelease();
            fireReleaseAction = action;
            if (fireReleaseAction != null) fireReleaseAction.canceled += HandleFireReleased;
        }

        actionInput.SetAttackHeld(value.isPressed);
    }

    private void HandleFireReleased(InputAction.CallbackContext context)
    {
        if (actionInput != null) actionInput.SetAttackHeld(false);
    }

    private void UnsubscribeFireRelease()
    {
        if (fireReleaseAction != null) fireReleaseAction.canceled -= HandleFireReleased;
        fireReleaseAction = null;
    }

    private void OnDisable()
    {
        UnsubscribeFireRelease();
        if (player != null && !player.IsBot && actionInput != null) actionInput.ResetInput();
    }
}
