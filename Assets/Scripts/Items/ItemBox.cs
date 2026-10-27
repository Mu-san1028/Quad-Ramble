using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// アイテムの出現ポイント。取られたら一定時間後に復活する
/// </summary>
[DisallowMultipleComponent]
public class ItemBox : NetworkBehaviour
{
    [SerializeField] private float respawnTime = 10f;

    // サーバーが書き、全員が読む。今取れる状態かどうか
    private readonly NetworkVariable<bool> isAvailable = new (true);
    private Collider triggerCollider;
    private ItemBoxAnimation boxAnimator;

    protected virtual void Awake()
    {
        this.triggerCollider = GetComponent<Collider>();
        this.boxAnimator = GetComponentInChildren<ItemBoxAnimation>();
    }

    public override void OnNetworkSpawn()
    {
        this.isAvailable.OnValueChanged += this.OnAvailabilityChanged;
        this.ApplyVisual(this.isAvailable.Value); // 途中参加でも正しい状態から始まる
    }

    public override void OnNetworkDespawn()
    {
        this.isAvailable.OnValueChanged -= this.OnAvailabilityChanged;
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!this.isAvailable.Value) return;
        if (!other.TryGetComponent(out PlayerItem playerItem)) return;

        if (!this.TryRollItem(out Items item)) return;
        if (!playerItem.TryApplyItemServer(item)) return;

        this.isAvailable.Value = false;
        StartCoroutine(this.RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(this.respawnTime);
        this.isAvailable.Value = true;
    }

    protected virtual Items[] GetItemTable(RoundManager round) => round.AvailableItems;

    private bool TryRollItem(out Items item)
    {
        item = default;
        var round = RoundManager.Instance;
        var table = round != null ? this.GetItemTable(round) : null;
        if (table == null || table.Length == 0)
        {
            Debug.LogWarning($"{this.GetType().Name}: RoundManagerに抽選アイテムが設定されていません。", this);
            return false;
        }

        item = table[Random.Range(0, table.Length)];
        return true;
    }

    private void OnAvailabilityChanged(bool previous, bool current) => this.ApplyVisual(current);

    private void ApplyVisual(bool available)
    {
        if (this.triggerCollider != null) this.triggerCollider.enabled = available;

        if (this.boxAnimator == null) return;
        if (available) this.boxAnimator.PlayRespawnThenIdle();
        else           this.boxAnimator.PlayHit();
    }
}
