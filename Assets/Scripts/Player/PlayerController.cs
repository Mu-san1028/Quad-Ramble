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
[DefaultExecutionOrder(-200)]
public class PlayerController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 5f; // 基本の移動速度
    [SerializeField] private float acceleration = 20f; // 加速力
    [SerializeField] private float deceleration = 10f; // ブレーキ力
    [SerializeField] private float aimMoveSpeedMultiplier = 0.4f;

    private float CurrentMoveSpeed => (moveSpeed + (status != null ? status.SpeedBonus : 0f))
        * (status != null ? status.SlowSpeedMultiplier : 1f)
        * (isAiming ? aimMoveSpeedMultiplier : 1f);
    private float CurrentJumpHeight => (jumpHeight + (status != null ? status.JumpBonus : 0f))
        * (status != null ? status.JumpReductionMultiplier : 1f);
    private float CurrentTurnSmoothTime => Mathf.Max(0.02f, turnSmoothTime);

    private CharacterController controller;
    private readonly RaycastHit[] stepGroundHits = new RaycastHit[16];
    private readonly RaycastHit[] jumpGroundHits = new RaycastHit[16];
    private readonly Collider[] raceOverlapHits = new Collider[64];
    private bool traversingRaceStep;

    // 接地の真値。所有者しか Move() を呼ばないので、他人のコピーでは isGrounded が更新されない
    private readonly NetworkVariable<bool> networkGrounded = new(writePerm: NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> networkRising = new(writePerm: NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<Vector2> networkViewAngles = new(writePerm: NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> networkAiming = new(writePerm: NetworkVariableWritePermission.Owner);

    // 所有者のカメラ補正を含む実際の照準を一括同期する。
    private struct AimLineState : INetworkSerializable, System.IEquatable<AimLineState>
    {
        public bool visible;
        public int weaponKey;
        public Vector3 start;
        public Vector3 end;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref visible);
            serializer.SerializeValue(ref weaponKey);
            serializer.SerializeValue(ref start);
            serializer.SerializeValue(ref end);
        }

        public bool Equals(AimLineState other) => visible == other.visible && weaponKey == other.weaponKey
            && start.Equals(other.start) && end.Equals(other.end);
    }

    private readonly NetworkVariable<AimLineState> networkAimLine = new(writePerm: NetworkVariableWritePermission.Owner);

    private PlayerController spectatorTarget;
    private Transform spectatorAnchor;
    private bool spectating;
    private bool spectatorAiming;
    private float spectatorPitch;
    private bool spectatorTargetChanged;
    private SpectatorUI spectatorHud;
    [SerializeField] private SpectatorUI spectatorUIPrefab;

    public PlayerController SpectatorTarget => this.spectatorTarget;

    /// <summary>坂道の座標変化ではなく、ジャンプ・衝撃による上昇をアニメーションへ伝える。</summary>
    public bool IsRising => (IsSpawned && !IsOwner)
        ? this.networkRising.Value
        : (this.IsControllable && this.velocity.y + this.impact.y > 0.1f);

    /// <summary>アニメーション用の接地判定。自分とオフラインは実測、他人は同期された値</summary>
    public bool IsGrounded => (IsSpawned && !IsOwner)
        ? this.networkGrounded.Value
        : (this.controller != null && this.controller.isGrounded);

    [SerializeField] private TrajectoryPreview preview;
    [SerializeField] private PlayerCameraRig cameraRig;
    [SerializeField] private WeaponEffects effects;
    private Vector2 moveInput;

    [SerializeField] private float turnSmoothTime = 5.0f; // キャラクターが振り向くまでの時間
    [SerializeField] private float turnSmoothVelocity; 

    [SerializeField] private float gravity = -9.8f;
    private Vector3 impact = Vector3.zero; // 外部から受けた衝撃を保存する
    [SerializeField] private float jumpHeight = 2f;

    private Vector3 velocity;

    private PlayerItem playerItem;
    private PlayerStatus status;

    private readonly NetworkVariable<bool> eliminated = new(false);
    public bool IsEliminated => this.eliminated.Value;
    private bool isEliminated => this.IsEliminated;
    private readonly Dictionary<Renderer, bool> eliminatedRenderers = new();
    private readonly Dictionary<Collider, bool> eliminatedColliders = new();
    private bool controlsStopped;
    private Quaternion teleportRotation;
    private bool pendingStartFacing;

    public bool IsTitleMode { get; private set; }
    public int LastPlacementRound { get; private set; } = -1;

    public bool IsControllable =>
        (this.IsOwner || this.allowOfflineControl)
        && !this.isEliminated
        && !this.IsTitleMode
        && !this.controlsStopped
        && !SceneTransitionUI.InputBlocked
        && (RoundManager.Instance == null || !RoundManager.Instance.IsRoundOver);

    private readonly RaycastHit[] aimHits = new RaycastHit[8];

    // 攻撃側の強化倍率。status がまだ無いフレームでは 1 倍として扱う
    private float PowerMultiplier => this.status != null ? this.status.PowerMultiplier : 1f;
    private float RangeMultiplier => this.status != null ? this.status.RangeMultiplier : 1f;

    private Coroutine windupRoutine;   // 溜め中のコルーチン。ラウンドをまたいで残さない
    private bool fireHeld;
    private InputAction fireReleaseAction;
    public bool IsCharging => windupRoutine != null;
    public float ChargeProgress { get; private set; }
    private int nextProjectileId;


    [Header("射撃・Raycast設定")]
    public LayerMask shootableLayers; // 撃てる対象のレイヤー（Player自身を除外するため）
    [SerializeField] private LayerMask breakableLayers;  // 壊せるブロック
    [SerializeField] private LayerMask bombPushLayers;   // BombPushBomb専用レイヤー

    // 近接範囲と投擲爆発範囲で共通して調べる対象レイヤー。
    private LayerMask attackableLayers => this.shootableLayers | this.breakableLayers | this.bombPushLayers;

    public float shootRange = 100f;   // 弾が届く距離

    [Header("投擲設定")]
    [SerializeField] private LayerMask groundLayers;
    [SerializeField] private float throwForwardOffset = 0.8f; // 照準ピボットから前方へ離す距離

    // 照準のヨーだけを取り出した水平方向
    private Vector3 AimForwardFlat => Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward;

    // 発射点は照準ピボット基準。水平方向へ離すので、下を向いても高さが変わらない
    private Vector3 ThrowOrigin
    {
        get
        {
            Vector3 origin = aimPivot != null
                ? aimPivot.position + AimForwardFlat * throwForwardOffset
                : transform.position + transform.forward * throwForwardOffset;
            var weapon = playerItem != null ? playerItem.CurrentWeapon?.itemData as WeaponData : null;
            return origin + Vector3.up * (weapon != null ? weapon.launchHeightOffset : 0f);
        }
    }

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
        blockerLayers = shootableLayers | bombPushLayers,
        maxSteps = trajectoryMaxSteps,
        ignoreNetworkObjectId = NetworkObjectId,
    };

    private void Awake()
    {
        this.controller = GetComponent<CharacterController>();
        // PlayerInputのOnEnableより先に止め、所有者とシーンが確定してから有効化する。
        if (!this.allowOfflineControl)
            GetComponent<PlayerInput>().enabled = false;
    }


    public override void OnNetworkSpawn()
    {
        this.eliminated.OnValueChanged += this.HandleEliminatedChanged;
        this.ApplyEliminatedState(this.IsEliminated);
        // TitlePlayerPresentation.Updateを待たず、生成時点で入力の担当を確定する。
        this.IsTitleMode = FindAnyObjectByType<TitleInputController>() != null;

        PlayerManager.Instance.AddPlayer(this);
        PlayerInput input = GetComponent<PlayerInput>();

        if (this.IsOwner && !this.IsTitleMode)
        {
            this.PairInputDevice(input);
        }
        else
        {
            input.enabled = false;
        }

        // 照準の表示は自分の画面にだけ出す
        if (!IsOwner) preview.Hide();
    }

    /// <summary>Title中のゲーム操作と移動を停止し、切り替え時の入力・速度をリセットする。</summary>
    public void SetTitleMode(bool enabled)
    {
        if (this.IsTitleMode == enabled)
            return;

        this.IsTitleMode = enabled;

        PlayerInput input = GetComponent<PlayerInput>();

        bool canUseInput =
            this.IsOwner
            || (!this.IsSpawned && this.allowOfflineControl);

        if (!enabled && canUseInput)
        {
            this.PairInputDevice(input);
        }
        else
        {
            input.enabled = false;
        }

        // 入力停止前の操作や、移動の勢いを持ち越さない。
        this.moveInput = Vector2.zero;
        this.lookInput = Vector2.zero;
        this.velocity = Vector3.zero;
        this.impact = Vector3.zero;
        this.turnSmoothVelocity = 0f;
        this.isAiming = false;

        // 通常操作へ戻ったとき、古い照準方向へ振り向かないようにする。
        this.aimYaw = transform.eulerAngles.y;
        this.aimPitch = 0f;

        this.CancelCharge();

        if (this.preview != null)
        {
            this.preview.Hide();
        }
    }

    public override void OnNetworkDespawn()
    {
        CancelCharge();
        this.preview?.Hide();
        this.ClearSpectating(false);
        this.eliminated.OnValueChanged -= this.HandleEliminatedChanged;
        this.effects.ClearProjectiles();
        PlayerManager.Instance?.RemovePlayer(this);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += RefreshMainCamera;
    }
    void OnDisable()
    {
        if (fireReleaseAction != null)
        {
            fireReleaseAction.canceled -= HandleFireReleased;
            fireReleaseAction = null;
        }
        CancelCharge();
        fireHeld = false;
        this.preview?.Hide();
        if (IsSpawned && IsOwner) networkAimLine.Value = default;
        this.ClearSpectating(false);
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
        // Titleの入力オブジェクトが破棄された後に、通常操作へ戻す。
        if (this.IsTitleMode && FindAnyObjectByType<TitleInputController>() == null)
            this.SetTitleMode(false);

        if (!IsOwner) return;
        this.ClearSpectating(false);
        isAiming = false;
        cameraRig.Rebind(aimPivot != null ? aimPivot : transform);
    }

    /// <summary>通常席はキーボードと指定パッド、他の席は指定パッドだけを使用する。</summary>
    private void PairInputDevice(PlayerInput input)
    {
        var pad = LocalGamepad.Current;
        input.neverAutoSwitchControlSchemes = true;
        string scheme;
        var devices = new List<InputDevice>();

        if (LocalGamepad.Seat == 0 && Keyboard.current != null && Keyboard.current.added)
        {
            scheme = "Keyboard&Mouse";
            devices.Add(Keyboard.current);
            if (Mouse.current != null && Mouse.current.added) devices.Add(Mouse.current);
            if (pad != null && pad.added) devices.Add(pad);
        }
        else if (pad != null && pad.added)
        {
            scheme = "Gamepad";
            devices.Add(pad);
        }
        else
        {
            input.enabled = false;
            Debug.LogWarning($"[Input] seat={LocalGamepad.Seat} の入力デバイスが見つかりません", this);
            return;
        }

        // 前回の有効化に失敗していた場合は、デバイス解放後に初期化し直す。
        if (input.enabled && !input.user.valid)
            input.enabled = false;

        input.defaultControlScheme = scheme;
        input.enabled = true;

        if (!input.user.valid)
        {
            input.enabled = false;
            Debug.LogWarning($"[Input] seat={LocalGamepad.Seat} の入力ユーザーを作成できません。Title側や他のPlayerInputのデバイス割り当てを確認してください。", this);
            return;
        }

        input.SwitchCurrentControlScheme(scheme, devices.ToArray());
    }

    #region ネットワーク経由での脱落・復活・移動制御
    /// <summary>脱落をクライアントに反映させる</summary>
    public void Eliminate()
    {
        if (!IsServer || this.IsEliminated) return;
        this.eliminated.Value = true;
    }

    // サーバーだけが保持する。復帰・次ラウンドの配置で破棄する。
    private PlayerController lastKnockoutAttacker;

    public void ConfirmFallKnockout()
    {
        if (!IsServer || this.IsEliminated) return;
        var attacker = this.lastKnockoutAttacker;
        this.lastKnockoutAttacker = null;
        if (attacker == null || attacker == this || !attacker.IsSpawned) return;
        attacker.ShowKnockoutOwnerRpc(ConnectionContainer.Instance != null
            ? ConnectionContainer.Instance.CurrentRound : 0);
    }

    [Rpc(SendTo.Owner)]
    private void ShowKnockoutOwnerRpc(int round)
    {
        if (!IsOwner || (ConnectionContainer.Instance != null
            && ConnectionContainer.Instance.CurrentRound != round)) return;
        GetComponent<PlayerKOFeedback>()?.Play();
    }

    private void HandleEliminatedChanged(bool previous, bool current)
    {
        this.ApplyEliminatedState(current);
    }

    private void ApplyEliminatedState(bool hidden)
    {
        if (hidden)
        {
            this.isAiming = false;
            this.moveInput = this.lookInput = Vector2.zero;
            this.velocity = this.impact = Vector3.zero;
            this.CancelCharge();
            if (this.preview != null) this.preview.Hide();

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (!this.eliminatedRenderers.ContainsKey(renderer))
                    this.eliminatedRenderers.Add(renderer, renderer.enabled);
                renderer.enabled = false;
            }
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (!this.eliminatedColliders.ContainsKey(collider))
                    this.eliminatedColliders.Add(collider, collider.enabled);
                collider.enabled = false;
            }
        }
        else
        {
            foreach (var entry in this.eliminatedRenderers)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            foreach (var entry in this.eliminatedColliders)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            this.eliminatedRenderers.Clear();
            this.eliminatedColliders.Clear();
        }

        GetComponentInChildren<PlayerAppearance>(true)?.SetEliminated(hidden);
        if (this.controller != null)
            this.controller.enabled = !hidden && !this.controlsStopped && !this.IsTitleMode;
    }

    /// <summary>移動の可否をクライアントに反映させる</summary>
    public void StopOrMovePlayer(bool movable)
    {
        if (!IsServer) return;
        StopOrMovePlayerClientRpc(movable);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void StopOrMovePlayerClientRpc(bool movable)
    {
        // シーン切り替え中はTitleの配置処理がテレポート後に走る場合がある。
        // 操作開始時に所有者側でスポーン方向と視線をもう一度確定する。
        if (movable && IsOwner && this.pendingStartFacing)
        {
            this.SetTitleMode(false);
            transform.rotation = this.teleportRotation;
            this.ResetFacing(this.teleportRotation);
            this.pendingStartFacing = false;
            cameraRig.Rebind(aimPivot != null ? aimPivot : transform);
        }

        controlsStopped = !movable;
        controller.enabled = movable && !isEliminated;

        if (!movable)
        {
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            velocity = Vector3.zero;
            impact = Vector3.zero;
            isAiming = false;

            this.CancelCharge();

            if (preview != null) preview.Hide();
        }
    }

    /// <summary>復活をクライアントに反映させる</summary>
    public void Revive()
    {
        if (!IsServer) return;
        this.lastKnockoutAttacker = null;
        this.eliminated.Value = false;
    }

    /// <summary>テレポートをクライアントに反映させる</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;
        this.lastKnockoutAttacker = null;
        TeleportClientRpc(position, rotation, ConnectionContainer.Instance != null
            ? ConnectionContainer.Instance.CurrentRound : 0);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void TeleportClientRpc(Vector3 position, Quaternion rotation, int placementRound)
    {
        GetComponent<PlayerAnimator>()?.ResetAirborneState();
        // モデルだけのTitle回転はNetworkTransformでは同期されないため全端末で戻す。
        GetComponentInChildren<PlayerAppearance>(true)?.ResetTitleModelRotation();
        if (!IsOwner) return;

        this.teleportRotation = rotation;
        this.pendingStartFacing = true;

        // クライアントごとにシーン読み込みとRPCの到着順が違っても、
        // Title終了処理でスポーン時の照準方向が上書きされないよう先に解除する。
        this.SetTitleMode(false);

        // CharacterControllerが有効なままだと座標の直接代入が打ち消されるので一時的に切る
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        GetComponent<NetworkTransform>().Teleport(position, rotation, transform.localScale);
        Debug.Log($"[RoundSpawn] Applied: ClientId={OwnerClientId}, Position={transform.position}", this);
        controller.enabled = !this.IsEliminated && !this.controlsStopped && !this.IsTitleMode;

        // 照準もスポーン地点の向きに合わせる。
        // これが無いと、体が次のフレームで前ラウンドの aimYaw に向き直ってしまう
        this.ResetFacing(rotation);

        this.LastPlacementRound = placementRound;

        // 速度もリセット
        velocity = Vector3.zero;
        impact = Vector3.zero;

        // 溜めをラウンドをまたいで持ち越さない。
        // クールダウンはスロット側にあり、ClearAllWeapons で一緒に消える
        this.CancelCharge();
    }

    private void ResetFacing(Quaternion rotation)
    {
        aimYaw = rotation.eulerAngles.y;
        aimPitch = 0f;
        isAiming = false;
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        turnSmoothVelocity = 0f;
        this.ApplyAimPivot();
        GetComponentInChildren<PlayerAppearance>(true)?.ResetTitleModelRotation();
    }
    #endregion

    #region コース上のトリガー（セーブポイント・落下）の報告
    // サーバー上では他人のプレイヤーに OnTriggerEnter が来ないことがあるので、本人の端末が判定して報告する

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void ReportSavePointServerRpc(int order, Vector3 position, Quaternion rotation)
    {
        SavePoint.Apply(this, order, position, rotation);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    public void ReportDropOutServerRpc(int respawnLimit, bool respawnAtStart)
    {
        DropOutSensor.Apply(this, respawnLimit, respawnAtStart);
    }
    #endregion

    #region 入力イベント
    void OnJump(InputValue value)
    {
        if (!this.IsControllable)
            return;

        // ボタンが押されたら
        if (value.isPressed)
        {
            // 地面にいればジャンプ
            if (controller != null && controller.enabled && controller.isGrounded
                && velocity.y <= 0f && impact.y <= 0f && HasWalkableGround())
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
        if (!this.IsControllable)
            return;

        moveInput = value.Get<Vector2>();
    }

    void OnAim(InputValue value)
    {
        if (!this.IsControllable)
            return;

        isAiming = value.isPressed;
    }

    void OnLook(InputValue value)
    {
        if (!this.IsControllable)
            return;

        lookInput = value.Get<Vector2>();
    }

    // ラウンドの開始演出・カウントダウン・結果表示中は武器を切り替えない。
    private bool CanSwitchWeapon
    {
        get
        {
            var round = RoundManager.Instance;
            return this.IsControllable && round != null && round.IsSpawned && round.IsReady
                && !round.IsIntro && !round.IsRoundOver
                && round.CountdownRemaining <= RoundManager.StartDisplayDuration;
        }
    }

    void OnNextWeapon(InputValue value)
    {
        if (value.isPressed && this.TrySwitchSpectator(1)) return;

        if (!this.CanSwitchWeapon)
            return;

        if (value.isPressed)
        {
            this.CancelCharge();
            this.playerItem.SwitchWeapon(1);
        }
    }

    void OnPreviousWeapon(InputValue value)
    {
        if (value.isPressed && this.TrySwitchSpectator(-1)) return;

        if (!this.CanSwitchWeapon)
            return;

        if (value.isPressed)
        {
            this.CancelCharge();
            this.playerItem.SwitchWeapon(-1);
        }
    }

    void OnFire(InputValue value)
    {
        // SendMessagesではButton型のcanceledが届かないため、解除だけ直接購読する。
        // 実際に入力された時点の、PlayerInputが所有するActionインスタンスを使う。
        InputAction action = GetComponent<PlayerInput>().actions.FindAction("Fire", false);
        if (fireReleaseAction != action)
        {
            if (fireReleaseAction != null) fireReleaseAction.canceled -= HandleFireReleased;
            fireReleaseAction = action;
            if (fireReleaseAction != null) fireReleaseAction.canceled += HandleFireReleased;
        }

        if (!value.isPressed)
        {
            fireHeld = false;
            CancelCharge();
            return;
        }

        if (fireHeld) return;
        fireHeld = true;
        if (!IsControllable || windupRoutine != null || playerItem == null) return;

        ItemSlot slot = playerItem.CurrentWeapon;
        if (slot == null || !slot.IsReady || slot.currentAmount <= 0
            || slot.itemData is not WeaponData weapon) return;

        if (weapon.attackWindup > 0f)
        {
            windupRoutine = StartCoroutine(FireAfterWindup(slot, weapon));
            PlayChargeStartSE(weapon);
        }
        else
            CompleteAttack(slot, weapon);
    }

    private void HandleFireReleased(InputAction.CallbackContext context)
    {
        fireHeld = false;
        CancelCharge();
    }

    private void CancelCharge()
    {
        if (windupRoutine != null) StopCoroutine(windupRoutine);
        windupRoutine = null;
        ChargeProgress = 0f;
    }

    private void CompleteAttack(ItemSlot slot, WeaponData weapon)
    {
        if (!IsControllable || playerItem.CurrentWeapon != slot || !slot.IsReady
            || !playerItem.TryConsumeAmmo()) return;

        slot.nextFireTime = Time.time + Mathf.Max(0f, weapon.reloadTime);
        BeginMeleeEffect(weapon);
        FireWeapon(weapon);
        playerItem.DiscardIfEmpty();
    }
    #endregion

    /// <summary>壁への接触を接地と誤認せず、足元の歩ける面だけでジャンプを許可する。</summary>
    private bool HasWalkableGround()
    {
        Bounds bounds = controller.bounds;
        float skin = controller.skinWidth * Mathf.Abs(transform.lossyScale.y);
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        if (radius <= 0f) return false;

        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + radius + skin, bounds.center.z);
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, jumpGroundHits,
            skin * 2f + 0.03f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float minGroundNormal = Mathf.Max(0.01f, Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad));
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = jumpGroundHits[i];
            if (hit.collider == controller || hit.collider.transform.IsChildOf(transform)
                || Physics.GetIgnoreLayerCollision(gameObject.layer, hit.collider.gameObject.layer)
                || Physics.GetIgnoreCollision(controller, hit.collider)
                || hit.normal.y < minGroundNormal)
                continue;

            return true;
        }
        return false;
    }

    // 外部のギミックによる速度変化
    public void VelocityChange(Vector3 externalAcceleration)
    {
        if (this.IsTitleMode)
            return;

        velocity += externalAcceleration;
    }

    /// <summary>ジャンプ台などで真上に打ち上げる。今の縦速度より速いときだけ上書きする</summary>
    public void Launch(float upwardSpeed)
    {
        if (!this.IsControllable) return;
        velocity.y = Mathf.Max(velocity.y, upwardSpeed);
    }

    /// <summary>加速台の押し出しを加える。衝撃は移動処理で自然に減衰する。</summary>
    public void Boost(Vector3 boostVelocity)
    {
        if (!this.IsControllable) return;

        impact += boostVelocity;
    }

    void Update()
    {
        if (this.IsTitleMode)
        {
            this.preview?.Hide();
            if (IsSpawned && IsOwner && networkAimLine.Value.visible) networkAimLine.Value = default;
            return;
        }

        this.UpdateAimInput();
        if (IsOwner && !this.IsEliminated)
            cameraRig.UpdateAim(isAiming, aimPitch);

        if (controller != null && controller.enabled && this.IsControllable)
        {
            this.UpdateMovement();
        }

        // 体の回転より後に適用する。親の回転を継承させないため
        this.ApplyAimPivot();
        this.UpdateTrajectoryPreview();

        if (IsSpawned && IsOwner)
        {
            this.networkViewAngles.Value = new Vector2(this.aimYaw, this.aimPitch);
            this.networkAiming.Value = this.isAiming;
        }

        // 操作停止中も更新し、上昇状態が他端末に残らないようにする。
        if (IsSpawned && IsOwner && this.networkRising.Value != this.IsRising)
            this.networkRising.Value = this.IsRising;
    }

    /// <summary>右スティックの入力を照準のヨー・ピッチに積分する</summary>
    private void UpdateAimInput()
    {
        if (!this.IsControllable) return;

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


    /// <summary>操作とは独立して、脱落した本人のカメラだけを生存者へ向ける。</summary>
    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        var round = RoundManager.Instance;
        if (!this.IsEliminated || this.IsTitleMode || round == null || !round.IsSpawned || !round.IsReady)
        {
            this.ClearSpectating(true);
            return;
        }

        if (!this.spectating)
        {
            this.spectatorAnchor = new GameObject("Spectator Camera Target").transform;
            this.spectatorAnchor.gameObject.hideFlags = HideFlags.DontSave;
            this.spectatorAnchor.SetPositionAndRotation(
                this.aimPivot != null ? this.aimPivot.position : transform.position,
                Quaternion.Euler(-this.aimPitch, this.aimYaw, 0f));
            this.spectatorPitch = this.aimPitch;
            this.spectatorAiming = false;
            this.spectating = true;
            this.cameraRig.Rebind(this.spectatorAnchor);
        }

        // 結果表示中は追従を止め、最後に見ていた場所を保持する。
        if (round.IsRoundOver)
        {
            if (this.spectatorHud != null) this.spectatorHud.Hide();
            return;
        }

        bool switched = this.spectatorTargetChanged;
        this.spectatorTargetChanged = false;
        if (!this.CanSpectate(this.spectatorTarget))
        {
            this.spectatorTarget = this.ChooseSpectatorTarget();
            switched = this.spectatorTarget != null;
        }

        this.UpdateSpectatorHud();
        if (this.spectatorTarget == null) return;

        var target = this.spectatorTarget;
        Vector2 angles = target.networkViewAngles.Value;
        Quaternion rotation = Quaternion.Euler(-angles.y, angles.x, 0f);
        this.spectatorAnchor.position = target.aimPivot != null
            ? target.aimPivot.position : target.transform.position;
        this.spectatorAnchor.rotation = switched ? rotation : Quaternion.Slerp(
            this.spectatorAnchor.rotation, rotation, 1f - Mathf.Exp(-15f * Time.deltaTime));
        this.spectatorPitch = angles.y;
        this.spectatorAiming = target.networkAiming.Value;

        if (switched)
        {
            // アンカー自体は再利用するため、対象変更時には明示的にカメラの履歴を切る。
            this.cameraRig.SetFollowTarget(null);
            this.cameraRig.SetFollowTarget(this.spectatorAnchor);
        }

        this.cameraRig.UpdateAim(this.spectatorAiming, this.spectatorPitch);
    }


    private bool CanSpectate(PlayerController candidate)
    {
        return candidate != null && candidate != this && candidate.IsSpawned
            && candidate.isActiveAndEnabled && !candidate.IsEliminated && !candidate.IsTitleMode;
    }

    private bool TrySwitchSpectator(int direction)
    {
        if (SceneTransitionUI.InputBlocked) return true;
        if (!IsSpawned || !IsOwner || !this.IsEliminated) return false;

        var round = RoundManager.Instance;
        if (!this.spectating || this.IsTitleMode || round == null || !round.IsSpawned
            || !round.IsReady || round.IsRoundOver) return true;

        var manager = PlayerManager.Instance;
        if (manager == null) return true;

        var candidates = new List<PlayerController>();
        foreach (var candidate in manager.AllPlayers)
        {
            if (this.CanSpectate(candidate)) candidates.Add(candidate);
        }

        if (candidates.Count == 0) return true;

        // 接続順で固定し、LとRが互いに逆順になるようにする。
        candidates.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        int index = candidates.IndexOf(this.spectatorTarget);
        int next = index < 0 ? (direction > 0 ? 0 : candidates.Count - 1)
            : (index + direction + candidates.Count) % candidates.Count;
        this.spectatorTargetChanged |= this.spectatorTarget != candidates[next];
        this.spectatorTarget = candidates[next];
        return true;
    }

    private void UpdateSpectatorHud()
    {
        if (this.spectatorTarget == null)
        {
            if (this.spectatorHud != null) this.spectatorHud.Hide();
            return;
        }

        if (this.spectatorHud == null)
        {
            if (this.spectatorUIPrefab == null) return;
            this.spectatorHud = Instantiate(this.spectatorUIPrefab);
        }

        var name = ConnectionContainer.Instance != null
            ? ConnectionContainer.Instance.GetName(this.spectatorTarget.OwnerClientId) : null;
        if (string.IsNullOrWhiteSpace(name)) name = $"Player {this.spectatorTarget.OwnerClientId + 1}";
        this.spectatorHud.Show(name);
    }


    private PlayerController ChooseSpectatorTarget()
    {
        var manager = PlayerManager.Instance;
        if (manager == null) return null;

        // Playersの脱落順リストはサーバーで更新されるため、同期済みのIsEliminatedで選ぶ。
        PlayerController selected = null;
        int count = 0;
        foreach (var candidate in manager.AllPlayers)
        {
            if (!this.CanSpectate(candidate)) continue;
            count++;
            if (Random.Range(0, count) == 0) selected = candidate;
        }
        return selected;
    }


    private void ClearSpectating(bool restoreCamera)
    {
        if (!this.spectating) return;

        if (restoreCamera && IsOwner)
            this.cameraRig.Rebind(this.aimPivot != null ? this.aimPivot : transform);

        this.spectating = false;
        this.spectatorTarget = null;
        this.spectatorTargetChanged = false;
        if (this.spectatorHud != null) Destroy(this.spectatorHud.gameObject);
        this.spectatorHud = null;
        if (this.spectatorAnchor != null) Destroy(this.spectatorAnchor.gameObject);
        this.spectatorAnchor = null;
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

        // 通常移動と衝撃をまとめ、最後の Move だけで接地状態が上書きされないようにする。
        if (impact.sqrMagnitude <= 0.04f) impact = Vector3.zero;
        bool isRace = SceneManager.GetSceneByName("Race").isLoaded;
        if (isRace) ResolveRacePenetration();
        bool useGroundFollow = isRace || SceneManager.GetSceneByName("Territory").isLoaded;
        bool followGround = useGroundFollow
            && controller.isGrounded && velocity.y <= 0f && impact.y <= 0f;
        traversingRaceStep = isRace && followGround;
        // ブーストや低FPS時にも、カプセルの半径以内の距離で衝突を検出する。
        float distance = (velocity + impact).magnitude * Time.deltaTime;
        float maxStep = Mathf.Max(0.05f, controller.radius
            * Mathf.Min(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) * 0.5f);
        int steps = isRace ? Mathf.Clamp(Mathf.CeilToInt(distance / maxStep), 1, 128) : 1;
        CollisionFlags collisions = CollisionFlags.None;
        for (int i = 0; i < steps; i++)
            collisions |= controller.Move((velocity + impact) * (Time.deltaTime / steps));
        if (followGround && !controller.isGrounded)
        {
            collisions |= FollowRaceStepDown();
        }
        traversingRaceStep = false;
        if (isRace) ResolveRacePenetration();

        if ((collisions & CollisionFlags.Above) != 0 && velocity.y > 0f)
        {
            velocity.y = 0f;
        }

        impact = Vector3.Lerp(impact, Vector3.zero, 5f * Time.deltaTime);

        // 他人のコピーは Move() を呼ばないので、接地の真値をここから配る
        if (IsSpawned && IsOwner && this.networkGrounded.Value != controller.isGrounded)
        {
            this.networkGrounded.Value = controller.isGrounded;
        }
    }

    /// <summary>動く障害物や復帰地点ですでに重なった場合も、実際の形状に沿って押し戻す。</summary>
    private void ResolveRacePenetration()
    {
        // CharacterController が接地維持に使う skinWidth 分の重なりは許容する。
        float tolerance = Mathf.Max(0.001f, controller.skinWidth * Mathf.Max(
            Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z)));
        Bounds bounds = controller.bounds;
        float maxPush = Mathf.Min(bounds.extents.x, bounds.extents.z);
        int count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * maxPush,
            raceOverlapHits, Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);

        // 重なっている全コライダーを同時に満たす位置を探す。1 つずつ Move すると、
        // 回転物と壁に挟まれたときに押し合いになり、壁の中へ押し込まれてしまう。
        Vector3 push = Vector3.zero;
        float remaining = 0f;
        for (int pass = 0; pass < 6; pass++)
        {
            remaining = 0f;
            for (int i = 0; i < count; i++)
            {
                Collider other = raceOverlapHits[i];
                if (other == controller || other.transform.IsChildOf(transform)
                    || other is CharacterController
                    || Physics.GetIgnoreLayerCollision(gameObject.layer, other.gameObject.layer)
                    || Physics.GetIgnoreCollision(controller, other))
                    continue;

                if (!Physics.ComputePenetration(controller, transform.position + push, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float depth) || depth <= tolerance)
                    continue;

                push += direction * (depth + 0.001f);
                remaining = Mathf.Max(remaining, depth);
            }
            if (remaining <= tolerance) break;
        }

        // 挟まれて横に逃げ場がない場合は、壁を抜けずに上へ押し出す。
        if (remaining > tolerance)
        {
            push.x = 0f;
            push.z = 0f;
            push.y = Mathf.Max(push.y, remaining + 0.001f);
        }
        if (push.sqrMagnitude <= 0f) return;

        // 押し戻し量は半径までに抑え、壁とは CharacterController の衝突で止める。
        controller.Move(Vector3.ClampMagnitude(push, maxPush));
    }

    /// <summary>歩いて下りられる高さだけ足元へ追従し、段差で接地が途切れるのを防ぐ。</summary>
    private CollisionFlags FollowRaceStepDown()
    {
        Bounds bounds = controller.bounds;
        float skin = controller.skinWidth * Mathf.Abs(transform.lossyScale.y);
        float stepHeight = controller.stepOffset * Mathf.Abs(transform.lossyScale.y);
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        if (radius <= 0f || stepHeight <= 0f) return CollisionFlags.None;

        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + radius + skin, bounds.center.z);
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, stepGroundHits,
            stepHeight + skin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        float minGroundNormal = Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = stepGroundHits[i];
            if (hit.collider.transform.IsChildOf(transform)
                || Physics.GetIgnoreLayerCollision(gameObject.layer, hit.collider.gameObject.layer)
                || Physics.GetIgnoreCollision(controller, hit.collider)
                || hit.normal.y < minGroundNormal)
                continue;

            nearest = Mathf.Min(nearest, hit.distance);
        }

        // 足場がない場所や大きな落差では、通常どおり重力で落下する。
        return float.IsPositiveInfinity(nearest)
            ? CollisionFlags.None
            : controller.Move(Vector3.down * Mathf.Min(nearest + skin, stepHeight + skin));
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // 床・段差判定の早期returnより先に、どの方向からの接触でも取得を要求する。
        if (this.IsOwner && this.IsControllable)
        {
            DroppedItem item = hit.collider.GetComponentInParent<DroppedItem>();
            if (item != null)
                item.RequestPickup();
        }

        // 接触面へ向かう成分だけを除去する。壁沿いの移動と斜面の上りは維持する。
        if (hit.normal.y > 0.01f) return;
        // Move 中は段差の側面にも触れる。乗り越え可能な高さでは水平速度を削らない。
        if (traversingRaceStep && hit.normal.y >= -0.01f
            && hit.point.y <= controller.bounds.min.y
                + (controller.stepOffset + controller.skinWidth) * Mathf.Abs(transform.lossyScale.y))
            return;

        float velocityIntoSurface = Vector3.Dot(velocity, hit.normal);
        if (velocityIntoSurface < 0f)
            velocity -= hit.normal * velocityIntoSurface;

        float impactIntoSurface = Vector3.Dot(impact, hit.normal);
        if (impactIntoSurface < 0f)
            impact -= hit.normal * impactIntoSurface;
    }

    /// <summary>押し続けた時間だけ溜め、完了時に初めて弾数を消費する。</summary>
    private IEnumerator FireAfterWindup(ItemSlot slot, WeaponData weapon)
    {
        float elapsed = 0f;
        float duration = weapon.attackWindup;
        ChargeProgress = 0f;
        while (elapsed < duration)
        {
            // 最初に必ずyieldし、StartCoroutineの戻り値による完了状態の上書きを防ぐ。
            yield return null;
            if (!fireHeld || !IsControllable || playerItem.CurrentWeapon != slot
                || slot.itemData != weapon || slot.currentAmount <= 0)
            {
                windupRoutine = null;
                ChargeProgress = 0f;
                yield break;
            }

            elapsed += Time.deltaTime;
            ChargeProgress = Mathf.Clamp01(elapsed / duration);
        }

        windupRoutine = null;
        CompleteAttack(slot, weapon);
    }
    private void FireWeapon(WeaponData weaponData)
    {
        effects.PlayShoot(gameObject.name);

        switch (weaponData.weaponType)
        {
            case WeaponType.Melee:      this.FireMelee(weaponData);      break;
            case WeaponType.Throw:      this.FireThrow(weaponData);      break;
            case WeaponType.Deployable: this.FireDeployable(weaponData); break;
        }
    }

    private void BeginMeleeEffect(WeaponData weapon)
    {
        if (weapon.weaponType != WeaponType.Melee || weapon.meleeEffectPrefab == null)
            return;

        // 本人は通信を待たず再生する。空振りでも一度だけ表示する。
        this.PlayMeleeEffect(weapon);

        if (this.IsSpawned)
            this.RequestMeleeEffectServerRpc(ItemDatabase.Instance.GetKey(weapon));
    }

    private void PlayChargeStartSE(WeaponData weapon)
    {
        if (weapon.chargeStartSE == null) return;

        SEAudio.Instance?.Play(weapon.chargeStartSE, weapon.chargeStartSEVolume);
        if (IsSpawned && ItemDatabase.Instance != null)
            RequestChargeStartSEServerRpc(ItemDatabase.Instance.GetKey(weapon),
                ConnectionContainer.Instance != null ? ConnectionContainer.Instance.CurrentRound : 0);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestChargeStartSEServerRpc(Items weaponKey, int roundNumber)
    {
        var round = RoundManager.Instance;
        if (IsEliminated || IsTitleMode || round == null || !round.IsSpawned || round.IsIntro
            || round.IsRoundOver || round.CountdownRemaining > RoundManager.StartDisplayDuration
            || (ConnectionContainer.Instance != null && ConnectionContainer.Instance.CurrentRound != roundNumber))
            return;

        if (ItemDatabase.Instance.GetItem(weaponKey) is WeaponData weapon
            && weapon.attackWindup > 0f && weapon.chargeStartSE != null)
            PlayChargeStartSEClientRpc(weaponKey, roundNumber);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayChargeStartSEClientRpc(Items weaponKey, int roundNumber)
    {
        // 本人は入力時に再生済み。ホストでも二重に鳴らさない。
        if (IsOwner || (ConnectionContainer.Instance != null
            && ConnectionContainer.Instance.CurrentRound != roundNumber)) return;

        if (ItemDatabase.Instance.GetItem(weaponKey) is WeaponData weapon)
            SEAudio.Instance?.Play(weapon.chargeStartSE, weapon.chargeStartSEVolume);
    }

    private void PlayWeaponUseSE(WeaponData weapon, Items weaponKey)
    {
        // 本人は即時再生し、RPCでは本人を除いて通知する。
        SEAudio.Instance?.Play(weapon.useSE, weapon.useSEVolume);

        if (this.IsSpawned && weapon.useSE != null)
            this.RequestWeaponUseSEServerRpc(weaponKey);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestWeaponUseSEServerRpc(Items weaponKey)
    {
        var round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || round.IsIntro || round.IsRoundOver
            || round.CountdownRemaining > RoundManager.StartDisplayDuration)
            return;

        this.PlayWeaponSEClientRpc(weaponKey, false);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayWeaponSEClientRpc(Items weaponKey, bool hit)
    {
        if (!hit && this.IsOwner) return;

        if (ItemDatabase.Instance.GetItem(weaponKey) is not WeaponData weapon)
            return;

        SEAudio.Instance?.Play(hit ? weapon.hitSE : weapon.useSE,
            hit ? weapon.hitSEVolume : weapon.useSEVolume);
    }

    private void PlayMeleeEffect(WeaponData weapon)
    {
        if (weapon.meleeEffectPrefab == null || this.IsTitleMode
            || (RoundManager.Instance != null && RoundManager.Instance.IsRoundOver))
            return;

        SwordSwingEffect effect = Instantiate(weapon.meleeEffectPrefab, transform);
        effect.transform.localPosition = weapon.meleeEffectOffset;
        effect.transform.localRotation = Quaternion.Euler(weapon.meleeEffectRotation);
        effect.Play(this, 0f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestMeleeEffectServerRpc(Items weaponKey)
    {
        var round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || round.IsIntro || round.IsRoundOver
            || round.CountdownRemaining > RoundManager.StartDisplayDuration)
            return;

        this.PlayMeleeEffectClientRpc(weaponKey);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayMeleeEffectClientRpc(Items weaponKey)
    {
        // 所有者は既に再生済み。ホストが所有者の場合も二重再生しない。
        if (this.IsOwner)
            return;

        if (ItemDatabase.Instance.GetItem(weaponKey) is WeaponData weapon
            && weapon.weaponType == WeaponType.Melee)
        {
            this.PlayMeleeEffect(weapon);
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
        this.PlayWeaponUseSE(weaponData, weaponKey);
        var hits = weaponData.attackRange.GetHitTargets(transform.position, transform.rotation,
                                                        this.attackableLayers,
                                                        this.RangeMultiplier);
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
        this.PlayWeaponUseSE(weaponData, weaponKey);
        Vector3 startPos = ThrowOrigin;
        Vector3 throwDir = GetThrowDirection(weaponData.launchAngle);
        // まっすぐ飛ぶ武器は弾速がそのまま射程になるので、範囲強化をここに乗せる
        float speed = weaponData.throwSpeed * (weaponData.FliesStraight ? this.RangeMultiplier : 1f);
        ThrowProjectileServerRpc(startPos, throwDir * speed, weaponKey);
    }

    private void FireDeployable(WeaponData weaponData)
    {
        // TODO: 設置物のスポーン（次のステップで実装）
        Debug.Log($"{weaponData.itemName} を設置（未実装）");
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void SubmitShootServerRpc(Vector3 hitPoint, ulong targetId, Vector3 direction, Items weaponKey)
    {
        // まずNetworkObjectを一度だけ取得し、プレイヤーとBombの両方を同じ対象から判定する。
        if (targetId == NetworkObjectId
            || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var target))
            return;

        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        float power = (wd != null ? wd.Power : 10f) * this.PowerMultiplier;

        // BombPushBombには、プレイヤー用のノックバック処理ではなく物理的な力を加える。
        if (target.TryGetComponent<BombPushBomb>(out var bomb))
        {
            bomb.ApplyWeaponKnockback(direction, power);
        }
        else if (target.GetComponent<PlayerController>() != null)
        {
            // プレイヤーの場合は既存の疲労・ノックバック処理を使う。
            this.ApplyKnockback(targetId, direction, power);
        }
        else
        {
            // このRPCはプレイヤーとBomb用。ほかの種類のNetworkObjectには何もしない。
            return;
        }

        // Bombへの命中でもプレイヤーへの命中でも、設定されていれば命中音を再生する。
        if (wd != null && wd.hitSE != null)
            this.PlayWeaponSEClientRpc(weaponKey, true);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void BreakBlockServerRpc(ulong blockId, Items weaponKey)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(blockId, out var netObj)) return;

        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        netObj.GetComponent<BreakableBlock>()?.AddDamage((wd != null ? wd.Power : 0f) * this.PowerMultiplier);
    }

    /// <summary>サーバーが最終的なノックバックを決めて対象に適用させる</summary>
    private void ApplyKnockback(ulong targetId, Vector3 direction, float power, bool critical = false)
    {
        if (!IsServer) return;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var netObj)) return;

        var targetStatus = netObj.GetComponent<PlayerStatus>();

        var victim = netObj.GetComponent<PlayerController>();
        if (victim == null || victim == this || victim.IsEliminated) return;
        victim.lastKnockoutAttacker = this;

        // 先に疲労を蓄積し、その値を含めた倍率で吹き飛ばす
        float previousFatigue = targetStatus != null ? targetStatus.Fatigue : 0f;
        float damageMultiplier = critical && ItemDatabase.Instance != null
            ? ItemDatabase.Instance.CriticalDamageMultiplier : 1f;
        targetStatus?.AddFatigue(power * damageMultiplier);
        float damage = targetStatus != null ? targetStatus.Fatigue - previousFatigue : 0f;
        if (damage > 0f)
            ShowDamageOwnerRpc(damage, victim.transform.position,
                ConnectionContainer.Instance != null ? ConnectionContainer.Instance.CurrentRound : 0, critical);
        float multiplier = targetStatus != null ? targetStatus.KnockbackMultiplier : 1f;

        Vector3 force = direction.normalized * power * multiplier;
        force.y += power * 0.3f * multiplier;

        Debug.Log($"[Knockback] power={power}, fatigue={targetStatus?.Fatigue}, multiplier={multiplier:F2}");

        ApplyKnockbackClientRpc(targetId, force);
    }

    [Rpc(SendTo.Owner)]
    private void ShowDamageOwnerRpc(float damage, Vector3 position, int round, bool critical)
    {
        if (!IsOwner || (ConnectionContainer.Instance != null
            && ConnectionContainer.Instance.CurrentRound != round)) return;

        GetComponent<PlayerDamageFeedback>()?.Show(damage, position, round, critical);
    }

    [Rpc(SendTo.ClientsAndHost)]
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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ThrowProjectileServerRpc(Vector3 startPos, Vector3 initialVelocity, Items weaponKey)
    {
        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        float power = (wd?.Power ?? 10f) * this.PowerMultiplier;
        // 弧を描く武器は爆発半径が範囲。まっすぐ飛ぶ武器は弾速側で強化済みなので触らない
        bool straight = wd != null && wd.FliesStraight;
        float blastRadius = (wd?.blastRadius ?? 3f) * (straight ? 1f : this.RangeMultiplier);
        float gravity = wd?.gravityScale ?? 9.8f;

        int projectileId = this.nextProjectileId++;
        float lifetime = trajectoryMaxSteps * TrajectorySimulator.StepDuration;
        ShowProjectileClientRpc(projectileId, startPos, initialVelocity, lifetime, gravity, weaponKey);
        // 発射時のキーを保持し、武器を切り替えても元の弾の効果を使う。
        StartCoroutine(FlyProjectile(projectileId, startPos, initialVelocity, lifetime, gravity, blastRadius, power, weaponKey));
    }

    private void UpdateTrajectoryPreview()
    {
        if (!IsOwner)
        {
            var state = networkAimLine.Value;
            if (!IsSpawned || !state.visible || IsEliminated || controlsStopped || IsTitleMode
                || SceneTransitionUI.InputBlocked || (RoundManager.Instance != null && RoundManager.Instance.IsRoundOver)
                || ItemDatabase.Instance == null)
            {
                preview.Hide();
                return;
            }

            if (ItemDatabase.Instance.GetItem((Items)state.weaponKey) is WeaponData remoteWeapon
                && remoteWeapon.showAimLine && remoteWeapon.weaponType == WeaponType.Throw && remoteWeapon.FliesStraight)
                preview.DrawAimLine(state.start, state.end, remoteWeapon.aimLineColor, remoteWeapon.aimLineWidth, false);
            else
                preview.Hide();
            return;
        }

        var weaponData = this.playerItem != null
            ? this.playerItem.CurrentWeapon?.itemData as WeaponData
            : null;

        bool showLine = isAiming && IsControllable && weaponData != null
            && weaponData.weaponType == WeaponType.Throw && weaponData.FliesStraight && weaponData.showAimLine;
        if (showLine)
        {
            Vector3 start = ThrowOrigin;
            float distance = Mathf.Min(Mathf.Max(0f, weaponData.aimLineMaxDistance),
                Mathf.Max(0f, weaponData.throwSpeed * RangeMultiplier * trajectoryMaxSteps * TrajectorySimulator.StepDuration));
            Vector3 end = start + GetThrowDirection(weaponData.launchAngle) * distance;
            if (TrajectorySimulator.TryGetHit(start, end, TrajectorySettings, out var lineHit)) end = lineHit.point;
            preview.DrawAimLine(start, end, weaponData.aimLineColor, weaponData.aimLineWidth, true);
            if (IsSpawned && ItemDatabase.Instance != null)
                networkAimLine.Value = new AimLineState
                {
                    visible = true, weaponKey = (int)ItemDatabase.Instance.GetKey(weaponData), start = start, end = end
                };
            return;
        }

        if (IsSpawned && networkAimLine.Value.visible) networkAimLine.Value = default;

        if (!isAiming || !IsControllable || weaponData == null)
        {
            preview.Hide();
            return;
        }

        if (weaponData.weaponType == WeaponType.Melee)
        {
            preview.DrawMelee(weaponData.attackRange, transform.position, transform.rotation, this.RangeMultiplier);
            return;
        }

        if (weaponData.weaponType != WeaponType.Throw)
        {
            preview.Hide();
            return;
        }

        // まっすぐ飛ぶ武器は着弾点がレティクルそのものなので、線を引いても情報が増えない
        if (weaponData.FliesStraight)
        {
            preview.DrawCrosshair();
            return;
        }

        float previewSpeed = weaponData.throwSpeed * (weaponData.FliesStraight ? this.RangeMultiplier : 1f);
        Vector3 vel = GetThrowDirection(weaponData.launchAngle) * previewSpeed;
        Vector3 landing = TrajectorySimulator.Simulate(ThrowOrigin, vel, weaponData.gravityScale,
                                                       TrajectorySettings, out _, out bool hit,
                                                       trajectoryPoints);
        preview.Draw(trajectoryPoints, landing, hit);
    }

    [Rpc(SendTo.ClientsAndHost)]
    void ShowProjectileClientRpc(int projectileId, Vector3 startPos, Vector3 initialVelocity, float lifetime, float gravity, Items weaponKey)
    {
        var wd = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        if (wd?.bulletPrefab == null) return;
        StartCoroutine(effects.Projectile(
            projectileId, wd.bulletPrefab, startPos, initialVelocity,
            lifetime, gravity, wd.faceFlightDirection));
    }

    private IEnumerator FlyProjectile(int projectileId, Vector3 start, Vector3 initialVelocity,
        float lifetime, float gravity, float radius, float power, Items weaponKey)
    {
        Vector3 center = start;
        float elapsed = 0f;
        var settings = TrajectorySettings;
        while (elapsed < lifetime)
        {
            yield return null;
            if (!IsSpawned) yield break;
            if (GameManager.Instance.currentState != GameState.Playing)
            {
                FinishProjectileClientRpc(projectileId, center, radius, false);
                yield break;
            }

            // 移動中の相手にも当たるよう、発射時の予測位置ではなく毎フレーム判定する。
            float frameEnd = Mathf.Min(elapsed + Time.deltaTime, lifetime);
            while (elapsed < frameEnd)
            {
                float nextTime = Mathf.Min(elapsed + TrajectorySimulator.StepDuration, frameEnd);
                Vector3 next = TrajectorySimulator.PositionAtTime(start, initialVelocity, gravity, nextTime);
                if (TrajectorySimulator.TryGetHit(center, next, settings, out var hit))
                {
                    this.ExplodeProjectile(projectileId, hit.point, radius, power, weaponKey, collided: true);
                    yield break;
                }
                center = next;
                elapsed = nextTime;
            }
        }
        this.ExplodeProjectile(projectileId, center, radius, power, weaponKey, collided: false);
    }

    private void ExplodeProjectile(int projectileId, Vector3 center, float radius, float power, Items weaponKey, bool collided)
    {
        if (!IsServer) return;

        // 演出は全員に
        FinishProjectileClientRpc(projectileId, center, radius, true);

        var weapon = ItemDatabase.Instance.GetItem(weaponKey) as WeaponData;
        var reductionEffect = weapon != null && weapon.bulletPrefab != null
            ? weapon.bulletPrefab.GetComponent<StatReductionOnHit>() : null;
        bool hitPlayer = false;

        // 判定はサーバーだけが行う
        var hits = Physics.OverlapSphere(center, radius, this.attackableLayers);
        var hitIds = new HashSet<ulong>();
        var targets = new List<NetworkObject>();

        foreach (var col in hits)
        {
            var target = col.GetComponentInParent<NetworkObject>();
            var block = col.GetComponentInParent<BreakableBlock>();
            if (block != null)
            {
                if (target != null && target.NetworkObjectId != NetworkObjectId
                    && hitIds.Add(target.NetworkObjectId)) block.AddDamage(power);
                continue;
            }
            var bomb = col.GetComponentInParent<BombPushBomb>();
            if (bomb != null)
            {
                if (target != null && target.NetworkObjectId != NetworkObjectId
                    && hitIds.Add(target.NetworkObjectId))
                    bomb.ApplyWeaponKnockback(bomb.transform.position - center, power);
                continue;
            }
            if (target != null) targets.Add(target);
        }

        // 顔だけに命中した場合も対象に含める。身体との重複は下のID集合で除外する。
        foreach (var region in CriticalHitRegion.Active)
        {
            if (region.Overlaps(center, radius)
                && (shootableLayers.value & (1 << region.Owner.gameObject.layer)) != 0)
                targets.Add(region.Owner.NetworkObject);
        }

        foreach (var target in targets)
        {
            if (target == null) continue;
            if (target.NetworkObjectId == NetworkObjectId) continue;   // 自分は無視
            if (!hitIds.Add(target.NetworkObjectId)) continue;

            var victim = target.GetComponent<PlayerController>();
            if (victim == null) continue;

            hitPlayer = true;

            if (reductionEffect != null)
                reductionEffect.Apply(target.GetComponent<PlayerStatus>());

            Vector3 direction = (target.transform.position - center).normalized;
            this.ApplyKnockback(target.NetworkObjectId, direction, power, CriticalHitRegion.IsCritical(victim, center));
        }

        // 時間切れでは無音。衝突時は敵への命中、または非命中SE設定に従う。
        // 敵が複数いても、爆発1回につきSEは1回だけ通知する。
        if (collided && weapon != null && weapon.hitSE != null
            && (weapon.playExplosionSEOnMiss || hitPlayer))
        {
            this.PlayWeaponSEClientRpc(weaponKey, true);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    void FinishProjectileClientRpc(int projectileId, Vector3 center, float radius, bool explode)
    {
        effects.FinishProjectile(projectileId, center, explode);
        if (explode) effects.PlayExplosion(center, radius);
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
