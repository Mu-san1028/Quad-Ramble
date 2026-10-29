using UnityEngine;

/// <summary>
/// 移動アニメーションの駆動を担当する。
///
/// Character_Movement.controller の仕様:
///   State  0〜1  歩き ↔ 走り（最上位の 1D ツリー）
///   Hor    -1〜1 体のローカル座標での左右（2D ツリーの X）
///   Vert   -1〜1 体のローカル座標での前後（2D ツリーの Y）
///   IsJump bool  空中なら true
/// </summary>
public class PlayerAnimator : MonoBehaviour
{
    private static readonly int HorId = Animator.StringToHash("Hor");
    private static readonly int VertId = Animator.StringToHash("Vert");
    private static readonly int StateId = Animator.StringToHash("State");
    private static readonly int IsJumpId = Animator.StringToHash("IsJump");

    [Header("速度の基準")]
    [SerializeField] private float walkSpeed = 2f;   // これ以下は歩き（State 0）
    [SerializeField] private float runSpeed = 5f;    // これ以上は走り（State 1）

    [Header("なまし")]
    [SerializeField] private float smoothing = 12f;
    [SerializeField] private float teleportThreshold = 1f;   // 1フレームでこれ以上動いたら瞬間移動とみなす

    [Header("空中モーション")]
    [SerializeField, Min(0f)] private float airborneDelay = 0.1f;

    private float airborneTime;
    private bool airborneAnimation;

    private Animator animator;
    private CharacterController controller;
    private PlayerController player;
    private Vector3 previousPosition;
    private Vector2 direction;   // なまし後の Hor / Vert
    private float state;         // なまし後の State

    private void Awake()
    {
        this.controller = GetComponent<CharacterController>();
        this.player = GetComponent<PlayerController>();
        this.previousPosition = transform.position;
    }

    private void OnEnable()
    {
        this.ResetAirborneState();
        // 再有効化やテレポート直後に巨大な差分が出ないよう basis を取り直す
        this.previousPosition = transform.position;
    }

    /// <summary>モデルを差し替えたときに PlayerAppearance から呼ばれる</summary>
    public void SetAnimator(Animator animator)
    {
        this.animator = animator;
        this.ResetAirborneState();
        this.previousPosition = transform.position;
        this.direction = Vector2.zero;
        this.state = 0f;

        if (this.animator != null && this.player != null && this.player.IsTitleMode)
        {
            this.ApplyTitleIdle();
        }
    }

    private void Update()
    {
        if (this.animator == null) return;

        if (this.player != null && this.player.IsTitleMode)
        {
            this.ApplyTitleIdle();
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - this.previousPosition;
        this.previousPosition = transform.position;

        // 上下だけのテレポートも、空中判定の持ち越しを防ぐ。
        bool teleported = delta.magnitude >= this.teleportThreshold;
        if (teleported)
            this.ResetAirborneState();

        // 上下はジャンプ側が扱う。混ぜると前後アニメが暴れる
        delta.y = 0f;

        Vector2 targetDirection = Vector2.zero;
        float targetState = 0f;

        // ラウンド開始時のテレポートで一瞬だけ全力疾走にならないよう弾く
        if (!teleported)
        {
            Vector3 velocity = delta / dt;
            float speed = velocity.magnitude;

            if (speed > 0.01f)
            {
                // 体のローカル座標に直す。alwaysFaceCamera のとき、これが無いと横歩きが再生されない
                Vector3 local = transform.InverseTransformDirection(velocity);
                targetDirection = Vector2.ClampMagnitude(new Vector2(local.x, local.z) / Mathf.Max(0.01f, this.runSpeed), 1f);
                targetState = Mathf.InverseLerp(this.walkSpeed, this.runSpeed, speed);
            }
        }

        // フレームレートに依存しないなまし
        float k = 1f - Mathf.Exp(-this.smoothing * dt);
        this.direction = Vector2.Lerp(this.direction, targetDirection, k);
        this.state = Mathf.Lerp(this.state, targetState, k);

        this.animator.SetFloat(HorId, this.direction.x);
        this.animator.SetFloat(VertId, this.direction.y);
        this.animator.SetFloat(StateId, this.state);
        // 他人のコピーでは CharacterController.isGrounded が更新されないので、同期された値を使う
        bool grounded = this.player != null
            ? this.player.IsGrounded
            : (this.controller != null && this.controller.isGrounded);

        this.UpdateJumpAnimation(grounded, this.player != null && this.player.IsRising, dt);
    }

    private void UpdateJumpAnimation(bool grounded, bool rising, float dt)
    {
        if (rising)
        {
            this.airborneTime = 0f;
            this.airborneAnimation = true;
        }
        else if (grounded)
        {
            this.airborneTime = 0f;
            this.airborneAnimation = false;
        }
        else
        {
            this.airborneTime += dt;
            // 一度空中になったら着地まで維持し、ジャンプ頂点で地上へ戻さない。
            if (this.airborneTime >= this.airborneDelay)
                this.airborneAnimation = true;
        }

        this.animator.SetBool(IsJumpId, this.airborneAnimation);
    }

    public void ResetAirborneState()
    {
        this.airborneTime = 0f;
        this.airborneAnimation = false;

        if (this.animator != null)
            this.animator.SetBool(IsJumpId, false);
    }

    /// <summary>Title中は移動量や接地判定によらず、地上の待機モーションにする。</summary>
    private void ApplyTitleIdle()
    {
        this.ResetAirborneState();
        // 台への配置を移動とみなさず、通常操作へ戻るときも古い位置を持ち越さない。
        this.previousPosition = transform.position;
        this.direction = Vector2.zero;
        this.state = 0f;

        this.animator.SetFloat(HorId, 0f);
        this.animator.SetFloat(VertId, 0f);
        this.animator.SetFloat(StateId, 0f);
        this.animator.SetBool(IsJumpId, false);
    }
}
