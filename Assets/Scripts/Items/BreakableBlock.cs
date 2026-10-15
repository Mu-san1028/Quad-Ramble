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
    [SerializeField] private GameObject dropVisual;    // 落ちたアイテムのモデルを差し込む場所
    [SerializeField] private Collider dropTrigger;     // 拾うためのトリガー

    [SerializeField] private string droppedLayerName = "Default";
    private int intactLayerId;
    private int droppedLayerId;

    [Header("耐久")]
    [SerializeField] private float durability = 15f;

    // 適用順は宣言順。droppedItem → state → damage の順にクライアントへ届く
    private readonly NetworkVariable<Items> droppedItem = new ();
    private readonly NetworkVariable<BlockState> state = new (BlockState.Intact);
    private readonly NetworkVariable<float> damage = new ();

    private BlockAnimation blockAnimation;
    private Coroutine hideRoutine;
    private GameObject dropInstance;   // 差し込んだモデル。状態が変わったら捨てる

    private void Awake()
    {
        this.intactLayerId  = this.gameObject.layer;   // 配置時のレイヤーを覚える
        this.droppedLayerId = LayerMask.NameToLayer(this.droppedLayerName);
        this.blockAnimation = GetComponentInChildren<BlockAnimation>();
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

        if (this.TryRollItem(out Items item))
        {
            this.droppedItem.Value = item;
            this.state.Value = BlockState.Dropped;
        }
        else
        {
            this.state.Value = BlockState.Consumed;   // 何も出ないブロック
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (this.state.Value != BlockState.Dropped) return;
        if (!other.TryGetComponent(out PlayerItem playerItem)) return;

        if (!playerItem.TryApplyItemServer(this.droppedItem.Value)) return;

        this.state.Value = BlockState.Consumed;
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
        if (this.blockCollider != null) this.blockCollider.enabled = (s == BlockState.Intact);
        if (this.dropVisual    != null) this.dropVisual.SetActive(s == BlockState.Dropped);
        if (this.dropTrigger   != null) this.dropTrigger.enabled  = (s == BlockState.Dropped);

        this.ApplyDropModel(s);

        // 落ちたアイテムに弾道・照準が反応しないよう、壊れたらレイヤーを戻す
        this.gameObject.layer = (s == BlockState.Intact) ? this.intactLayerId : this.droppedLayerId;

        // 消す側は PlayBreakThenHide に任せる。ここでは戻すときだけ扱う
        if (this.blockVisual != null && s == BlockState.Intact) this.blockVisual.SetActive(true);
    }

    /// <summary>
    /// 落ちたアイテムのモデルを差し込む。見た目だけなので各クライアントがローカルに生成する。
    /// droppedItem は state より先に届くので、途中参加でも正しいモデルが出る
    /// </summary>
    private void ApplyDropModel(BlockState s)
    {
        if (s != BlockState.Dropped)
        {
            if (this.dropInstance != null) Destroy(this.dropInstance);
            this.dropInstance = null;
            return;
        }

        if (this.dropInstance != null) return;   // 既に出ている
        if (this.dropVisual == null || ItemDatabase.Instance == null) return;

        var data = ItemDatabase.Instance.GetItem(this.droppedItem.Value);
        if (data == null || data.itemPrefab == null) return;   // 未設定なら何も出さない

        // 第3引数 false でマウントのローカル座標に置く。true だとプレハブの世界座標を保とうとする
        this.dropInstance = Instantiate(data.itemPrefab, this.dropVisual.transform, false);
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
