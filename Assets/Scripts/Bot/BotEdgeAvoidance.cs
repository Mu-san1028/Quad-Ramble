using UnityEngine;
using UnityEngine.AI;

/// <summary>歩行入力に余白を設け、NavMeshの縁から内側への移動を優先する。</summary>
[DisallowMultipleComponent]
public sealed class BotEdgeAvoidance : MonoBehaviour
{
    [Tooltip("ベイク時のAgent Radiusに加えて確保するNavMesh境界からの余白。")]
    [SerializeField, Min(0.05f)] private float edgeClearance = 0.6f;
    [SerializeField, Min(0.1f)] private float lookAheadDistance = 0.9f;
    [SerializeField, Min(0f)] private float predictionTime = 0.2f;
    [SerializeField, Min(0.1f)] private float recoveryDistance = 1.2f;
    [SerializeField, Range(0.1f, 1f)] private float recoveryInput = 0.65f;
    [SerializeField] private LayerMask groundMask = 129;
    [Tooltip("接地面より低い穴の底や、別の階へ吸着しないための高さ制限。")]
    [SerializeField, Min(0.05f)] private float maxGroundHeightDifference = 0.45f;

    private PlayerController player;
    private BotPathNavigator navigator;
    private BotJumpController jumper;
    private CharacterController body;

    public float EdgeClearance => edgeClearance;
    private NavMeshQueryFilter Filter => navigator != null ? navigator.QueryFilter
        : new NavMeshQueryFilter { agentTypeID = 0, areaMask = 1 };

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        navigator = GetComponent<BotPathNavigator>();
        jumper = GetComponent<BotJumpController>();
        body = GetComponent<CharacterController>();
    }

    public bool IsDestinationSafe(Vector3 position)
    {
        return !isActiveAndEnabled || (NavMesh.FindClosestEdge(position, out var edge, Filter)
            && edge.distance >= edgeClearance);
    }

    public Vector3 FilterInput(Vector3 input)
    {
        if (!isActiveAndEnabled) return input;
        if (!BotBrain.CanAct(player) || player.IsEliminated) return Vector3.zero;
        // 空中の物理挙動は変えず、ジャンプの着地点を事前に制限する。
        if (!player.IsGrounded || (jumper != null && jumper.IsJumping)) return input;

        float speed = body != null ? new Vector2(body.velocity.x, body.velocity.z).magnitude : 0f;
        float distance = Mathf.Max(lookAheadDistance, speed * predictionTime);
        Vector3 feet = body != null ? new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z)
            : transform.position;
        Vector3 safeInput = ChooseInput(feet, input, distance);
        if (safeInput.sqrMagnitude < 0.001f) return Vector3.zero;
        // NavMesh外へ押し出された後も、床が連続している範囲だけ歩いて戻す。
        float probeDistance = Mathf.Min(distance, recoveryDistance);
        return HasGroundAlong(feet, feet + safeInput.normalized * probeDistance) ? safeInput : Vector3.zero;
    }

    private Vector3 ChooseInput(Vector3 position, Vector3 input, float distance)
    {
        input.y = 0f;
        input = Vector3.ClampMagnitude(input, 1f);
        if (!NavMesh.SamplePosition(position, out var start, 0.65f, Filter)
            || Mathf.Abs(start.position.y - position.y) > maxGroundHeightDifference
            || !NavMesh.FindClosestEdge(start.position, out var edge, Filter)) return Vector3.zero;

        bool recovering = edge.distance < edgeClearance
            || HorizontalDistance(position, start.position) > 0.1f;
        if (!recovering && input.sqrMagnitude < 0.001f) return Vector3.zero;
        distance = Mathf.Min(distance, recoveryDistance);
        float minimumClearance = recovering ? Mathf.Max(0f, edge.distance - 0.01f) : edgeClearance;

        if (!recovering && IsSegmentSafe(start.position, position + input.normalized * distance,
            minimumClearance, out _)) return input;

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        // 元の進行方向を含めて評価。縁に寄った場合は余白の回復を最優先。
        for (int i = 0; i < 24; i++)
        {
            float angle = i * Mathf.PI / 12f;
            Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (!IsSegmentSafe(start.position, start.position + direction * distance,
                minimumClearance, out float clearance)) continue;
            if (recovering && clearance < edge.distance + 0.1f) continue;
            float alignment = input.sqrMagnitude > 0.001f ? Vector3.Dot(input.normalized, direction) : 0f;
            // 安全な位置では大きな逆走をせず、経路の再探索に任せる。
            if (!recovering && alignment < 0.1f) continue;
            float score = recovering ? Mathf.Min(clearance, edgeClearance + 0.3f) * 4f + alignment * 0.2f
                : alignment + Mathf.Min(clearance, edgeClearance + 0.3f) * 0.1f;
            if (score <= bestScore) continue;
            bestScore = score;
            best = direction;
        }

        return best * (recovering ? recoveryInput : input.magnitude * 0.7f);
    }

    private bool IsSegmentSafe(Vector3 start, Vector3 end, float minimumClearance, out float clearance)
    {
        clearance = 0f;
        if (!NavMesh.SamplePosition(end, out var goal, 0.12f, Filter)
            || HorizontalDistance(end, goal.position) > 0.1f
            || NavMesh.Raycast(start, goal.position, out _, Filter)) return false;

        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, goal.position) / 0.2f));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 probe = Vector3.Lerp(start, goal.position, (float)i / steps);
            if (!NavMesh.SamplePosition(probe, out var sample, 0.15f, Filter)
                || !NavMesh.FindClosestEdge(sample.position, out var edge, Filter)
                || edge.distance < minimumClearance) return false;
            clearance = edge.distance;
        }
        return true;
    }

    private bool HasGroundAlong(Vector3 start, Vector3 end)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / 0.2f));
        float previousHeight = start.y;
        for (int i = 0; i <= steps; i++)
        {
            Vector3 position = Vector3.Lerp(start, end, (float)i / steps);
            position.y = previousHeight + maxGroundHeightDifference + 0.1f;
            if (!Physics.Raycast(position, Vector3.down, out var hit,
                maxGroundHeightDifference * 2f + 0.2f, groundMask, QueryTriggerInteraction.Ignore)
                || Mathf.Abs(hit.point.y - previousHeight) > maxGroundHeightDifference
                || Vector3.Angle(hit.normal, Vector3.up) > (body != null ? body.slopeLimit : 45f)) return false;
            previousHeight = hit.point.y;
        }
        return true;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
