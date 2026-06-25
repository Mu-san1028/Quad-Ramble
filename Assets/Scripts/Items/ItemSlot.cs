using System;

[Serializable]
public class ItemSlot
{
    public ItemBase itemData;  // 大元の設計図（ScriptableObject. 書き換えない）
    public int currentAmount;  // 実際の所持数（ここを足し引きする）

    public ItemSlot(ItemBase data, int amount)
    {
        this.itemData = data;
        this.currentAmount = amount;
    }
}
