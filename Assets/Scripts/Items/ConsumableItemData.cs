using UnityEngine;


[CreateAssetMenu(fileName = "NewConsumable", menuName = "Items/Consumable")]
public class ConsumableItemData : ItemBase
{
    public float speedBoost;
    public float jumpBoost;
    public float turnSpeedBoost;
    public float duration;

}
