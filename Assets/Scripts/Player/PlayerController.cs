using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// プレイヤーの移動、ジャンプ、慣性、カメラの処理を管理
/// - カメラ相対移動とワールド空間絶対移動の切り替え
/// - キャラクターが移動方向に向くように回転
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(PlayerItem))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f; // 基本の移動速度
    [SerializeField] private float acceleration = 20f; // 加速力
    [SerializeField] private float deceleration = 20f; // ブレーキ力
    private CharacterController controller;
    private PlayerInput playerInput;
    private Vector2 moveInput;

    [SerializeField] private Transform mainCamera;
    public Unity.Cinemachine.CinemachineCamera aimCamera;
    [SerializeField] private float turnSmoothTime = 1.0f; // キャラクターが振り向くまでの時間
    [SerializeField] private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    [SerializeField] private float jumpHeight = 2f;

    private Vector3 velocity;

    private PlayerItem playerItem;

    [Header("カメラの透明化設定")]
    public Camera mainUnityCamera;      // 実際のカメラ機能
    public LayerMask defaultCameraMask; // 普段の視力（全部見える）
    public LayerMask aimCameraMask;     // エイム時の視力（プレイヤーだけ見えない）

    private bool isAiming = false;

    public enum MoveMode { 
        CameraRelative, // カメラの向きに合わせて移動するモード（TPSやFPS用）
        WorldAbsolute // カメラの向きを無視して、ワールド空間の絶対的な北(Z)と東(X)を使うモード（見下ろし用）
    }

    // GMなどの別のスクリプトから、player.currentMoveMode = MoveMode.WorldAbsolute; のようにして、移動モードを切り替えることができる。
    public MoveMode CurrentMoveMode { get; set; } = MoveMode.CameraRelative;

    void Start()
    {
        this.controller = GetComponent<CharacterController>();
        this.playerItem = GetComponent<PlayerItem>();
        playerInput = GetComponent<PlayerInput>();

        // メインカメラが設定されていなければ、自動でシーン内のMain Cameraを探す
        if (mainCamera == null)
        {
            mainCamera = Camera.main.transform;
        }
        PlayerManager.Instance.AddPlayer(this);
    }

    void OnJump(InputValue value)
    {
        // ボタンが押されたら
        if (value.isPressed)
        {
            // 地面にいればジャンプ
            if (controller.isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            // ボタンが離されたら下向きに加速度を働かせる
            if (velocity.y > 0f)
            {
                velocity.y *= 0.5f; 
            }
        }
    }

    // WASD入力があるたびに自動で呼ばれる
    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void OnAim(InputValue value)
    {
        if (value.isPressed)
        {
            aimCamera.Priority = 20;
            mainUnityCamera.cullingMask = aimCameraMask;
            isAiming = true;
        }
        else
        {
            aimCamera.Priority = 0;
            mainUnityCamera.cullingMask = defaultCameraMask;
            isAiming = false;
        }
    }

    void Update()
    {
        #region カメラ相対移動の計算
        Vector3 forwardDirection, rightDirection;

        if (this.CurrentMoveMode == MoveMode.CameraRelative)
        {
            // カメラの向きに合わせるモード（TPSやFPS用）
            forwardDirection = mainCamera.forward;
            rightDirection = mainCamera.right;
            forwardDirection.y = 0f;
            rightDirection.y = 0f;
            forwardDirection.Normalize();
            rightDirection.Normalize();
        }
        else
        {
            // カメラの向きを無視して、ワールド空間の絶対的な北(Z)と東(X)を使うモード（見下ろし用）
            forwardDirection = Vector3.forward;
            rightDirection = Vector3.right;
        }

        // 進行方向を決定
        Vector3 moveDirection = forwardDirection * moveInput.y + rightDirection * moveInput.x;
        #endregion

        #region キャラを振り向かせる
        if (moveDirection.magnitude > 0.01f && !isAiming)
        {
            // Atan2関数を使って、X軸とZ軸の値から向くべき角度を計算
            float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            
            // 現在の角度から目標の角度へ、turnSmoothTime秒かけて滑らかに回転させる
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }
        #endregion

        #region 慣性の計算
        // 目標速度（入力が半分なら速度も半分になるように Mathf.Clamp01 を使用）
        Vector3 targetVelocity = moveDirection.normalized * moveSpeed * Mathf.Clamp01(moveInput.magnitude);
        float accelRate = (moveInput.magnitude > 0.01f) ? acceleration : deceleration;

        // X軸とZ軸の慣性
        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocity.x, accelRate * Time.deltaTime);
        velocity.z = Mathf.MoveTowards(velocity.z, targetVelocity.z, accelRate * Time.deltaTime);
        #endregion

        #region 重力の計算
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
        velocity.y += gravity * Time.deltaTime;
        #endregion

        controller.Move(velocity * Time.deltaTime);
    }

    public void ApplyBoost(ConsumableItemData consumable)
    {
        StartCoroutine(this.ApplyBoostCoroutine(consumable));
    }

    private IEnumerator ApplyBoostCoroutine(ConsumableItemData consumable)
    {
        float originalSpeed = moveSpeed;
        float originalJumpHeight = jumpHeight;
        float originalTurnSpeed = turnSmoothTime;

        // ブーストを適用
        moveSpeed += consumable.speedBoost;
        jumpHeight += consumable.jumpBoost;
        turnSmoothTime -= consumable.turnSpeedBoost;

        // ブーストの効果が切れるまで待機
        yield return new WaitForSeconds(consumable.duration);

        // 元の値に戻す
        moveSpeed = originalSpeed;
        jumpHeight = originalJumpHeight;
        turnSmoothTime = originalTurnSpeed;
    }

    // Rボタン（Right Shoulder）を押した時：次の武器へ
    void OnNextWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            this.playerItem.SwitchWeapon(1);
        }
    }

    // Lボタン（Left Shoulder）を押した時：前の武器へ
    void OnPreviousWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            this.playerItem.SwitchWeapon(-1);
        }
    }
}    
