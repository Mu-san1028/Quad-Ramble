using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>ブロックから独立して落下し、接触したプレイヤーが取得するアイテム。</summary>
[RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform), typeof(NetworkRigidbody))]
public class DroppedItem : NetworkBehaviour
{
    [SerializeField, Min(0.05f)] private float requestInterval = 0.25f;
    [SerializeField, Min(0f)] private float pickupTolerance = 0.75f;

    private Items item;
    private bool initialized;
    private bool consumed;
    private float nextRequestTime;
    private Collider[] pickupColliders;

    private void Awake()
    {
        this.pickupColliders = GetComponentsInChildren<Collider>();
    }

    /// <summary>ホストが生成後、Spawn前に種類を渡す。見た目はPrefab自体が持つ。</summary>
    public void Initialize(Items itemType)
    {
        if (this.IsSpawned)
            return;

        this.item = itemType;
        this.initialized = true;
    }

    public void RequestPickup()
    {
        if (!this.IsSpawned || Time.unscaledTime < this.nextRequestTime)
            return;

        this.nextRequestTime = Time.unscaledTime + this.requestInterval;
        this.RequestPickupServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestPickupServerRpc(RpcParams rpcParams = default)
    {
        if (!this.IsSpawned || !this.initialized || this.consumed)
            return;

        var round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || round.IsIntro || round.IsRoundOver
            || round.CountdownRemaining > RoundManager.StartDisplayDuration)
            return;

        // 要求側が指定したプレイヤーではなく、実際の送信者を使う。
        if (!this.NetworkManager.ConnectedClients.TryGetValue(
            rpcParams.Receive.SenderClientId, out var client) || client.PlayerObject == null)
            return;

        var player = client.PlayerObject.GetComponent<PlayerController>();
        var inventory = client.PlayerObject.GetComponent<PlayerItem>();
        var controller = client.PlayerObject.GetComponent<CharacterController>();
        if (player == null || inventory == null || controller == null || !controller.enabled
            || player.IsTitleMode || PlayerManager.Instance == null
            || !PlayerManager.Instance.Players.Contains(player) || !this.IsNear(controller))
            return;

        // サーバーで一度だけ適用する。満杯ならアイテムを残す。
        this.consumed = true;
        if (!inventory.TryApplyItemServer(this.item))
        {
            this.consumed = false;
            return;
        }

        this.NetworkObject.Despawn();
    }

    private bool IsNear(CharacterController controller)
    {
        Bounds playerBounds = controller.bounds;
        foreach (Collider itemCollider in this.pickupColliders)
        {
            if (itemCollider == null || !itemCollider.enabled || itemCollider.isTrigger
                || !itemCollider.gameObject.activeInHierarchy)
                continue;

            Vector3 nearest = itemCollider.ClosestPoint(playerBounds.center);
            if (playerBounds.SqrDistance(nearest) <= this.pickupTolerance * this.pickupTolerance)
                return true;
        }

        return false;
    }
}
