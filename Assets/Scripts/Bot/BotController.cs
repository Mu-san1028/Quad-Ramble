using UnityEngine;

/// <summary>サーバーでNavMeshの経由地点へ歩く。実際の移動は共通入力を使用する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerActionInput), typeof(BotPathNavigator))]
[RequireComponent(typeof(BotJumpController))]
[RequireComponent(typeof(BotEdgeAvoidance))]
[DefaultExecutionOrder(-205)]
public sealed class BotController : MonoBehaviour
{
    [Tooltip("Play中、ホスト側のBotに移動先を割り当てる。未指定なら静止する。")]
    [SerializeField] private Transform moveTarget;
    [Tooltip("移動先との水平距離がこの値以内なら移動入力を止める。")]
    [SerializeField, Min(0.05f)] private float stoppingDistance = 0.5f;
    [Tooltip("停止距離の手前で移動入力を弱める距離。速度自体は通常プレイヤーと共通。")]
    [SerializeField, Min(0.05f)] private float slowdownDistance = 1.5f;

    private PlayerController player;
    private PlayerActionInput actions;
    private BotPathNavigator navigator;
    private BotJumpController jumper;
    private BotEdgeAvoidance edgeAvoidance;
    public float RemainingPathDistance => navigator != null ? navigator.RemainingDistance : float.PositiveInfinity;

    public bool CanReach(Vector3 position, out float pathLength)
    {
        pathLength = float.PositiveInfinity;
        return navigator != null && navigator.TryGetPathLength(position, out pathLength);
    }
    private Vector3 destination;
    private bool hasDestination;
    private int placementRound = -1;
    private bool wasSpawned;
    private float destinationStoppingDistance = -1f;

    private bool IsServerBot => player != null && player.IsSpawned && player.IsServer && player.IsBot;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        actions = GetComponent<PlayerActionInput>();
        navigator = GetComponent<BotPathNavigator>();
        jumper = GetComponent<BotJumpController>();
        edgeAvoidance = GetComponent<BotEdgeAvoidance>();
    }

    public void SetMoveTarget(Transform target)
    {
        if (!IsServerBot) return;
        placementRound = player.LastPlacementRound;
        moveTarget = target;
        hasDestination = false;
        destinationStoppingDistance = -1f;
    }

    public void SetDestination(Vector3 worldPosition, float stopDistance = -1f)
    {
        if (!IsServerBot) return;
        placementRound = player.LastPlacementRound;
        moveTarget = null;
        destination = worldPosition;
        hasDestination = true;
        destinationStoppingDistance = stopDistance;
    }

    public void ClearDestination(bool resetActions = true)
    {
        // 攻撃距離に入っても、開始済みのジャンプは着地まで完了させる。
        if (!resetActions && jumper != null && jumper.IsJumping) return;
        if (jumper != null) jumper.Cancel();
        if (navigator != null) navigator.ResetPath();
        moveTarget = null;
        hasDestination = false;
        destinationStoppingDistance = -1f;
        if (IsServerBot)
        {
            if (resetActions)
            {
                actions.ResetInput();
                // 敵が見つからないときも、縁にいるなら退避を止めない。
                if (isActiveAndEnabled) ApplyMoveInput(Vector3.zero);
            }
            else ApplyMoveInput(Vector3.zero);
        }
    }

    private void Update()
    {
        if (!IsServerBot)
        {
            if (wasSpawned) ClearDestination();
            wasSpawned = false;
            return;
        }

        wasSpawned = true;
        if (placementRound != player.LastPlacementRound)
        {
            // 永続プレイヤーなので、前ラウンドの座標を追い続けないようにする。
            placementRound = player.LastPlacementRound;
            ClearDestination();
        }

        if (player.IsEliminated)
        {
            ClearDestination();
            return;
        }

        if (!player.isActiveAndEnabled || !player.IsControllable || !actions.isActiveAndEnabled)
        {
            jumper.Cancel();
            actions.ResetInput();
            return;
        }

        if (moveTarget == null && !hasDestination)
        {
            ApplyMoveInput(Vector3.zero);
            return;
        }

        if (jumper.TryGetMoveInput(out Vector3 jumpInput))
        {
            ApplyMoveInput(jumpInput);
            return;
        }

        float stop = Mathf.Max(0.05f, destinationStoppingDistance >= 0f
            ? destinationStoppingDistance : stoppingDistance);
        Vector3 goal = moveTarget != null ? moveTarget.position : destination;
        if (navigator.TryGetMoveInput(goal, stop, slowdownDistance, out Vector3 input))
        {
            input = edgeAvoidance.FilterInput(input);
            jumper.TryStart(input, navigator.RemainingDistance);
            actions.SetWorldMoveInput(input);
        }
        else
            ApplyMoveInput(Vector3.zero);
    }

    private void ApplyMoveInput(Vector3 input)
    {
        actions.SetWorldMoveInput(edgeAvoidance != null ? edgeAvoidance.FilterInput(input) : input);
    }

    private void OnDisable()
    {
        ClearDestination();
    }
}
