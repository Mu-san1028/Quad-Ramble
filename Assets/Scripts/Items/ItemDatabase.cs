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
    Gun,
    Thorn,
    Spear,
    Sword,
    Landmine,

    // Permanent Upgrades
    JumpUp,
    SpeedUp,
    TurnUp,
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
