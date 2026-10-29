using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum BlockState : byte { Intact, Dropped, Consumed }

/// <summary>
/// 武器で壊せるブロック。壊れると中の強化アイテムが現れ、誰でも拾える。
/// 復活はしない（ラウンド終了＝シーン再読み込みで元に戻る）
/// </summary>
public class BreakableBlock : NetworkBehaviour
{
    [SerializeField] private GameObject blockVisual;   // 壊れる前の見た目
    [SerializeField] private Collider blockCollider;   // 壁としての当たり判定
    // 旧Prefabとの互換用。独立アイテムへ移行したため常に無効にする。
    [SerializeField, HideInInspector] private GameObject dropVisual;
    [SerializeField, HideInInspector] private Collider dropTrigger;

    [SerializeField] private string droppedLayerName = "Default";
    private int intactLayerId;
    private int droppedLayerId;

    [Header("耐久")]
    [SerializeField] private float durability = 15f;

    [Header("アイテムの飛び出し")]
    [SerializeField, Min(0f)] private float dropLaunchSpeed = 3f;
    [Tooltip("真上からの傾き。横方向は生成するたびにランダムになります。")]
    [SerializeField, Range(0f, 80f)] private float dropLaunchAngle = 20f;

    private readonly NetworkVariable<BlockState> state = new (BlockState.Intact);
    private readonly NetworkVariable<float> damage = new ();

    private BlockAnimation blockAnimation;
    private Coroutine hideRoutine;
    private Rigidbody blockBody;

    private void Awake()
    {
        this.intactLayerId  = this.gameObject.layer;   // 配置時のレイヤーを覚える
        this.droppedLayerId = LayerMask.NameToLayer(this.droppedLayerName);
        this.blockAnimation = GetComponentInChildren<BlockAnimation>();
        this.blockBody = this.blockCollider != null
            ? this.blockCollider.attachedRigidbody : GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        this.state.OnValueChanged  += this.OnStateChanged;
        this.damage.OnValueChanged += this.OnDamageChanged;

        this.ApplyVisual(this.state.Value);   // 途中参加でも今の状態から始まる

        // 途中参加でとっくに壊れている場合は、演出を飛ばしてそのまま消す
        if (this.blockVisual != null && this.state.Value != BlockState.Intact)
        {
            this.blockVisual.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        this.state.OnValueChanged  -= this.OnStateChanged;
        this.damage.OnValueChanged -= this.OnDamageChanged;
    }

    /// <summary>サーバーのみ。攻撃が当たったときに呼ぶ。耐久を超えたら壊れる</summary>
    public void AddDamage(float power)
    {
        if (!IsServer) return;
        if (this.state.Value != BlockState.Intact) return;   // 二重破壊を弾く

        this.damage.Value += power;
        if (this.damage.Value < this.durability) return;

        Vector3 dropPosition = this.blockCollider != null
            ? this.blockCollider.bounds.center : transform.position;

        // 先にブロックの当たり判定を消し、生成したアイテムと衝突させない。
        this.state.Value = BlockState.Consumed;

        if (this.TryRollItem(out Items item))
            this.SpawnDroppedItem(item, dropPosition);
    }

    private void SpawnDroppedItem(Items item, Vector3 position)
    {
        if (ItemDatabase.Instance == null)
            return;

        ItemBase data = ItemDatabase.Instance.GetItem(item);
        GameObject prefab = data != null ? data.itemPrefab : null;
        if (prefab == null || !prefab.TryGetComponent<DroppedItem>(out _)
            || !prefab.TryGetComponent<NetworkObject>(out _))
        {
            Debug.LogWarning($"{item}: Item PrefabにDroppedItem付きのネットワークPrefabを設定してください。", this);
            return;
        }

        if (!this.NetworkManager.NetworkConfig.Prefabs.Contains(prefab))
        {
            Debug.LogWarning($"{prefab.name}: Network Prefabsに登録してください。", this);
            return;
        }

        GameObject instance = Instantiate(prefab, position, prefab.transform.rotation);
        instance.GetComponent<DroppedItem>().Initialize(item);
        instance.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);

        // Spawnで物理演算の権限が確定してから、ホスト側で初速を与える。
        float angle = this.dropLaunchAngle * Mathf.Deg2Rad;
        float azimuth = Random.Range(0f, Mathf.PI * 2f);
        Vector3 direction = new Vector3(
            Mathf.Sin(angle) * Mathf.Cos(azimuth),
            Mathf.Cos(angle),
            Mathf.Sin(angle) * Mathf.Sin(azimuth)
        );

        Rigidbody body = instance.GetComponent<Rigidbody>();
        body.linearVelocity = direction * this.dropLaunchSpeed;
    }

    private bool TryRollItem(out Items item)
    {
        item = default;
        var table = RoundManager.Instance != null ? RoundManager.Instance.BlockDropItems : null;
        if (table == null || table.Length == 0) return false;   // 空で JumpBoost を配らない
        item = table[Random.Range(0, table.Length)];
        return true;
    }

    private void OnStateChanged(BlockState previous, BlockState current)
    {
        this.ApplyVisual(current);

        // 壊れた瞬間だけ、見た目が消えるのを破壊アニメの後まで遅らせる
        if (previous == BlockState.Intact && current != BlockState.Intact)
        {
            // 同期された破壊の瞬間だけ再生。途中参加時の状態反映では鳴らさない。
            if (IsClient) SEAudio.Instance?.Play(SEType.BlockBreak);

            this.PlayBreakThenHide();
        }
    }

    private void OnDamageChanged(float previous, float current)
    {
        if (current <= previous) return;                      // 初期同期では鳴らさない
        if (this.state.Value != BlockState.Intact) return;    // 壊れた一撃は破壊アニメに任せる

        if (this.blockAnimation != null) this.blockAnimation.PlayHit();
    }

    /// <summary>今の状態を静的に反映する。壊れる演出はここではなく OnStateChanged が持つ</summary>
    private void ApplyVisual(BlockState s)
    {
        // 破壊後のブロック本体が床を抜けて落ち続けないよう停止する。
        if (s != BlockState.Intact && this.blockBody != null)
            this.blockBody.isKinematic = true;

        if (this.blockCollider != null) this.blockCollider.enabled = (s == BlockState.Intact);
        if (this.dropVisual != null && this.dropVisual != this.gameObject)
            this.dropVisual.SetActive(false);
        if (this.dropTrigger != null) this.dropTrigger.enabled = false;

        // 落ちたアイテムに弾道・照準が反応しないよう、壊れたらレイヤーを戻す
        this.gameObject.layer = (s == BlockState.Intact) ? this.intactLayerId : this.droppedLayerId;

        // 消す側は PlayBreakThenHide に任せる。ここでは戻すときだけ扱う
        if (this.blockVisual != null && s == BlockState.Intact) this.blockVisual.SetActive(true);
    }

    /// <summary>当たり判定はもう切れている。見た目だけアニメの分だけ残す</summary>
    private void PlayBreakThenHide()
    {
        if (this.hideRoutine != null) StopCoroutine(this.hideRoutine);

        if (this.blockAnimation == null)
        {
            if (this.blockVisual != null) this.blockVisual.SetActive(false);
            return;
        }

        this.blockAnimation.PlayBreak();
        this.hideRoutine = StartCoroutine(this.HideAfterBreak(this.blockAnimation.BreakLength));
    }

    private IEnumerator HideAfterBreak(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (this.blockVisual != null) this.blockVisual.SetActive(false);
        this.hideRoutine = null;
    }
}
