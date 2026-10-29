using System;
using UnityEngine;

[Serializable]
public class ItemSlot
{
    public ItemBase itemData;  // 大元の設計図（ScriptableObject. 書き換えない）
    public int currentAmount;  // 実際の所持数（ここを足し引きする）
    public float nextFireTime; // 発射後のクールダウンが終了する時刻

    /// <summary>このスロットの武器が撃てるか</summary>
    public bool IsReady => Time.time >= this.nextFireTime;

    /// <summary>クールダウンの残り割合。撃った直後が1、撃てるようになると0</summary>
    public float CooldownRemaining
    {
        get
        {
            var weapon = this.itemData as WeaponData;
            if (weapon == null) return 0f;

            float cycle = weapon.reloadTime;
            if (cycle <= 0f) return 0f;

            float remaining = this.nextFireTime - Time.time;
            return remaining <= 0f ? 0f : Mathf.Clamp01(remaining / cycle);
        }
    }

    public ItemSlot(ItemBase data, int amount)
    {
        this.itemData = data;
        this.currentAmount = amount;
    }
}
