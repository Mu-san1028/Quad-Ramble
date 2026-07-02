using UnityEngine;
using Unity.Netcode;

/// <summary>
/// プレイヤーのアイテム取得を管理
/// - アイテムを取得したときの処理
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerItem : NetworkBehaviour
{
    [field: SerializeField] public ItemSlot[] Weapons { get; private set; } = new ItemSlot[3];
    private int currentWeaponIndex; // 現在装備している武器
    private PlayerController playerController;
    private ItemDatabase itemDatabase;

    public ItemSlot CurrentWeapon
    {
        get { return this.Weapons[this.currentWeaponIndex]; }
    }

    public int CurrentWeaponIndex
    {
        get { return this.currentWeaponIndex; }
    }

    private void Start()
    {
        this.playerController = GetComponent<PlayerController>();
        this.itemDatabase = FindFirstObjectByType<ItemDatabase>();
    }

    public void ApplyItem(Items item)
    {
        this.ApplyItemServerRpc(item);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ApplyItemServerRpc(Items item) {
        this.ApplyItemClientRpc(item);
    }

    [ClientRpc]
    private void ApplyItemClientRpc(Items item)
    {
        ItemBase data = this.itemDatabase.GetItem(item);
        if (data is ConsumableItemData consumableItem)
        {
            this.ApplyConsumableItem(consumableItem);
        }
        else if (data is WeaponData weapon)
        {
            this.ApplyWeapon(weapon);
        }
    }

    private void ApplyConsumableItem(ConsumableItemData data) {
        Debug.Log("消費アイテムを取得: " + data.name);
        this.playerController.ApplyBoost(data);
    }

    private void ApplyWeapon(WeaponData weapon) {
        Debug.Log("武器を取得: " + weapon.name);

        // 空いているスロットを探す
        int emptySlotIndex = -1;
        for (int i = 0; i < this.Weapons.Length; i++)
        {
            if (this.Weapons[i] == null || this.Weapons[i].itemData == null)
            {
                emptySlotIndex = i;
                break;
            }
        }
        if (emptySlotIndex != -1)
        {
            // 空き枠に新しい箱を作って武器を入れる
            this.Weapons[emptySlotIndex] = new ItemSlot(weapon, weapon.bulletStock); 
            Debug.Log($"スロット {emptySlotIndex + 1} に {weapon.name} を収納した！");

            // はじめて武器を拾った時は自動装備
            // （CurrentWeapon 自体が無い、または中身が無い場合）
            if (CurrentWeapon == null || CurrentWeapon.itemData == null) 
            {
                EquipWeapon(emptySlotIndex);
            }
        }
        else
        {
            // カバンがいっぱいの場合：今の武器と入れ替える
            Debug.Log($"カバンが満杯！ {CurrentWeapon.itemData.itemName} を捨てて {weapon.name} を装備した！");
            
            // TODO: ここで「古い武器(CurrentWeapon)のPrefabを目の前にドロップする処理」を入れる
            this.Weapons[currentWeaponIndex] = new ItemSlot(weapon, weapon.bulletStock);
            EquipWeapon(currentWeaponIndex);
        }
    }

    // 武器切り替えの共通ロジック（direction は 1 または -1 が入る）
    public void SwitchWeapon(int direction)
    {
        // 3つのスロットを順番に探す
        for (int i = 1; i <= this.Weapons.Length; i++)
        {
            int step = i * direction;
            int nextIndex = (currentWeaponIndex + step + this.Weapons.Length) % this.Weapons.Length;

            // 箱が存在し、かつ、中身(itemData)も空っぽじゃない時だけ持ち替える
            if (this.Weapons[nextIndex] != null && this.Weapons[nextIndex].itemData != null)
            {
                EquipWeapon(nextIndex);
                break;
            }
        }
    }

    private void EquipWeapon(int index)
    {
        currentWeaponIndex = index;
        Debug.Log($"スロット {index + 1} の {CurrentWeapon.itemData.itemName} に持ち替えた！");
        
        // TODO: 手に持っている3Dモデルを切り替える処理
        // TODO: UI（アイコンや残弾数）を更新する処理
    }

    public bool TryConsumeAmmo()
    {
        // スロットが空、またはスロットの中の設計図が空なら撃てない
        if (Weapons[currentWeaponIndex] == null || Weapons[currentWeaponIndex].itemData == null) return false;

        if (Weapons[currentWeaponIndex].currentAmount > 0)
        {
            Weapons[currentWeaponIndex].currentAmount--;
            Debug.Log($"残弾数: {Weapons[currentWeaponIndex].currentAmount}");
            return true;
        }
        else
        {
            Debug.Log("弾切れです");
            return false;
        }
    }

    /// <summary>
    /// 【外部から呼び出し用】すべての武器スロットを空にする
    /// </summary>
    public void ClearAllWeapons()
    {
        if (this.Weapons == null) return;

        for (int i = 0; i < this.Weapons.Length; i++)
        {
            this.Weapons[i] = null; // スロットを空にする
        }

        this.currentWeaponIndex = 0; // 装備位置もリセット
        Debug.Log("外部スクリプトからの要求により、すべての武器を空にしました。");

        // TODO: ここで手に持っている3Dモデルを消したり、UIを消したりする処理を呼ぶ
    }
}
