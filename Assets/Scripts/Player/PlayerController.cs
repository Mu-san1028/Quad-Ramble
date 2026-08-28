using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
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
    [SerializeField] private float aimMoveSpeedMultiplier = 0.4f;

    private float CurrentMoveSpeed  => (moveSpeed + (status != null ? status.SpeedBonus : 0f)) * (isAiming ? aimMoveSpeedMultiplier : 1f);
    private float CurrentJumpHeight => jumpHeight + (status != null ? status.JumpBonus  : 0f);
    private float CurrentTurnSmoothTime => Mathf.Max(0.02f, turnSmoothTime + (status != null ? status.TurnBonus : 0f));

    private CharacterController controller;
    [SerializeField] private TrajectoryPreview preview;
    [SerializeField] private PlayerCameraRig cameraRig;
    [SerializeField] private WeaponEffects effects;
    private Vector2 moveInput;

    [SerializeField] private float turnSmoothTime = 1.0f; // キャラクターが振り向くまでの時間
    [SerializeField] private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    private Vector3 impact = Vector3.zero; // 外部から受けた衝撃を保存する
    [SerializeField] private float jumpHeight = 2f;

    private Vector3 velocity;

    private PlayerItem playerItem;
    private PlayerStatus status;

    private bool isEliminated = false;
    public bool IsControllable => (IsOwner || this.allowOfflineControl) && !isEliminated;
    private readonly RaycastHit[] aimHits = new RaycastHit[8];


    [Header("射撃・Raycast設定")]
    public LayerMask shootableLayers; // 撃てる対象のレイヤー（Player自身を除外するため）
    [SerializeField] private LayerMask breakableLayers;  // 壊せるブロック

    public float shootRange = 100f;   // 弾が届く距離

    [Header("投擲設定")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float throwForwardOffset = 0.8f; // 照準ピボットから前方へ離す距離

    // 照準のヨーだけを取り出した水平方向
    private Vector3 AimForwardFlat => Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward;

    // 発射点は照準ピボット基準。水平方向へ離すので、下を向いても高さが変わらない
    private Vector3 ThrowOrigin => aimPivot != null
        ? aimPivot.position + AimForwardFlat * throwForwardOffset
        : transform.position + transform.forward * throwForwardOffset;

    [Header("軌道表示")]
    [SerializeField] private int trajectoryMaxSteps = 60; // サーバーの弾道判定もこの値を使う
    private readonly List<Vector3> trajectoryPoints = new();

    [Header("照準")]
    // カメラのブームの根元。この向きがそのまま視線であり、投擲方向でもある
    [SerializeField] private Transform aimPivot;
    [SerializeField] private float yawSensitivity = 180f;     // 度/秒
    [SerializeField] private float pitchSensitivity = 120f;   // 度/秒
    [SerializeField] private float aimPitchMin = -60f;        // 見下ろしの限界
    [SerializeField] private float aimPitchMax = 70f;         // 見上げの限界

    // true = 常に視線の向き（ストレイフ移動） / false = 移動方向を向く（原神型）
    [SerializeField] private bool alwaysFaceCamera = true;

    private Vector2 lookInput;
    private float aimYaw;
    private float aimPitch;
    private bool isAiming = false;

    [Header("Debug")]
    [SerializeField] private bool allowOfflineControl = false;

    public enum MoveMode { 
        CameraRelative, // カメラの向きに合わせて移動するモード（TPSやFPS用）
        WorldAbsolute // カメラの向きを無視して、ワールド空間の絶対的な北(Z)と東(X)を使うモード（見下ろし用）
    }

    public MoveMode CurrentMoveMode { get; set; } = MoveMode.CameraRelative;

    private TrajectorySimulator.Settings TrajectorySettings => new()
    {
        groundLayers = groundLayers,
        blockerLayers = shootableLayers,
        maxSteps = trajectoryMaxSteps,
        ignoreNetworkObjectId = NetworkObjectId,
    };

    public override void OnNetworkSpawn()
    {
        PlayerManager.Instance.AddPlayer(this);
        PlayerInput input = GetComponent<PlayerInput>();

        input.enabled = IsOwner;
        if (IsOwner) PairInputDevice(input);

        // 照準の表示は自分の画面にだけ出す
        if (!IsOwner) preview.Show(false);
    }

    public override void OnNetworkDespawn()
    {
        PlayerManager.Instance?.RemovePlayer(this);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += RefreshMainCamera;
    }
    void OnDisable()
    {
        SceneManager.sceneLoaded -= RefreshMainCamera;
    }

    void Start()
    {
        this.controller = GetComponent<CharacterController>();
        this.playerItem = GetComponent<PlayerItem>();
        this.status = GetComponent<PlayerStatus>();
    }

    private void RefreshMainCamera(Scene scene, LoadSceneMode mode)
    {
        if (!IsOwner) return;
        isAiming = false;
        cameraRig.Rebind(aimPivot != null ? aimPivot : transform);
    }

    /// <summary>この端末に割り当てられたパッドだけを受け付ける</summary>
    private void PairInputDevice(PlayerInput input)
    {
        var pad = LocalGamepad.Current;
        if (pad == null)
        {
            // パッドが無ければ従来どおり全デバイス受付（エディタのキーボード確認を壊さない）
            if (LocalGamepad.Seat > 0) Debug.LogError($"[Input] seat={LocalGamepad.Seat} のパッドが見つかりません");
            return;
        }

        input.neverAutoSwitchControlSchemes = true;   // 他のデバイスに触れても奪われない
        input.SwitchCurrentControlScheme("Gamepad", pad);
    }

    #region ネットワーク経由での脱落・復活・移動制御
    /// <summary>脱落をクライアントに反映させる</summary>
    public void Eliminate()
    {
        if (!IsServer) return;
        EliminateClientRpc();
    }

    [ClientRpc]
    private void EliminateClientRpc()
    {
        isEliminated = true;
        isAiming = false;
        Debug.Log($"【{gameObject.name}】が脱落しました！");
        controller.enabled = false;
        // キャラクターを非表示にする
        gameObject.GetComponentInChildren<Renderer>().enabled = false;
    }

    /// <summary>移動の可否をクライアントに反映させる</summary>
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

    /// <summary>復活をクライアントに反映させる</summary>
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

    /// <summary>テレポートをクライアントに反映させる</summary>
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

        // 照準もスポーン地点の向きに合わせる。
        // これが無いと、体が次のフレームで前ラウンドの aimYaw に向き直ってしまう
        aimYaw = rotation.eulerAngles.y;
        aimPitch = 0f;
        isAiming = false;

        // 速度もリセット
        velocity = Vector3.zero;
        impact = Vector3.zero;
    }
    #endregion

    #region 入力イベント
    void OnJump(InputValue value)
    {
        // ボタンが押されたら
        if (value.isPressed)
        {
            // 地面にいればジャンプ
            if (controller.isGrounded)
            {
                velocity.y = Mathf.Sqrt(CurrentJumpHeight * -2f * gravity);
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

    void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void OnAim(InputValue value)
    {
        isAiming = value.isPressed;
    }

    void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }

    void OnNextWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            this.playerItem.SwitchWeapon(1);
        }
    }

    void OnPreviousWeapon(InputValue value)
    {
        if (value.isPressed)
        {
            this.playerItem.SwitchWeapon(-1);
        }
    }

    void OnFire(InputValue value)
    {
        if (!this.IsControllable) return;
        if (!value.isPressed) return;

        if (!this.playerItem.TryConsumeAmmo())
        {
            Debug.Log("弾、または武器がありません！");
            return;
        }

        this.FireCurrentWeapon();
        this.playerItem.DiscardIfEmpty();
    }
    #endregion

    // 外部のギミックによる速度変化
    public void VelocityChange(Vector3 externalAcceleration)
    {
        velocity += externalAcceleration;
    }

    void Update()
    {
        this.UpdateAimInput();
        cameraRig.UpdateAim(isAiming);
        this.UpdateTrajectoryPreview();

        if (controller != null && controller.enabled && this.IsControllable)
        {
            this.UpdateMovement();
        }
        // 体の回転より後に適用する。親の回転を継承させないため
        this.ApplyAimPivot();
    }

    /// <summary>右スティックの入力を照準のヨー・ピッチに積分する</summary>
    private void UpdateAimInput()
    {
        if (!IsOwner) return;

        float dt = Time.deltaTime;

        aimYaw += lookInput.x * yawSensitivity * dt;
        aimYaw = Mathf.Repeat(aimYaw + 180f, 360f) - 180f;

        aimPitch = Mathf.Clamp(aimPitch + lookInput.y * pitchSensitivity * dt, aimPitchMin, aimPitchMax);
    }

    /// <summary>照準ピボットにワールド回転を直接書き込む。子であっても体の回転を継承しない</summary>
    private void ApplyAimPivot()
    {
        if (!IsOwner || aimPivot == null) return;

        // aimPitch は「正が上向き」。UnityのX回転は正が下向きなので符号を反転する
        aimPivot.rotation = Quaternion.Euler(-aimPitch, aimYaw, 0f);
    }

    private void UpdateMovement()
    {
        #region カメラ相対移動の計算
        Vector3 forwardDirection, rightDirection;

        if (this.CurrentMoveMode == MoveMode.CameraRelative)
        {
            // 照準のヨーを基準にする。カメラがピッチしても移動方向がぶれない
            Quaternion yawRot = Quaternion.Euler(0f, aimYaw, 0f);
            forwardDirection = yawRot * Vector3.forward;
            rightDirection = yawRot * Vector3.right;
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
        float targetAngle = 0f;
        bool hasTarget = false;

        if (isAiming || alwaysFaceCamera)
        {
            // 体は視線の向き。Fortnite 型では移動は横歩き・後ろ歩きになる
            targetAngle = aimYaw;
            hasTarget = true;
        }
        else if (moveDirection.magnitude > 0.01f)
        {
            targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            hasTarget = true;
        }

        if (hasTarget)
        {
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, CurrentTurnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }
        #endregion

        #region 慣性の計算
        // 目標速度（入力が半分なら速度も半分になるように Mathf.Clamp01 を使用）
        Vector3 targetVelocity = moveDirection.normalized * CurrentMoveSpeed * Mathf.Clamp01(moveInput.magnitude);
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

    private void FireCurrentWeapon()
    {
        var weaponData = this.playerItem.CurrentWeapon?.itemData as WeaponData;
        if (weaponData == null) return;

        effects.PlayShoot(gameObject.name);

        switch (weaponData.weaponType)
        {
            case WeaponType.Melee:      this.FireMelee(weaponData);      break;
            case WeaponType.Throw:      this.FireThrow(weaponData);      break;
            case WeaponType.Deployable: this.FireDeployable(weaponData); break;
        }
    }

    private void FireMelee(WeaponData weaponData)
    {
        if (weaponData.attackRange == null)
        {
            Debug.LogWarning($"{weaponData.itemName} は Melee ですが attackRange が未設定です");
            return;
        }

        Items weaponKey = ItemDatabase.Instance.GetKey(weaponData);
        var hits = weaponData.attackRange.GetHitTargets(transform.position, transform.rotation,
                                                        shootableLayers | breakableLayers);
        var hitIds = new HashSet<ulong>();

        foreach (var col in hits)
        {
            NetworkObject targetNetObj = col.GetComponentInParent<NetworkObject>();
            if (targetNetObj == null || targetNetObj.NetworkObjectId == NetworkObjectId) continue;
            if (!hitIds.Add(targetNetObj.NetworkObjectId)) continue;

            if (col.GetComponentInParent<BreakableBlock>() != null)
            {
                BreakBlockServerRpc(targetNetObj.NetworkObjectId, weaponKey);
                continue;
            }

            Vector3 direction = (col.transform.position - transform.position).normalized;
            SubmitShootServerRpc(col.transform.position, targetNetObj.NetworkObjectId, direction, weaponKey);
        }
    }

    private void FireThrow(WeaponData weaponData)
    {
        if (weaponData.bulletPrefab == null)
        {
            Debug.LogWarning($"{weaponData.itemName} は Throw ですが bulletPrefab が未設定です");
            return;
        }

        Items weaponKey = ItemDatabase.Instance.GetKey(weaponData);
        Vector3 startPos = ThrowOrigin;
        Vector3 throwDir = GetThrowDirection(weaponData.launchAngle);
        ThrowProjectileServerRpc(startPos, throwDir * weaponData.throwSpeed, weaponKey);
    }

    private void FireDeployable(WeaponData weaponData)
    {
        // TODO: 設置物のスポーン（次のステップで実装）
        Debug.Log($"{weaponData.itemName} を設置（未実装）");
    }

    [ServerRpc]
    void SubmitShootServerRpc(Vector3 hitPoint, ulong targetId, Vector3 direction, Items weaponKey)
    {
        PlayShootEffectClientRpc(hitPoint);
        if (targetId == 0) return;

        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        float power = wd != null ? wd.Power : 10f;

        this.ApplyKnockback(targetId, direction, power);
    }

    [ServerRpc]
    void BreakBlockServerRpc(ulong blockId, Items weaponKey)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(blockId, out var netObj)) return;

        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        netObj.GetComponent<BreakableBlock>()?.AddDamage(wd != null ? wd.Power : 0f);
    }

    /// <summary>サーバーが最終的なノックバックを決めて対象に適用させる</summary>
    private void ApplyKnockback(ulong targetId, Vector3 direction, float power)
    {
        if (!IsServer) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj)) return;

        var targetStatus = netObj.GetComponent<PlayerStatus>();

        // 先に疲労を蓄積し、その値を含めた倍率で吹き飛ばす
        targetStatus?.AddFatigue(power);
        float multiplier = targetStatus != null ? targetStatus.KnockbackMultiplier : 1f;

        Vector3 force = direction.normalized * power * multiplier;
        force.y += power * 0.3f * multiplier;

        Debug.Log($"[Knockback] power={power}, fatigue={targetStatus?.Fatigue}, multiplier={multiplier:F2}");

        ApplyKnockbackClientRpc(targetId, force);
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
        effects.PlayShoot(gameObject.name);
    }

    [ServerRpc]
    void ThrowProjectileServerRpc(Vector3 startPos, Vector3 initialVelocity, Items weaponKey)
    {
        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        float power = wd?.Power ?? 10f;
        float blastRadius = wd?.blastRadius ?? 3f;
        float gravity = wd?.gravityScale ?? 9.8f;

        Vector3 landing = TrajectorySimulator.Simulate(startPos, initialVelocity, gravity,
                                                       TrajectorySettings, out float flightTime, out bool hitSomething);

        ShowProjectileClientRpc(startPos, initialVelocity, flightTime, gravity, weaponKey);

        // 何にも当たらなかった弾は、空中で爆発せずにそのまま消滅する
        if (!hitSomething) return;

        Debug.Log($"[Throw] landing: {landing}, flightTime: {flightTime}, power: {power}, blastRadius: {blastRadius}");
        StartCoroutine(ExplodeAfterDelay(landing, flightTime, blastRadius, power));
    }

    private void UpdateTrajectoryPreview()
    {
        if (!IsOwner) return;

        var weaponData = this.playerItem != null
            ? this.playerItem.CurrentWeapon?.itemData as WeaponData
            : null;

        if (!isAiming || !IsControllable || weaponData == null || weaponData.weaponType != WeaponType.Throw)
        {
            preview.Show(false);
            return;
        }

        Vector3 vel = GetThrowDirection(weaponData.launchAngle) * weaponData.throwSpeed;
        Vector3 landing = TrajectorySimulator.Simulate(ThrowOrigin, vel, weaponData.gravityScale,
                                                       TrajectorySettings, out _, out bool hit,
                                                       trajectoryPoints);
        preview.Draw(trajectoryPoints, landing, hit);
    }

    [ClientRpc]
    void ShowProjectileClientRpc(Vector3 startPos, Vector3 initialVelocity, float flightTime, float gravity, Items weaponKey)
    {
        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        if (wd?.bulletPrefab == null) return;
        StartCoroutine(effects.Projectile(wd.bulletPrefab, startPos, initialVelocity, flightTime, gravity));
    }

    private IEnumerator ExplodeAfterDelay(Vector3 center, float delay, float radius, float power)
    {
        yield return new WaitForSeconds(delay);
        if (!IsSpawned) yield break;
        if (GameManager.Instance.currentState != GameState.Playing) yield break;

        // 演出は全員に
        ExplosionEffectClientRpc(center, radius);

        // 判定はサーバーだけが行う
        var hits = Physics.OverlapSphere(center, radius, shootableLayers | breakableLayers);
        var hitIds = new HashSet<ulong>();

        foreach (var col in hits)
        {
            var target = col.GetComponentInParent<NetworkObject>();
            if (target == null) continue;
            if (target.NetworkObjectId == NetworkObjectId) continue;   // 自分は無視
            if (!hitIds.Add(target.NetworkObjectId)) continue;

            var block = col.GetComponentInParent<BreakableBlock>();
            if (block != null) { block.AddDamage(power); continue; }   // ここはサーバー実行なので直接呼べる

            if (col.GetComponentInParent<PlayerController>() == null) continue;

            Vector3 direction = (col.transform.position - center).normalized;
            this.ApplyKnockback(target.NetworkObjectId, direction, power);
        }
    }

    [ClientRpc]
    void ExplosionEffectClientRpc(Vector3 center, float radius)
    {
        effects.PlayExplosion(center, radius);
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
    }

    /// <summary>画面中央が指すワールド座標。カメラは自分の後ろにあるので自分自身は無視する</summary>
    private Vector3 GetAimPoint()
    {
        Ray ray = new Ray(cameraRig.Main.transform.position, cameraRig.Main.transform.forward);
        Vector3 point = ray.GetPoint(shootRange);   // 何にも当たらなければ遠方の点

        int count = Physics.RaycastNonAlloc(ray, aimHits, shootRange, groundLayers | shootableLayers);
        float nearest = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var netObj = aimHits[i].collider.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == NetworkObjectId) continue;

            if (aimHits[i].distance < nearest)
            {
                nearest = aimHits[i].distance;
                point = aimHits[i].point;
            }
        }
        return point;
    }

    /// <summary>銃口から狙点へ向ける。カメラと銃口の視差をここで吸収する</summary>
    private Vector3 GetThrowDirection(float launchAngle)
    {
        if (aimPivot == null) return transform.forward;

        // 構えていないときは、狙点ではなく武器ごとの既定の弧で投げる
        if (!isAiming)
        {
            float rad = launchAngle * Mathf.Deg2Rad;
            return AimForwardFlat * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
        }

        if (cameraRig.Main == null) return aimPivot.forward;

        Vector3 dir = GetAimPoint() - ThrowOrigin;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : aimPivot.forward;
    }
}    
