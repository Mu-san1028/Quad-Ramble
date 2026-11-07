using UnityEngine;
using Unity.Netcode;

/// <summary>
/// プレイヤーのアイテム取得を管理
/// - アイテムを取得したときの処理
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerItem : NetworkBehaviour
{
    public static PlayerItem Local { get; private set; }
    public static event System.Action<string, NoticeType> LocalItemNotice;
    [field: SerializeField] public ItemSlot[] Weapons { get; private set; } = new ItemSlot[3];
    private int currentWeaponIndex; // 現在装備している武器
    private PlayerController playerController;
    private PlayerStatus playerStatus;
    public int LastWeaponsResetRound { get; private set; } = -1;

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
        this.playerStatus = GetComponent<PlayerStatus>();
    }

    public override void OnNetworkSpawn()
    {
        this.LastWeaponsResetRound = -1;
        if (GetComponent<PlayerController>().IsLocalHuman) Local = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    public void ApplyItem(Items item)
    {
        this.ApplyItemServerRpc(item);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ApplyItemServerRpc(Items item) {
        TryApplyItemServer(item);
    }

    /// <summary>サーバーで取得可否を判定する。満杯の武器は取得しない。</summary>
    public bool TryApplyItemServer(Items item)
    {
        if (!IsServer || ItemDatabase.Instance == null) return false;

        ItemBase data = ItemDatabase.Instance.GetItem(item);
        if (data == null) return false;

        if (data is WeaponData weapon)
        {
            int slot = FindEmptyWeaponSlot();
            if (slot < 0) return false;

            ApplyWeapon(weapon, slot);
            ReceiveWeaponClientRpc(item, slot, this.LastWeaponsResetRound);
        }
        else
        {
            ApplyItemData(data);
        }

        return true;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReceiveWeaponClientRpc(Items item, int slot, int round)
    {
        if (IsServer || round != this.LastWeaponsResetRound) return;

        if (ItemDatabase.Instance.GetItem(item) is WeaponData weapon)
            ApplyWeapon(weapon, slot);
    }

    private void ApplyItemData(ItemBase data)
    {
        if (data is ConsumableItemData consumableItem)
        {
            this.ApplyConsumableItem(consumableItem);
        }
        else if (data is PermanentUpgrade upgrade)
        {
            this.ApplyPermanentUpgrade(upgrade);
        }

    }

    private void ApplyConsumableItem(ConsumableItemData data)
    {
        if (!IsServer) return;

        Debug.Log("消費アイテムを取得: " + data.name);
        this.playerStatus.AddTemporary(data);
        NotifyUpgradeOwnerRpc(GetPickupMessage(data));
    }

    private void ApplyPermanentUpgrade(PermanentUpgrade data)
    {
        if (!IsServer) return;
        bool applied = this.playerStatus.AddPermanent(data);
        Debug.Log(applied ? $"恒久強化を取得: {data.itemName}" : $"上限のため無効: {data.itemName}");

        if (applied)
            NotifyUpgradeOwnerRpc(GetPickupMessage(data));
    }

    [Rpc(SendTo.Owner)]
    private void NotifyUpgradeOwnerRpc(string message)
    {
        if (GetComponent<PlayerController>().IsLocalHuman)
        {
            LocalItemNotice?.Invoke(message, NoticeType.Upgrade);
            SEAudio.Instance?.Play(SEType.PowerUpPickup);
        }
    }

    private static string GetDisplayName(ItemBase data)
    {
        return string.IsNullOrWhiteSpace(data.itemName) ? data.name : data.itemName;
    }

    private static string GetPickupMessage(ItemBase data)
    {
        return string.IsNullOrWhiteSpace(data.pickupMessage)
            ? $"{GetDisplayName(data)}を拾った"
            : data.pickupMessage;
    }

    private int FindEmptyWeaponSlot()
    {
        for (int i = 0; i < this.Weapons.Length; i++)
        {
            if (this.Weapons[i] == null || this.Weapons[i].itemData == null)
                return i;
        }

        return -1;
    }

    private void ApplyWeapon(WeaponData weapon, int slot)
    {
        bool needsEquip = CurrentWeapon == null || CurrentWeapon.itemData == null;
        this.Weapons[slot] = new ItemSlot(weapon, weapon.bulletStock);

        if (needsEquip)
            EquipWeapon(slot);

        if (GetComponent<PlayerController>().IsLocalHuman)
        {
            LocalItemNotice?.Invoke(GetPickupMessage(weapon), NoticeType.Weapon);
            SEAudio.Instance?.Play(SEType.ItemPickup);
        }
    }

    // 武器切り替えの共通ロジック（direction は 1 または -1 が入る）
    public void SwitchWeapon(int direction, bool playSE = true)
    {
        // 3つのスロットを順番に探す
        for (int i = 1; i <= this.Weapons.Length; i++)
        {
            int step = i * direction;
            int nextIndex = (currentWeaponIndex + step + this.Weapons.Length) % this.Weapons.Length;

            // 箱が存在し、かつ、中身(itemData)も空っぽじゃない時だけ持ち替える
            if (this.Weapons[nextIndex] != null && this.Weapons[nextIndex].itemData != null)
            {
                if (nextIndex == this.currentWeaponIndex)
                    return;

                EquipWeapon(nextIndex);

                if (playSE && GetComponent<PlayerController>().IsLocalHuman)
                    SEAudio.Instance?.Play(SEType.WeaponSelect);

                break;
            }
        }
    }

    public void DiscardIfEmpty()
    {
        ItemSlot slot = this.CurrentWeapon;
        if (slot == null || slot.itemData == null) return;
        if (slot.currentAmount > 0) return;

        Debug.Log($"{slot.itemData.itemName} を使い切ったので捨てた");
        this.Weapons[this.currentWeaponIndex] = null;

        if (IsSpawned && IsOwner)
            ReleaseWeaponSlotServerRpc(this.currentWeaponIndex, this.LastWeaponsResetRound);

        // 他に武器が残っていれば自動で持ち替える（SwitchWeapon は空スロットを飛ばす）
        this.SwitchWeapon(1, playSE: false);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ReleaseWeaponSlotServerRpc(int slot, int round)
    {
        if (round != this.LastWeaponsResetRound || slot < 0 || slot >= Weapons.Length) return;

        Weapons[slot] = null;
        ReleaseWeaponSlotClientRpc(slot, round);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ReleaseWeaponSlotClientRpc(int slot, int round)
    {
        if (IsServer || IsOwner || round != this.LastWeaponsResetRound || slot < 0 || slot >= Weapons.Length) return;

        Weapons[slot] = null;
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

    /// <summary>サーバーから全端末へラウンド開始時の武器リセットを通知する。</summary>
    public void ResetWeaponsForRound(int round)
    {
        if (!IsServer || !IsSpawned) return;

        this.ApplyRoundWeaponReset(round);
        this.ResetWeaponsForRoundClientRpc(round);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ResetWeaponsForRoundClientRpc(int round)
    {
        if (IsServer) return;
        this.ApplyRoundWeaponReset(round);
    }

    private void ApplyRoundWeaponReset(int round)
    {
        // 古い通知や同じラウンドの再通知で、新しく拾った武器を消さない。
        if (round <= this.LastWeaponsResetRound) return;

        this.ClearAllWeapons();
        this.LastWeaponsResetRound = round;
    }

    /// <summary>この端末の武器配列を消去する。ラウンド開始時はResetWeaponsForRoundを使う。</summary>
    public void ClearAllWeapons()
    {
        if (this.Weapons == null) return;

        for (int i = 0; i < this.Weapons.Length; i++)
        {
            this.Weapons[i] = null; // スロットを空にする
        }

        this.currentWeaponIndex = 0; // 装備位置もリセット
        Debug.Log("外部スクリプトからの要求により、すべての武器を空にしました。");

        // WeaponUIは毎フレームこの配列を読み直すため、次の更新で空表示になる。
    }
}
