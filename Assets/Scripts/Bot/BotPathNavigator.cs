using UnityEngine;
using UnityEngine.AI;

/// <summary>NavMeshで経路だけを調べる。Transformの補正や自動移動は行わない。</summary>
[DisallowMultipleComponent]
public sealed class BotPathNavigator : MonoBehaviour
{
    [SerializeField] private int agentTypeId;
    [Tooltip("初期値はWalkableのみ。ジャンプ経路は今回使用しない。")]
    [SerializeField] private int areaMask = 1;
    [SerializeField, Min(0.1f)] private float repathInterval = 0.5f;
    [SerializeField, Min(0.05f)] private float destinationMoveThreshold = 0.5f;
    [SerializeField, Min(0.05f)] private float startSampleDistance = 0.6f;
    [SerializeField, Min(0.05f)] private float maxStartHeightDifference = 0.45f;
    [Tooltip("箱の中心など、床から浮いた目的地を歩行面へ投影する距離。")]
    [SerializeField, Min(0.1f)] private float destinationSampleDistance = 2.5f;
    [Tooltip("穴の向こうや隣の島へ目的地が吸着しないための水平制限。")]
    [SerializeField, Min(0.05f)] private float maxDestinationHorizontalOffset = 0.6f;
    [SerializeField, Min(0.05f)] private float cornerReachDistance = 0.15f;
    [SerializeField, Min(0.1f)] private float cornerSlowdownDistance = 1.5f;
    [Tooltip("NavMesh境界そのものを踏まないよう、曲がり角を歩行面の内側へずらす余白。")]
    [SerializeField, Min(0.01f)] private float cornerInset = 0.9f;

    private PlayerController player;
    private BotJumpController jumper;
    private BotEdgeAvoidance edgeAvoidance;
    private NavMeshPath path;
    private NavMeshPath queryPath;
    private Vector3[] corners;
    private Vector3 requestedGoal;
    private bool hasGoal;
    private float nextRepath;
    private int cornerIndex;

