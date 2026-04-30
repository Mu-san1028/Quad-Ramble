using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// プレイヤーの移動、ジャンプ、慣性、カメラの処理とアイテムの取得を管理
/// - カメラ相対移動とワールド空間絶対移動の切り替え
/// - キャラクターが移動方向に向くように回転
/// - アイテムを取得したときの処理
/// </summary>
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f; // 基本の移動速度
    [SerializeField] private float acceleration = 20f; // 加速力
    [SerializeField] private float deceleration = 20f; // ブレーキ力
    private CharacterController controller;
    private Vector2 moveInput;

    public Transform mainCamera;
    public float turnSmoothTime = 1.0f; // キャラクターが振り向くまでの時間
    private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    [SerializeField] private float jumpHeight = 2f;

    [SerializeField]public WeaponData[] weapons = new WeaponData[3];
    public int currentWeaponIndex; // 現在装備している武器
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

    public void GetItem(ItemBase itemBase)
    {
        if (itemBase is WeaponData weapon)
        {
            Debug.Log("武器を取得: " + weapon.name);
            // 武器の取得処理（例：インベントリに追加）
            // 空いているスロット（nullの場所）を探す
            int emptySlotIndex = -1;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == null)
                {
                    emptySlotIndex = i;
                    break;
                }
            }

            if (emptySlotIndex != -1)
            {
                // 空き枠がある場合：そこに武器を入れる
                weapons[emptySlotIndex] = weapon;
                Debug.Log($"スロット {emptySlotIndex + 1} に {weapon.name} を収納した！");

                // もし初めて武器を拾った（今まで丸腰だった）なら、自動でそれを装備する
                if (CurrentWeapon == null || emptySlotIndex == 0) 
                {
                    EquipWeapon(emptySlotIndex);
                }
            }
            else
            {
                // カバンがいっぱいの場合：今の武器と入れ替える
                Debug.Log($"カバンが満杯！ {CurrentWeapon.name} を捨てて {weapon.name} を装備した！");
                
                // TODO: ここで「古い武器(CurrentWeapon)のPrefabを目の前にドロップする処理」を入れる
                
                // 現在のスロットを新しい武器で上書きする
                weapons[currentWeaponIndex] = weapon;
                EquipWeapon(currentWeaponIndex);
            }
        }
        else if (itemBase is ConsumableItemData consumable)
        {
            Debug.Log("消費アイテムを取得: " + consumable.name);
            StartCoroutine(ApplyBoost(consumable));
        }
    }

    private IEnumerator ApplyBoost(ConsumableItemData consumable)
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

    public WeaponData CurrentWeapon
    {
        get { return weapons[currentWeaponIndex]; }
    }

    // Rボタン（Right Shoulder）を押した時：次の武器へ
    void OnNextWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            SwitchWeapon(1);
        }
    }

    // Lボタン（Left Shoulder）を押した時：前の武器へ
    void OnPreviousWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            SwitchWeapon(-1);
        }
    }

    // 武器切り替えの共通ロジック（direction は 1 または -1 が入る）
    private void SwitchWeapon(int direction)
    {
        // 3つのスロットを順番に探す
        for (int i = 1; i <= weapons.Length; i++)
        {
            int step = i * direction;
            int nextIndex = (currentWeaponIndex + step + weapons.Length) % weapons.Length;

            // そのスロットに武器が入っていれば持ち替えて終了
            if (weapons[nextIndex] != null)
            {
                EquipWeapon(nextIndex);
                break;
            }
        }
    }

    private void EquipWeapon(int index)
    {
        currentWeaponIndex = index;
        Debug.Log($"スロット {index + 1} の {CurrentWeapon.name} に持ち替えた！");
        
        // TODO: 手に持っている3Dモデルを切り替える処理
        // TODO: UI（アイコンや残弾数）を更新する処理
    }
}    

