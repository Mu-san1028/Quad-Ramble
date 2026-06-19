using UnityEngine;
using Unity.Netcode;

/// <summary>
/// プレイヤーのアイテム取得を管理
/// - アイテムを取得したときの処理
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerItem : NetworkBehaviour
{
    public WeaponData[] Weapons { get; private set; } = new WeaponData[3];
    private int currentWeaponIndex; // 現在装備している武器
    private PlayerController playerController;
    private ItemDatabase itemDatabase;

    public WeaponData CurrentWeapon
    {
        get { return this.Weapons[this.currentWeaponIndex]; }
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

        // 空いているスロット（nullの場所）を探す
        int emptySlotIndex = -1;
        for (int i = 0; i < this.Weapons.Length; i++)
        {
            if (this.Weapons[i] == null)
            {
                emptySlotIndex = i;
                break;
            }
        }

        if (emptySlotIndex != -1)
        {
            // 空き枠がある場合：そこに武器を入れる
            this.Weapons[emptySlotIndex] = weapon;
            Debug.Log($"スロット {emptySlotIndex + 1} に {weapon.name} を収納した！");

            // もし初めて武器を拾ったなら、自動でそれを装備する
            if (CurrentWeapon == null || emptySlotIndex == 0)
            {
                EquipWeapon(emptySlotIndex);
            }
        }
        else
        {
            // カバンがいっぱいの場合：今の武器と入れ替える
            Debug.Log($"カバンが満杯！ {CurrentWeapon.name} を捨てて {weapon.name} を装備した！");

            // TODO: ここで「古い武器(CurrentWeapon)のPrefabを目の前にドロップする処理」を入れる

            // 現在のスロットを新しい武器で上書きする
            this.Weapons[currentWeaponIndex] = weapon;
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

            // そのスロットに武器が入っていれば持ち替えて終了
            if (this.Weapons[nextIndex] != null)
            {
                EquipWeapon(nextIndex);
                break;
            }
        }
    }

    private void EquipWeapon(int index)
    {
        currentWeaponIndex = index;
        Debug.Log($"スロット {index + 1} の {CurrentWeapon.name} に持ち替えた！");
        
        // TODO: 手に持っている3Dモデルを切り替える処理
        // TODO: UI（アイコンや残弾数）を更新する処理
    }
}
