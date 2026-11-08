using System.Collections.Generic;
using UnityEngine;

/// <summary>取得可能な最寄りの箱を選ぶ。取得は通常の接触処理に任せる。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BotController), typeof(PlayerItem))]
[DefaultExecutionOrder(-207)]
public sealed class BotItemCollector : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float searchInterval = 0.5f;
    [Tooltip("箱の中心へ十分近づき、取得用Colliderに触れるための停止距離。")]
    [SerializeField, Min(0.05f)] private float pickupStoppingDistance = 0.05f;
    [Tooltip("この秒数、目的地に近づけなければ別の箱を探す。")]
    [SerializeField, Min(0.5f)] private float stuckTimeout = 4f;
    [SerializeField, Min(0.1f)] private float retryDelay = 8f;
    [SerializeField, Min(0.01f)] private float minimumProgress = 0.2f;

    private PlayerController player;
    private PlayerItem inventory;
    private BotController movement;
    private ItemBox target;
    private readonly Dictionary<ItemBox, float> retryAfter = new();
    private float nextSearch;
    private float bestDistance;
    private float lastProgress;
    private int placementRound = -1;
    private bool ownsDestination;
    private bool collecting = true;

    public ItemBox Target => target;

    public void SetCollecting(bool value)
    {
        if (collecting == value) return;
        collecting = value;
        if (!value) ReleaseTarget();
        else nextSearch = 0f;
    }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        inventory = GetComponent<PlayerItem>();
        movement = GetComponent<BotController>();
    }

    private void Update()
    {
        if (!collecting) return;
        if (!player.IsSpawned || !player.IsServer || !player.IsBot)
        {
            ResetSearch();
            return;
        }

        if (placementRound != player.LastPlacementRound)
        {
            ResetSearch();
            placementRound = player.LastPlacementRound;
        }

        var round = RoundManager.Instance;
        if (!player.isActiveAndEnabled || !player.IsControllable || !movement.isActiveAndEnabled
            || round == null || !round.IsSpawned || !round.IsReady || round.IsIntro
            || round.IsRoundOver || round.CountdownRemaining > RoundManager.StartDisplayDuration)
        {
            ReleaseTarget();
            return;
        }

        if (!inventory.HasEmptyWeaponSlot)
        {
            ReleaseTarget();
            return;
        }

        if (target != null && !target.CanBeCollected) ReleaseTarget();

        if (target != null)
        {
            // 穴の迂回中は直線距離が増えるため、経路の残距離で進捗を見る。
            float distance = movement.RemainingPathDistance;
            if (distance <= bestDistance - Mathf.Max(0.01f, minimumProgress))
            {
                bestDistance = distance;
                lastProgress = Time.time;
            }
            else if (Time.time - lastProgress >= Mathf.Max(0.5f, stuckTimeout))
            {
                retryAfter[target] = Time.time + Mathf.Max(0.1f, retryDelay);
                ReleaseTarget();
            }
        }

        if (target == null)
        {
            // Destroyされた箱への移動も残さない。
            if (ownsDestination) ReleaseTarget();
            if (Time.time < nextSearch) return;
            nextSearch = Time.time + Mathf.Max(0.1f, searchInterval);
            FindNearestBox();
        }

        if (target != null)
        {
            movement.SetDestination(target.PickupPosition, Mathf.Max(0.05f, pickupStoppingDistance));
            ownsDestination = true;
        }
    }

    private void FindNearestBox()
    {
        float nearest = float.PositiveInfinity;
        foreach (var box in FindObjectsByType<ItemBox>(FindObjectsSortMode.None))
        {
            if (!box.CanBeCollected || (retryAfter.TryGetValue(box, out float until) && Time.time < until))
                continue;

            if (!movement.CanReach(box.PickupPosition, out float distance)) continue;
            if (distance >= nearest) continue;
            target = box;
            nearest = distance;
        }

        if (target == null) return;
        bestDistance = nearest;
        lastProgress = Time.time;
    }

    private float HorizontalDistance(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void ReleaseTarget()
    {
        target = null;
        if (ownsDestination && movement != null) movement.ClearDestination();
        ownsDestination = false;
    }

    private void ResetSearch()
    {
        ReleaseTarget();
        retryAfter.Clear();
        nextSearch = 0f;
    }

    private void OnDisable()
    {
        ResetSearch();
    }
}