    public bool HasPath { get; private set; }
    public float RemainingDistance { get; private set; } = float.PositiveInfinity;
    public NavMeshQueryFilter QueryFilter => new() { agentTypeID = agentTypeId, areaMask = areaMask };
    private NavMeshQueryFilter Filter => QueryFilter;
    private bool CanQuery => isActiveAndEnabled && player != null && player.IsSpawned
        && player.IsServer && player.IsBot;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        jumper = GetComponent<BotJumpController>();
        edgeAvoidance = GetComponent<BotEdgeAvoidance>();
        path = new NavMeshPath();
        queryPath = new NavMeshPath();
    }

    public void ResetPath()
    {
        hasGoal = HasPath = false;
        corners = null;
        cornerIndex = 0;
        nextRepath = 0f;
        RemainingDistance = float.PositiveInfinity;
    }

    public bool TryGetPathLength(Vector3 destination, out float length)
    {
        length = float.PositiveInfinity;
        if (!CanQuery || !TrySampleStart(out Vector3 start)
            || !TryCalculate(start, destination, queryPath, out Vector3[] points)) return false;
        length = PathLength(start, points, points.Length > 1 ? 1 : 0);
        return true;
    }

    public bool TryGetMoveInput(Vector3 destination, float stoppingDistance, float slowdownDistance,
        out Vector3 input)
    {
        input = Vector3.zero;
        if (!CanQuery || !TrySampleStart(out Vector3 start))
        {
            ResetPath();
            return false;
        }

        bool goalMoved = !hasGoal || (destination - requestedGoal).sqrMagnitude
            > destinationMoveThreshold * destinationMoveThreshold;
        bool blocked = HasPath && NavMesh.Raycast(start, corners[cornerIndex], out _, Filter);
        if (goalMoved || Time.time >= nextRepath || blocked)
        {
            requestedGoal = destination;
            hasGoal = true;
            nextRepath = Time.time + Mathf.Max(0.1f, repathInterval);
            HasPath = TryCalculate(start, destination, path, out corners);
            cornerIndex = HasPath && corners.Length > 1 ? 1 : 0;
        }

        if (!HasPath)
        {
            RemainingDistance = float.PositiveInfinity;
            return false;
        }

        while (cornerIndex < corners.Length - 1
            && Vector3.Distance(start, corners[cornerIndex]) <= Mathf.Max(0.05f, cornerReachDistance)
            && !NavMesh.Raycast(start, corners[cornerIndex + 1], out _, Filter))
            cornerIndex++;

        // 現在位置から曲がり角までの直線が歩行面を外れる場合も直進しない。
        if (NavMesh.Raycast(start, corners[cornerIndex], out _, Filter))
        {
            ResetPath();
            return false;
        }

        RemainingDistance = PathLength(start, corners, cornerIndex);
        Vector3 offset = corners[cornerIndex] - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        bool finalCorner = cornerIndex == corners.Length - 1;
        float amount;
        if (finalCorner)
            amount = Mathf.Clamp01((distance - stoppingDistance) / Mathf.Max(0.05f, slowdownDistance));
        else
            // 曲がり角では慣性によるはみ出しを軽減するが、毎回停止はしない。
            amount = Mathf.Clamp(distance / Mathf.Max(0.1f, cornerSlowdownDistance), 0.15f, 1f);
        input = offset.normalized * amount;
        return true;
    }

    private bool TrySampleStart(out Vector3 position)
    {
        position = default;
        if (jumper != null && jumper.IsJumping)
        {
            if (!NavMesh.SamplePosition(jumper.TakeoffPosition, out var takeoff, startSampleDistance, Filter))
                return false;
            position = takeoff.position;
            return true;
        }
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
            Mathf.Max(0.05f, startSampleDistance), Filter)) return false;
        if (Mathf.Abs(hit.position.y - transform.position.y) > maxStartHeightDifference) return false;
        position = hit.position;
        return true;
    }

    private bool TryCalculate(Vector3 start, Vector3 destination, NavMeshPath result, out Vector3[] points)
    {
        points = null;
        if (!NavMesh.SamplePosition(destination, out NavMeshHit goal,
            Mathf.Max(0.1f, destinationSampleDistance), Filter)) return false;
        Vector3 shift = goal.position - destination;
        shift.y = 0f;
        if (shift.magnitude > maxDestinationHorizontalOffset) return false;
        if (edgeAvoidance != null && !edgeAvoidance.IsDestinationSafe(goal.position)) return false;
        if (!NavMesh.CalculatePath(start, goal.position, Filter, result)
            || result.status != NavMeshPathStatus.PathComplete) return false;
        points = result.corners;
        if (points.Length == 0) return false;
        InsetCorners(points);
        // NavMeshLinkによる飛び越しは、歩行入力だけでは実行できないため除外。
        for (int i = 1; i < points.Length; i++)
            if (NavMesh.Raycast(points[i - 1], points[i], out _, Filter)) return false;
        return true;
    }

    private void InsetCorners(Vector3[] points)
    {
        // CalculatePathの角は境界上にある。境界から境界へのRaycastは
        // 浮動小数点誤差で即座に遮断扱いになるため、歩行面内の余白を選ぶ。
        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector3 original = points[i];
            Vector3 best = original;
            float clearance = 0f;
            for (int step = 0; step < 16; step++)
            {
                float angle = step * Mathf.PI / 8f;
                Vector3 probe = original + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * cornerInset;
                if (!NavMesh.SamplePosition(probe, out var candidate, 0.03f, Filter)
                    || Mathf.Abs(candidate.position.y - original.y) > maxStartHeightDifference
                    || !NavMesh.FindClosestEdge(candidate.position, out var edge, Filter)
                    || edge.distance <= clearance) continue;
                best = candidate.position;
                clearance = edge.distance;
            }
            points[i] = best;
        }
    }

    private static float PathLength(Vector3 start, Vector3[] points, int index)
    {
        float distance = Vector3.Distance(start, points[index]);
        for (int i = index + 1; i < points.Length; i++) distance += Vector3.Distance(points[i - 1], points[i]);
        return distance;
    }

    private void OnDisable()
    {
        ResetPath();
    }
}
