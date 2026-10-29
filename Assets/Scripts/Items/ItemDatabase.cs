using System;
using System.Linq;
using UnityEngine;

public enum Items
{
    // Consumable Items
    JumpBoost,
    SpeedBoost,
    TurnSpeedBoost,

    // Weapons
    Bomb,
    GoldenBomb,
    Gun,
    shuriken,
    Spear,
    Sword,
    Landmine,

    // Permanent Upgrades
    JumpUp,
    SpeedUp,
    TurnUp,      // 廃止。値がずれると各シーンの抽選テーブルが壊れるので消さない
    PowerUp,
    RangeUp,

    // Append weapons
    RedPotion,
    BluePotion,
    YellowPotion,

    // 既存のシーン・アイテムの番号を保持して末尾へ追加する。
    JumpDownPotion,
    PowerDownPotion,
    shurikenGold,
    Dagger,
    Axe,
    GoldenSpear,
    Rifle,
    GoldRifle
}

public class ItemDatabase : MonoBehaviour
{
    [Serializable]
    private class ItemEntry
    {
        public Items key;
        public ItemBase value;
    }

    [SerializeField] private ItemEntry[] items;

    [Header("全武器共通のクリティカル設定")]
    [SerializeField, Min(1f)] private float criticalDamageMultiplier = 1.5f;
    public float CriticalDamageMultiplier => Mathf.Max(1f, criticalDamageMultiplier);

    public static ItemDatabase Instance { get; private set; }

    private void Awake()
    {
        if (ItemDatabase.Instance)
        {
            Debug.LogWarning("An instance of ItemDatabase already exists.");
            Destroy(this.gameObject);
        }
        else
        {
            ItemDatabase.Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    public ItemBase GetItem(Items key) => this.items.First(entry => entry.key == key).value;
    public Items GetKey(ItemBase data) => this.items.First(e => e.value == data).key;

}
