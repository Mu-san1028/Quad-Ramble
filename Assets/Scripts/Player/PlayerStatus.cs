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
    [SerializeField] private float fatiguePerPower = 1.0f;   // 威力1あたりの疲労蓄積量
    [SerializeField] private float fatigueScale = 0.01f;     // 疲労1あたりの倍率上昇

    private readonly NetworkVariable<float> fatigue = new ();
    public float Fatigue => this.fatigue.Value;

    /// <summary>ノックバック倍率。疲労が溜まるほど大きくなる</summary>
    public float KnockbackMultiplier => 1f + this.fatigue.Value * this.fatigueScale;

    private readonly List<ActiveEffect> effects = new ();

    // サーバーが書き、全員が読む。x=Speed, y=Jump, z=Turn
    private readonly NetworkVariable<Vector3> bonuses = new ();

    public float SpeedBonus => this.bonuses.Value.x;
    public float JumpBonus  => this.bonuses.Value.y;
    public float TurnBonus  => this.bonuses.Value.z;

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

    /// <summary>恒久強化。ラウンド終了まで消えない</summary>
    public void AddPermanent(float speed, float jump, float turn)
    {
        if (!IsServer) return;

        this.effects.Add(new ActiveEffect
        {
            SpeedBonus = speed, JumpBonus = jump, TurnBonus = turn, IsPermanent = true
        });
        this.Recalculate();
    }

    /// <summary>攻撃を受けたときに疲労を蓄積する（サーバーのみ）</summary>
    public void AddFatigue(float power)
    {
        if (!IsServer) return;
        this.fatigue.Value += power * this.fatiguePerPower;
    }

    /// <summary>ラウンド開始時にすべて解除する</summary>
    public void ResetForRound()
    {
        if (!IsServer) return;

        this.effects.Clear();
        this.fatigue.Value = 0f;
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
