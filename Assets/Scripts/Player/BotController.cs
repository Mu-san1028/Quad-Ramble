using UnityEngine;

/// <summary>サーバーで指定地点へ直進する検証用Bot。経路探索・崖回避・攻撃判断は未実装。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerActionInput))]
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

    public void ClearDestination()
    {
        moveTarget = null;
        hasDestination = false;
        destinationStoppingDistance = -1f;
        if (IsServerBot) actions.ResetInput();
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
            actions.ResetInput();
            return;
        }

        if (moveTarget == null && !hasDestination)
        {
            actions.SetWorldMoveInput(Vector3.zero);
            return;
        }

        Vector3 offset = (moveTarget != null ? moveTarget.position : destination) - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        float stop = Mathf.Max(0.05f, destinationStoppingDistance >= 0f
            ? destinationStoppingDistance : stoppingDistance);
        float amount = Mathf.Clamp01((distance - stop) / Mathf.Max(0.05f, slowdownDistance));
        actions.SetWorldMoveInput(distance <= stop ? Vector3.zero : offset.normalized * amount);
    }

    private void OnDisable()
    {
        ClearDestination();
    }
}
