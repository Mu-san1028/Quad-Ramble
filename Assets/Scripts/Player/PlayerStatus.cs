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
        public float SpeedBonus;
        public float JumpBonus;
        public float TurnBonus;
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

    // サーバーが書き、全員が読む。どちらも x=Speed, y=Jump, z=Turn
    private readonly NetworkVariable<Vector3> bonuses = new ();
    private readonly NetworkVariable<Vector3Int> stackCounts = new ();

    public float SpeedBonus => this.bonuses.Value.x;
    public float JumpBonus  => this.bonuses.Value.y;
    public float TurnBonus  => this.bonuses.Value.z;

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
            SpeedBonus = data.speedBoost,
            JumpBonus = data.jumpBoost,
            TurnBonus = -data.turnSpeedBoost,   // turnSmoothTime は小さいほど速い
            RemainingTime = data.duration,
        });
        this.Recalculate();
    }

    /// <summary>恒久強化。上限に達していたら false を返す</summary>
    public bool AddPermanent(PermanentUpgrade data)
    {
        if (!IsServer) return false;

        int index = (int)data.kind;
        Vector3Int counts = this.stackCounts.Value;
        if (counts[index] >= data.maxStacks) return false;

        // 構造体なので、コピーして書き換えて代入し直さないと同期されない
        counts[index] += 1;
        this.stackCounts.Value = counts;

        this.effects.Add(new ActiveEffect
        {
            SpeedBonus = data.speedBoost,
            JumpBonus  = data.jumpBoost,
            TurnBonus  = -data.turnSpeedBoost,
            IsPermanent = true
        });
        this.Recalculate();
        return true;
    }

    public int GetStacks(UpgradeKind kind) => this.stackCounts.Value[(int)kind];

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
        this.stackCounts.Value = Vector3Int.zero;
        this.fatigue.Value = 0f;
        this.roundScore.Value = 0;
        this.Recalculate();
    }

    private void Recalculate()
    {
        Vector3 total = Vector3.zero;
        foreach (var e in this.effects)
        {
            total.x += e.SpeedBonus;
            total.y += e.JumpBonus;
            total.z += e.TurnBonus;
        }
        this.bonuses.Value = total;
    }
}
