using UnityEngine;

/// <summary>収集と戦闘のどちらに操作を任せるかを決める。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BotItemCollector), typeof(BotCombatController))]
[DefaultExecutionOrder(-209)]
public sealed class BotBrain : MonoBehaviour
{
    private PlayerController player;
    private PlayerItem inventory;
    private BotItemCollector collector;
    private BotCombatController combat;

    public bool IsFighting { get; private set; }

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        inventory = GetComponent<PlayerItem>();
        collector = GetComponent<BotItemCollector>();
        combat = GetComponent<BotCombatController>();
    }

    public static bool CanAct(PlayerController player)
    {
        var round = RoundManager.Instance;
        return player != null && player.IsSpawned && player.IsServer && player.IsBot
            && player.isActiveAndEnabled && player.IsControllable
            && round != null && round.IsSpawned && round.IsReady && !round.IsIntro && !round.IsRoundOver
            && round.CountdownRemaining <= RoundManager.StartDisplayDuration;
    }

    public static bool IsUsableWeapon(ItemSlot slot)
    {
        if (slot == null || slot.currentAmount <= 0 || slot.itemData is not WeaponData weapon) return false;
        // Deployable自体がまだ未実装なので、戦闘可能な武器には含めない。
        return weapon.weaponType == WeaponType.Melee ? weapon.attackRange != null
            : weapon.weaponType == WeaponType.Throw && weapon.bulletPrefab != null && weapon.throwSpeed > 0f;
    }

    private void Update()
    {
        bool canAct = CanAct(player);
        if (canAct && !IsUsableWeapon(inventory.CurrentWeapon))
        {
            // 空・使い切り・未対応武器のスロットなら、使用可能な所持武器へ切り替える。
            for (int i = 0; i < inventory.Weapons.Length && !IsUsableWeapon(inventory.CurrentWeapon); i++)
                inventory.SwitchWeapon(1, playSE: false);
        }

        IsFighting = canAct && IsUsableWeapon(inventory.CurrentWeapon);
        // 先に現在の担当を停止してから次の担当を動かす。
        if (IsFighting)
        {
            collector.SetCollecting(false);
            combat.SetCombatEnabled(true);
        }
        else
        {
            combat.SetCombatEnabled(false);
            collector.SetCollecting(canAct);
        }
    }

    private void OnDisable()
    {
        IsFighting = false;
        if (combat != null) combat.SetCombatEnabled(false);
        if (collector != null) collector.SetCollecting(false);
    }
}
