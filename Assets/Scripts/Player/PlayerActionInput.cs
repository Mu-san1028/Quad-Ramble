using UnityEngine;

/// <summary>
/// 人間・Botに共通の操作窓口。入力状態を保持し、既存の実行処理へ即座に渡す。
/// 入力を次フレームまで遅延させず、停止中の操作は蓄積しない。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-210)]
public sealed class PlayerActionInput : MonoBehaviour
{
    private PlayerController player;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool AimHeld { get; private set; }
    public bool AttackHeld { get; private set; }

    // Botはサーバー、人間は所有端末（または既存のオフライン操作）だけが操作する。
    private bool HasAuthority => player != null && player.isActiveAndEnabled
        && (player.IsBot ? player.IsSpawned && player.IsServer : player.CanReceiveHumanInput);

    private bool CanAct => isActiveAndEnabled && HasAuthority && player.IsControllable;

    private bool HasInput => MoveInput != Vector2.zero || LookInput != Vector2.zero
        || AimHeld || AttackHeld;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    public void SetMoveInput(Vector2 input)
    {
        if (!CanAct) return;
        MoveInput = input;
        player.ApplyMoveInput(input);
    }

    public void SetLookInput(Vector2 input)
    {
        if (!CanAct) return;
        LookInput = input;
        player.ApplyLookInput(input);
    }

    /// <summary>ワールドXZ方向へ移動し、その方向へ向く。カメラの向きには依存しない。</summary>
    public void SetWorldMoveInput(Vector3 input)
    {
        if (!CanAct) return;
        input.y = 0f;
        input = Vector3.ClampMagnitude(input, 1f);
        MoveInput = new Vector2(input.x, input.z);
        LookInput = Vector2.zero;
        player.ApplyLookInput(Vector2.zero);
        player.ApplyWorldMoveInput(input);
    }

    public void SetAimHeld(bool held)
    {
        if (!CanAct) return;
        AimHeld = held;
        player.ApplyAimInput(held);
    }

    /// <summary>ジャンプは蓄積せず、その時点の接地状態で判定する。</summary>
    public void RequestJump(bool pressed = true)
    {
        if (CanAct) player.ApplyJumpInput(pressed);
    }

    public void SetAttackHeld(bool held)
    {
        // ボタン解除は操作停止中にも通し、チャージを確実に中断する。
        if (!held)
        {
            bool wasHeld = AttackHeld;
            AttackHeld = false;
            if (wasHeld && player != null) player.ApplyFireInput(false);
            return;
        }

        if (!CanAct || AttackHeld) return;
        AttackHeld = true;
        player.ApplyFireInput(true);
    }

    public void SwitchWeapon(int direction)
    {
        // 脱落中も人間の観戦対象切り替えに使う。試合中の可否は既存処理で判定する。
        if (!isActiveAndEnabled || !HasAuthority || direction == 0) return;
        if (direction > 0) player.ApplyNextWeaponInput(true);
        else player.ApplyPreviousWeaponInput(true);
    }

    public void ResetInput()
    {
        MoveInput = LookInput = Vector2.zero;
        AimHeld = AttackHeld = false;
        if (player != null) player.ResetActionInput();
    }

    private void Update()
    {
        // 暗転・終了など、RPC以外から操作不可へ変化した場合にも入力を残さない。
        if (HasInput && !CanAct) ResetInput();
    }

    private void OnDisable()
    {
        ResetInput();
    }
}
