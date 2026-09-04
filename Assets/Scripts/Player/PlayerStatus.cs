using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤーの可変ステータスを一元管理する。
/// 能力値は「基礎値 + 有効な効果の合計」で求め、基礎値そのものは書き換えない。
/// </summary>
public class PlayerStatus : NetworkBehaviour
{
    private class ActiveEffect
    {
        public StatBlock Bonus;
        public float RemainingTime;
        public bool IsPermanent;   // ラウンド終了まで消えない
    }

    [Header("疲労設定")]
    [SerializeField] private float fatiguePerPower = 5.0f;   // 威力1あたりの疲労蓄積量
    [SerializeField] private float fatigueScale = 0.5f;     // 疲労1あたりの倍率上昇

    private readonly NetworkVariable<int> roundScore = new ();
    public int RoundScore => this.roundScore.Value;

    private readonly NetworkVariable<float> fatigue = new ();
    public float Fatigue => this.fatigue.Value;

    /// <summary>ノックバック倍率。疲労が溜まるほど大きくなる</summary>
    public float KnockbackMultiplier => 1f + this.fatigue.Value * this.fatigueScale;

    private readonly List<ActiveEffect> effects = new ();

    // サーバーが書き、全員が読む
    private readonly NetworkVariable<StatBlock> bonuses = new ();
    private readonly NetworkVariable<StackBlock> stackCounts = new ();

    public float SpeedBonus => this.bonuses.Value.Speed;
    public float JumpBonus  => this.bonuses.Value.Jump;

    /// <summary>武器の威力にかける倍率。強化が無ければ 1</summary>
    public float PowerMultiplier => 1f + this.bonuses.Value.Power;

    /// <summary>攻撃範囲・爆発半径・弾速にかける倍率。強化が無ければ 1</summary>
    public float RangeMultiplier => 1f + this.bonuses.Value.Range;

    /// <summary>自分のプレイヤーの PlayerStatus。UI がここから読む</summary>
    public static PlayerStatus Local { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) Local = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    private void Update()
    {
        if (!IsServer) return;

        bool changed = false;
        for (int i = this.effects.Count - 1; i >= 0; i--)
        {
            var e = this.effects[i];
            if (e.IsPermanent) continue;

            e.RemainingTime -= Time.deltaTime;
            if (e.RemainingTime <= 0f)
            {
                this.effects.RemoveAt(i);
                changed = true;
            }
        }

        if (changed) this.Recalculate();
    }

    /// <summary>一時効果アイテム</summary>
    public void AddTemporary(ConsumableItemData data)
    {
        if (!IsServer) return;

        this.effects.Add(new ActiveEffect
        {
            Bonus = new StatBlock
            {
                Speed = data.speedBoost,
                Jump  = data.jumpBoost,
                Power = data.powerRate,
                Range = data.rangeRate,
            },
            RemainingTime = data.duration,
        });
        this.Recalculate();
    }

    /// <summary>恒久強化。上限に達していたら false を返す</summary>
    public bool AddPermanent(PermanentUpgrade data)
    {
        if (!IsServer) return false;

        if (this.GetStacks(data.kind) >= data.maxStacks) return false;

        // 構造体なので、コピーして書き換えて代入し直さないと同期されない
        StackBlock counts = this.stackCounts.Value;
        switch (data.kind)
        {
            case UpgradeKind.Speed: counts.Speed += 1; break;
            case UpgradeKind.Jump:  counts.Jump  += 1; break;
            case UpgradeKind.Power: counts.Power += 1; break;
            case UpgradeKind.Range: counts.Range += 1; break;
        }
        this.stackCounts.Value = counts;

        this.effects.Add(new ActiveEffect
        {
            Bonus = new StatBlock
            {
                Speed = data.speedBoost,
                Jump  = data.jumpBoost,
                Power = data.powerRate,
                Range = data.rangeRate,
            },
            IsPermanent = true
        });
        this.Recalculate();
        return true;
    }

    public int GetStacks(UpgradeKind kind) => kind switch
    {
        UpgradeKind.Speed => this.stackCounts.Value.Speed,
        UpgradeKind.Jump  => this.stackCounts.Value.Jump,
        UpgradeKind.Power => this.stackCounts.Value.Power,
        UpgradeKind.Range => this.stackCounts.Value.Range,
        _ => 0,
    };

    /// <summary>攻撃を受けたときに疲労を蓄積する（サーバーのみ）</summary>
    public void AddFatigue(float power)
    {
        if (!IsServer) return;
        this.fatigue.Value += power * this.fatiguePerPower;
    }

    /// <summary>サーバーのみ。ラウンド中の得点を加算する</summary>
    public void AddRoundScore(int delta)
    {
        Debug.Log($"AddRoundScore({delta}) called on {this.name}");
        if (!IsServer) return;
        this.roundScore.Value += delta;
    }

    /// <summary>ラウンド開始時にすべて解除する</summary>
    public void ResetForRound()
    {
        if (!IsServer) return;

        this.effects.Clear();
        this.stackCounts.Value = default;
        this.fatigue.Value = 0f;
        this.roundScore.Value = 0;
        this.Recalculate();
    }

    // Power と Range は割合の合計。使う側で 1 を足して倍率にする
    private void Recalculate()
    {
        StatBlock total = default;
        foreach (var e in this.effects)
        {
            total.Speed += e.Bonus.Speed;
            total.Jump  += e.Bonus.Jump;
            total.Power += e.Bonus.Power;
            total.Range += e.Bonus.Range;
        }
        this.bonuses.Value = total;
    }
}

/// <summary>Speed/Jump は加算値、Power/Range は割合（0.2 で +20%）</summary>
public struct StatBlock : INetworkSerializeByMemcpy
{
    public float Speed;
    public float Jump;
    public float Power;
    public float Range;
}

/// <summary>恒久強化を何段積んだか</summary>
public struct StackBlock : INetworkSerializeByMemcpy
{
    public int Speed;
    public int Jump;
    public int Power;
    public int Range;
}
