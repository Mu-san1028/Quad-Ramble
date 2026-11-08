using UnityEngine;
using UnityEngine.AI;

/// <summary>歩行経路上の低い段差だけを、通常のジャンプ入力で越える。</summary>
[DisallowMultipleComponent]
public sealed class BotJumpController : MonoBehaviour
{
    [SerializeField] private LayerMask groundMask = 129;
    [SerializeField, Min(0.1f)] private float obstacleDistance = 0.65f;
    [SerializeField, Min(0.1f)] private float landingDistance = 1.2f;
    [SerializeField, Min(0.05f)] private float maxStepHeight = 0.65f;
    [SerializeField, Min(0.1f)] private float cooldown = 1.2f;
    [SerializeField, Min(0.1f)] private float maxAirTime = 2f;

    private PlayerController player;
    private PlayerActionInput actions;
    private CharacterController body;
    private BotEdgeAvoidance edgeAvoidance;
    private Vector3 landing;
    private float startedAt;
    private float nextJump;
    private bool leftGround;
    private bool requesting;

    public bool IsJumping { get; private set; }
    public Vector3 TakeoffPosition { get; private set; }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        actions = GetComponent<PlayerActionInput>();
        body = GetComponent<CharacterController>();
        edgeAvoidance = GetComponent<BotEdgeAvoidance>();
        player.Jumped += OnJumped;
    }

    private void OnJumped()
    {
        // 接地・吹き飛ばし等の通常判定で、ジャンプが受理されたときだけ開始。
        if (!requesting) return;
        IsJumping = true;
        leftGround = false;
        startedAt = Time.time;
        TakeoffPosition = transform.position;
        nextJump = Time.time + cooldown;
    }

    public bool TryStart(Vector3 moveInput, float remainingDistance)
    {
        if (!isActiveAndEnabled || IsJumping || !BotBrain.CanAct(player) || player.IsEliminated
            || !player.IsGrounded || Time.time < nextJump || moveInput.sqrMagnitude < 0.001f
            || remainingDistance < landingDistance || body == null || !body.enabled) return false;

        Vector3 direction = moveInput.normalized;
        Bounds bounds = body.bounds;
        Vector3 feet = new(bounds.center.x, bounds.min.y, bounds.center.z);
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        // 足元の段差がないときは跳ばない。Triggerやプレイヤーは対象外。
        if (!Physics.SphereCast(feet + Vector3.up * 0.2f, 0.07f, direction,
            out _, radius + obstacleDistance, groundMask, QueryTriggerInteraction.Ignore)) return false;

        Vector3 ahead = feet + direction * landingDistance;
        if (!Physics.Raycast(ahead + Vector3.up * (maxStepHeight + 0.2f), Vector3.down,
            out var floor, maxStepHeight + 0.3f, groundMask, QueryTriggerInteraction.Ignore)) return false;
        float rise = floor.point.y - feet.y;
        if (rise < -0.05f || rise > Mathf.Min(maxStepHeight, player.BotJumpHeight * 0.7f)
            || Vector3.Angle(floor.normal, Vector3.up) > body.slopeLimit) return false;

        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = 1 };
        if (!NavMesh.SamplePosition(floor.point, out var target, 0.15f, filter)
            || !NavMesh.SamplePosition(transform.position, out var start, 0.6f, filter)
            || NavMesh.Raycast(start.position, target.position, out _, filter)) return false;
        // NavMesh外周そのものを着地点にしない。穴越えはこの機能では行わない。
        if (!NavMesh.FindClosestEdge(target.position, out var edge, filter) || edge.distance < 0.15f) return false;
        if (edgeAvoidance != null && !edgeAvoidance.IsDestinationSafe(target.position)) return false;

        float height = player.BotJumpHeight;
        Vector3 lower = feet + Vector3.up * (radius + 0.05f);
        Vector3 upper = feet + Vector3.up * (bounds.size.y - radius + 0.05f);
        float castRadius = Mathf.Max(0.05f, radius - 0.04f);
        Vector3 across = target.position - feet;
        across.y = 0f;
        // 上昇・頭上の横移動・着地の空間を確認してから通常入力を送る。
        if (Physics.CapsuleCast(lower, upper, castRadius, Vector3.up, height,
                groundMask, QueryTriggerInteraction.Ignore)
            || Physics.CapsuleCast(lower + Vector3.up * height, upper + Vector3.up * height,
                castRadius, across.normalized, across.magnitude, groundMask, QueryTriggerInteraction.Ignore)
            || Physics.CheckCapsule(lower + across + Vector3.up * rise,
                upper + across + Vector3.up * rise, castRadius, groundMask, QueryTriggerInteraction.Ignore)) return false;

        landing = target.position;
        requesting = true;
        actions.RequestJump();
        requesting = false;
        return IsJumping;
    }

    public bool TryGetMoveInput(out Vector3 input)
    {
        input = Vector3.zero;
        if (!IsJumping) return false;
        if (!BotBrain.CanAct(player) || player.IsEliminated || Time.time - startedAt > maxAirTime)
        {
            Cancel();
            return false;
        }
        if (!player.IsGrounded) leftGround = true;
        if (leftGround && player.IsGrounded)
        {
            Cancel();
            return false;
        }
        Vector3 offset = landing - transform.position;
        offset.y = 0f;
        input = offset.normalized * Mathf.Clamp01(offset.magnitude / 0.4f);
        return true;
    }

    public void Cancel()
    {
        IsJumping = false;
        requesting = false;
    }

    private void OnDisable()
    {
        Cancel();
    }

    private void OnDestroy()
    {
        if (player != null) player.Jumped -= OnJumped;
    }
}
