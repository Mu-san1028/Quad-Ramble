using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;

/// <summary>
/// プレイヤーの移動、ジャンプ、慣性、カメラの処理を管理
/// - カメラ相対移動とワールド空間絶対移動の切り替え
/// - キャラクターが移動方向に向くように回転
/// </summary>
[RequireComponent(typeof(CharacterController), typeof(PlayerInput), typeof(PlayerItem))]
public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f; // 基本の移動速度
    [SerializeField] private float acceleration = 20f; // 加速力
    [SerializeField] private float deceleration = 20f; // ブレーキ力
    private CharacterController controller;
    private Vector2 moveInput;

    [SerializeField] private Transform mainCamera;
    public CinemachineCamera aimCamera;
    [SerializeField] private float turnSmoothTime = 1.0f; // キャラクターが振り向くまでの時間
    [SerializeField] private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    private Vector3 impact = Vector3.zero; // 外部から受けた衝撃を保存する
    [SerializeField] private float jumpHeight = 2f;

    private Vector3 velocity;

    private PlayerItem playerItem;

    private bool isEliminated = false;
    public bool IsControllable => (IsOwner || this.allowOfflineControl) && !isEliminated;

    [Header("カメラの透明化設定")]
    public Camera mainUnityCamera;      // 実際のカメラ機能
    public LayerMask defaultCameraMask; // 普段の視力（全部見える）
    public LayerMask aimCameraMask;     // エイム時の視力（プレイヤーだけ見えない）

    [Header("射撃・Raycast設定")]
    public LayerMask shootableLayers; // 撃てる対象のレイヤー（Player自身を除外するため）
    public float shootRange = 100f;   // 弾が届く距離

    [Header("投擲設定")]
    [SerializeField] private LayerMask groundLayers; // 地面・壁のレイヤー  

    private bool isAiming = false;

    [Header("Debug")]
    [SerializeField] private bool allowOfflineControl = false;

    public enum MoveMode { 
        CameraRelative, // カメラの向きに合わせて移動するモード（TPSやFPS用）
        WorldAbsolute // カメラの向きを無視して、ワールド空間の絶対的な北(Z)と東(X)を使うモード（見下ろし用）
    }

    // GMなどの別のスクリプトから、player.currentMoveMode = MoveMode.WorldAbsolute; のようにして、移動モードを切り替えることができる。
    public MoveMode CurrentMoveMode { get; set; } = MoveMode.CameraRelative;

    // ネットワーク上にこのキャラが生成された時に1回だけ呼ばれるメソッド
    public override void OnNetworkSpawn()
    {
        PlayerManager.Instance.AddPlayer(this);
        PlayerInput input = GetComponent<PlayerInput>();

        // プレイヤーのキー入力は、1つのPlayerInputインスタンスにしか適用されない。
        // 複数のPlayerInputが有効になっていると、意図しないインスタンスに入力を吸われるため、無効化する。
        input.enabled = IsOwner;

        // もし、このキャラが「自分の操作するキャラ」ではない（＝他人のクローン）場合
        if (!IsOwner)
        {
            // 他人のクローンについている「Aim用カメラ」をオフにする（カメラ乗っ取り防止）
            if (aimCamera != null) aimCamera.gameObject.SetActive(false);
            // 他人のクローンのメインカメラもオフにする
            if (mainUnityCamera != null) mainUnityCamera.gameObject.SetActive(false);
        }
        else
        {
            mainCamera = Camera.main.transform;
        }
    }

    void Start()
    {
        this.controller = GetComponent<CharacterController>();
        this.playerItem = GetComponent<PlayerItem>();
    }

    void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += RefreshMainCamera;
    }
    void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= RefreshMainCamera;
    }
    private void RefreshMainCamera(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!IsOwner) return;
        // シーンが切り替わったら、メインカメラを再取得する
        mainCamera = Camera.main.transform;

        var followCamObj = GameObject.FindWithTag("FollowCamera");
        if (followCamObj != null)
        {
            var followCam = followCamObj.GetComponent<CinemachineCamera>();
            if (followCam != null)
            {
                followCam.Follow = transform;
                followCam.LookAt = transform;
            }
        }
    }

    public void Eliminate()
    {
        if (!IsServer) return;
        EliminateClientRpc();
    }

    [ClientRpc]
    private void EliminateClientRpc()
    {
        isEliminated = true;
        Debug.Log($"【{gameObject.name}】が脱落しました！");
        controller.enabled = false;
        // キャラクターを非表示にする
        gameObject.GetComponentInChildren<Renderer>().enabled = false;
    }

    public void StopOrMovePlayer(bool movable)
    {
        if (!IsServer) return;
        StopOrMovePlayerClientRpc(movable);
    }

    [ClientRpc]
    private void StopOrMovePlayerClientRpc(bool movable)
    {
        controller.enabled = movable;
    }

    public void Revive()
    {
        if (!IsServer) return;
        ReviveClientRpc();
    }

    [ClientRpc]
    private void ReviveClientRpc()
    {
        isEliminated = false;
        controller.enabled = true;
        // キャラクターを表示する
        gameObject.GetComponentInChildren<Renderer>().enabled = true;
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        TeleportClientRpc(position, rotation);
    }

    [ClientRpc]
    private void TeleportClientRpc(Vector3 position, Quaternion rotation)
    {
        if (!IsOwner) return;

        // CharacterControllerが有効なままだと座標の直接代入が打ち消されるので一時的に切る
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        GetComponent<NetworkTransform>().Teleport(position, rotation, transform.localScale);
        controller.enabled = true;

        // 速度もリセット
        velocity = Vector3.zero;
        impact = Vector3.zero;
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

    // 外部のギミックによる速度変化
    public void VelocityChange(Vector3 externalAcceleration)
    {
        velocity += externalAcceleration;
    }

    void Update()
    {
        // controller が null、または無効化されている場合は処理しない
        if (controller == null || !controller.enabled) return;

        if (!this.IsControllable) return;

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

        #region ノックバックの計算
        // 衝撃が少しでも残っている場合
        if (impact.magnitude > 0.2f)
        {
            // 衝撃の方向へ強制的に移動させる
            controller.Move(impact * Time.deltaTime);
            
            // 時間経過で衝撃を減衰させる（Lerpで滑らかにゼロに近づける。5fの部分で空気抵抗を調整）
            impact = Vector3.Lerp(impact, Vector3.zero, 5f * Time.deltaTime);
        }
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

    // R2ボタンが押された時
    void OnFire(InputValue value)
    {
        if (!this.IsControllable) return;
        if (!value.isPressed) return;

        if (!this.playerItem.TryConsumeAmmo())
        {
            Debug.Log("弾、または武器がありません！");
            return;
        }

        var currentSlot = this.playerItem.CurrentWeapon;
        PlayShootEffectLocal();

        var weaponData = currentSlot?.itemData as WeaponData;

        // attackRange が設定されている → エリア攻撃
        if (weaponData != null && weaponData.attackRange != null)
        {
            var hits = weaponData.attackRange.GetHitTargets(transform.position, transform.rotation, shootableLayers);
            foreach (var col in hits)
            {
                Debug.Log($"検出: {col.gameObject.name}");
                NetworkObject targetNetObj = col.GetComponent<NetworkObject>();
                if (targetNetObj == null || targetNetObj.NetworkObjectId == NetworkObjectId) continue;

                Vector3 knockbackForce = (col.transform.position - transform.position).normalized * weaponData.Power;
                knockbackForce.y += weaponData.Power * 0.3f;
                SubmitShootServerRpc(col.transform.position, targetNetObj.NetworkObjectId, knockbackForce);
            }
            return;
        }

        // bulletPrefab があれば投擲
        if (weaponData != null && weaponData.bulletPrefab != null)
        {
            int weaponSlotIndex = this.playerItem.CurrentWeaponIndex;
            Vector3 startPos = transform.position + Vector3.forward * 0.5f;
            Vector3 throwDir = GetThrowDirection(weaponData.launchAngle);
            ThrowProjectileServerRpc(startPos, throwDir * weaponData.throwSpeed, weaponSlotIndex);
            return;
        }


        // attackRange なし → 既存の Raycast 処理
        Ray ray = new Ray(mainUnityCamera.transform.position, mainUnityCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, shootRange, shootableLayers))
        {
            NetworkObject targetNetObj = hit.collider.GetComponent<NetworkObject>();

            if (targetNetObj != null)
            {
                if (currentSlot != null && currentSlot.itemData is WeaponData wd)
                {
                    Vector3 knockbackForce = ray.direction * wd.Power;
                    knockbackForce.y += wd.Power * 0.3f;
                    SubmitShootServerRpc(hit.point, targetNetObj.NetworkObjectId, knockbackForce);
                }
            }
            else
            {
                SubmitShootServerRpc(hit.point, 0, Vector3.zero);
            }
        }
        else
        {
            SubmitShootServerRpc(ray.GetPoint(shootRange), 0, Vector3.zero);
        }
    }
    

    [ServerRpc]
    void SubmitShootServerRpc(Vector3 hitPoint, ulong targetId, Vector3 knockbackForce)
    {
        // 全員にエフェクトを再生させる
        PlayShootEffectClientRpc(hitPoint);

        // プレイヤーに当たったら、対象者にノックバック命令を出す
        if (targetId != 0)
        {
            ApplyKnockbackClientRpc(targetId, knockbackForce);
        }
    }

    [ClientRpc]
    void ApplyKnockbackClientRpc(ulong targetId, Vector3 knockbackForce)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj))
        {
            var target = netObj.GetComponent<PlayerController>();
            if (target != null && target.IsOwner)
            {
                target.TakeKnockback(knockbackForce);
            }
        }
    }


    [ClientRpc]
    void PlayShootEffectClientRpc(Vector3 hitPoint)
    {
        if (IsOwner) return;

        // 自分以外のプレイヤーの画面で、他人のキャラが銃を撃つ音や光を再生する
        PlayShootEffectLocal();
    }

    void PlayShootEffectLocal()
    {
        // ここに発砲音（AudioSource.Play）や、銃口の光（ParticleSystem.Play）、弾道エフェクトなどを再生する処理を書く
        Debug.Log($"【{gameObject.name}】が撃った！");
    }

    [ServerRpc]
    void ThrowProjectileServerRpc(Vector3 startPos, Vector3 initialVelocity, int weaponSlotIndex)
    {
        var wd = playerItem.Weapons[weaponSlotIndex]?.itemData as WeaponData;
        float power = wd?.Power ?? 10f;
        float blastRadius = wd?.blastRadius ?? 3f;
        float gravity = wd?.gravityScale ?? 9.8f;

        Vector3 landing = SimulateTrajectory(startPos, initialVelocity, gravity, out float flightTime);
        Debug.Log($"[Throw] landing: {landing}, flightTime: {flightTime}, power: {power}, blastRadius: {blastRadius}");

        ShowProjectileClientRpc(startPos, initialVelocity, flightTime, gravity, weaponSlotIndex);
        StartCoroutine(ExplodeAfterDelay(landing, flightTime, blastRadius, power));
    }

    private Vector3 SimulateTrajectory(Vector3 start, Vector3 vel, float gravity, out float flightTime)
    {
        Vector3 pos = start;
        float dt = 0.05f;
        flightTime = 0f;

        for (int i = 0; i < 200; i++)
        {
            Vector3 next = pos + vel * dt;
            vel.y -= gravity * dt;
            flightTime += dt;

            if (Physics.Linecast(pos, next, groundLayers))
                return pos;

            if (Physics.Linecast(pos, next, out RaycastHit hit, shootableLayers))
            {
                var hitNetObj = hit.collider.GetComponent<NetworkObject>();
                if (hitNetObj == null || hitNetObj.NetworkObjectId != NetworkObjectId)
                {
                    return pos;
                }
            }

            pos = next;
        }
        return pos;
    }

    [ClientRpc]
    void ShowProjectileClientRpc(Vector3 startPos, Vector3 initialVelocity, float flightTime, float gravity, int weaponSlotIndex)
    {
        var wd = playerItem.Weapons[weaponSlotIndex]?.itemData as WeaponData;
        if (wd?.bulletPrefab == null) return;
        StartCoroutine(AnimateProjectile(wd.bulletPrefab, startPos, initialVelocity, flightTime, gravity));
    }

    private IEnumerator AnimateProjectile(GameObject prefab, Vector3 start, Vector3 vel, float duration, float gravity)
    {
        Quaternion initialRotation = (vel.sqrMagnitude > 0.0001f) ? Quaternion.LookRotation(vel) : Quaternion.identity;
        var obj = Instantiate(prefab, start, initialRotation);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 pos = start + vel * elapsed;
            pos.y -= 0.5f * gravity * elapsed * elapsed;
            obj.transform.position = pos;
            yield return null;
        }
        Destroy(obj);
    }


    private IEnumerator ExplodeAfterDelay(Vector3 center, float delay, float radius, float power)
    {
        yield return new WaitForSeconds(delay);
        ExplosionClientRpc(center, radius, power);
    }

    [ClientRpc]
    void ExplosionClientRpc(Vector3 center, float radius, float power)
    {
        var hits = Physics.OverlapSphere(center, radius, shootableLayers);
        Debug.Log($"[Explosion] center: {center}, radius: {radius}, hits: {hits.Length}");

        foreach (var col in hits)
        {
            var pc = col.GetComponent<PlayerController>();
            // Debug.Log($"[Explosion] candidate: {col.name}, hasPlayerController: {pc != null}, isOwner: {pc?.IsOwner}");

            if (pc == null || !pc.IsOwner) continue;
            if (pc.NetworkObjectId == NetworkObjectId) continue;

            Vector3 force = (col.transform.position - center).normalized * power;
            force.y += power * 0.3f;
            Debug.Log($"[Explosion] TakeKnockback呼び出し: force={force}");
            pc.TakeKnockback(force);
        }
    }

    public void TakeKnockback(Vector3 force)
    {
        if (!this.IsControllable) return;
        Debug.Log($"[TakeKnockback] impact適用前: {impact}, 追加するforce: {force}");
        impact += force;
    }

    void OnDrawGizmosSelected()
    {
        if (playerItem == null) return;
        var weaponData = playerItem.CurrentWeapon?.itemData as WeaponData;
        if (weaponData == null) return;

        // 攻撃範囲（扇形・球形など）
        weaponData.attackRange?.DrawGizmos(transform.position, transform.rotation);

        // 投擲軌道
        if (weaponData.bulletPrefab != null && weaponData.attackRange == null)
        {
            Vector3 throwDir = GetThrowDirection(weaponData.launchAngle);
            Vector3 startPos = transform.position + Vector3.forward * 0.5f;
            Vector3 vel = throwDir * weaponData.throwSpeed;
            float gravity = weaponData.gravityScale;
            float dt = 0.05f;

            Gizmos.color = Color.yellow;
            Vector3 prev = startPos;

            for (int i = 0; i < 200; i++)
            {
                Vector3 next = prev + vel * dt;
                vel.y -= gravity * dt;

                if (Physics.Linecast(prev, next, groundLayers))
                {
                    Gizmos.DrawSphere(prev, 0.3f); // 着弾点
                    break;
                }

                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }

    private Vector3 GetThrowDirection(float launchAngle)
    {
        Vector3 forward = (mainUnityCamera != null) ? mainUnityCamera.transform.forward : transform.forward;
        forward.y = 0f;

        // カメラが真上/真下を向いていて水平成分がほぼ0の場合のフォールバック
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        forward.Normalize();

        float rad = launchAngle * Mathf.Deg2Rad;
        return forward * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
    }
}    
