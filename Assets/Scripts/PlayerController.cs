using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの移動、ジャンプ、慣性、カメラの処理
/// - カメラ相対移動とワールド空間絶対移動の切り替え
/// - キャラクターが移動方向に向くように回転
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f; // 基本の移動速度
    [SerializeField] private float acceleration = 20f; // 加速力
    [SerializeField] private float deceleration = 20f; // ブレーキ力
    private CharacterController controller;
    private Vector2 moveInput;

    public Transform mainCamera;
    public float turnSmoothTime = 0.3f; // キャラクターが振り向くまでの時間
    private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    [SerializeField] private float jumpHeight = 2f;
    private Vector3 velocity;

    public enum MoveMode { 
        CameraRelative, // カメラの向きに合わせて移動するモード（TPSやFPS用）
        WorldAbsolute // カメラの向きを無視して、ワールド空間の絶対的な北(Z)と東(X)を使うモード（見下ろし用）
    }

    // GMなどの別のスクリプトから、player.currentMoveMode = MoveMode.WorldAbsolute; のようにして、移動モードを切り替えることができる。
    public MoveMode currentMoveMode = MoveMode.CameraRelative;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        // メインカメラが設定されていなければ、自動でシーン内のMain Cameraを探す
        if (mainCamera == null)
        {
            mainCamera = Camera.main.transform;
        }
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

    void Update()
    {
        #region カメラ相対移動の計算
        Vector3 forwardDirection, rightDirection;

        if (currentMoveMode == MoveMode.CameraRelative)
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
        if (moveDirection.magnitude > 0.01f)
        {
            // Atan2関数を使って、X軸とZ軸の値から「向くべき角度」を計算
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
}    

