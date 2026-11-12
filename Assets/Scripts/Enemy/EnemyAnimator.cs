using UnityEngine;

/// <summary>
/// PlayerAnimatorを参考に、移動速度からAnimatorの歩行・走行値を滑らかに更新します。
/// Animator ControllerではHor/Vert/State/IsJumpを使用できます。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAnimator : MonoBehaviour
{
    private static readonly int HorId = Animator.StringToHash("Hor");
    private static readonly int VertId = Animator.StringToHash("Vert");
    private static readonly int StateId = Animator.StringToHash("State");
    private static readonly int IsJumpId = Animator.StringToHash("IsJump");
    private static readonly int AttackId = Animator.StringToHash("Attack");
    private static readonly int DeathId = Animator.StringToHash("Death");

    [SerializeField] private Animator animator;
    [SerializeField, Min(0.1f)] private float walkSpeed = 2f;
    [SerializeField, Min(0.1f)] private float runSpeed = 5f;
    [SerializeField, Min(0f)] private float smoothing = 12f;

    private Vector3 previousPosition;
    private Vector2 direction;
    private float state;
    private bool isDead;

    /// <summary>敵の攻撃が命中したとき、Animator ControllerのAttack Triggerを起動します。</summary>
    public void PlayAttack()
    {
        if (animator != null && !isDead) animator.SetTrigger(AttackId);
    }

    /// <summary>死亡時のTriggerを起動し、その後の移動アニメーション更新を止めます。</summary>
    public void PlayDeath()
    {
        isDead = true;
        if (animator != null) animator.SetTrigger(DeathId);
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        previousPosition = transform.position;
    }

    private void OnEnable()
    {
        // 有効化直後の位置差分を移動速度として誤認しないよう基準位置を更新します。
        previousPosition = transform.position;
        direction = Vector2.zero;
        state = 0f;
    }

    private void Update()
    {
        if (animator == null || isDead || Time.deltaTime <= 0f) return;

        Vector3 delta = transform.position - previousPosition;
        previousPosition = transform.position;
        delta.y = 0f;

        Vector2 targetDirection = Vector2.zero;
        float targetState = 0f;
        float speed = delta.magnitude / Time.deltaTime;
        if (speed > 0.01f)
        {
            Vector3 local = transform.InverseTransformDirection(delta / Time.deltaTime);
            targetDirection = Vector2.ClampMagnitude(
                new Vector2(local.x, local.z) / Mathf.Max(0.01f, runSpeed), 1f);
            targetState = Mathf.InverseLerp(walkSpeed, runSpeed, speed);
        }

        // 指数平滑化により、フレームレートが変わっても自然な遷移になります。
        float blend = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        direction = Vector2.Lerp(direction, targetDirection, blend);
        state = Mathf.Lerp(state, targetState, blend);

        animator.SetFloat(HorId, direction.x);
        animator.SetFloat(VertId, direction.y);
        animator.SetFloat(StateId, state);
        animator.SetBool(IsJumpId, false); // NavMeshAgent移動ではジャンプを扱わない初期実装です。
    }
}
