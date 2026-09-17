using UnityEngine;

[CreateAssetMenu(fileName = "NewConsumable", menuName = "Items/Consumable")]
public class ConsumableItemData : ItemBase
{
    public float speedBoost;   // 加算
    public float jumpBoost;    // 加算
    public float powerRate;    // 割合。0.2 で武器の威力 +20%
    public float rangeRate;    // 割合。0.2 で攻撃範囲・爆発半径・弾速 +20%
    public float duration;
}
