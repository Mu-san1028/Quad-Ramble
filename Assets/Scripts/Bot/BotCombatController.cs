using UnityEngine;

/// <summary>近くの生存相手へ接近し、共通入力を使って通常の攻撃を行う。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BotController), typeof(PlayerActionInput), typeof(PlayerItem))]
[DefaultExecutionOrder(-201)]
public sealed class BotCombatController : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float targetSearchInterval = 0.5f;
    [Tooltip("遠距離武器で攻撃を試みる最大距離。実際の弾速・寿命・重力でも制限する。")]
    [SerializeField, Min(1f)] private float maxRangedDistance = 12f;
    [SerializeField, Min(0.05f)] private float attackInterval = 0.2f;
    [Tooltip("相手の足元から狙う高さ。CharacterControllerがあればその中心を使う。")]
    [SerializeField] private float fallbackAimHeight = 1f;

    private PlayerController player;
    private PlayerItem inventory;
    private PlayerStatus status;
    private PlayerActionInput actions;
    private BotController movement;
    private PlayerController target;
    private bool combatEnabled;
    private float nextSearch;
    private float nextAttack;
    private int placementRound = -1;

    public PlayerController Target => target;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        inventory = GetComponent<PlayerItem>();
        status = GetComponent<PlayerStatus>();
        actions = GetComponent<PlayerActionInput>();
        movement = GetComponent<BotController>();
    }

    public void SetCombatEnabled(bool value)
    {
        if (combatEnabled == value) return;
        combatEnabled = value;
        if (!value) StopCombat();
        else nextSearch = nextAttack = 0f;
    }

    private void Update()
    {
        if (!combatEnabled) return;
        if (!BotBrain.CanAct(player) || !movement.isActiveAndEnabled || !actions.isActiveAndEnabled)
        {
            StopCombat();
            return;
        }

        if (placementRound != player.LastPlacementRound)
        {
            StopCombat();
            placementRound = player.LastPlacementRound;
        }

        if (!BotBrain.IsUsableWeapon(inventory.CurrentWeapon))
        {
            StopCombat();
            return;
        }

        if (!IsValidTarget(target) || Time.time >= nextSearch)
        {
            var previous = target;
            target = FindNearestEnemy();
            nextSearch = Time.time + Mathf.Max(0.1f, targetSearchInterval);
            if (previous != target) actions.SetAttackHeld(false);
        }

        if (target == null)
        {
            movement.ClearDestination();
            return;
        }

        ItemSlot slot = inventory.CurrentWeapon;
        var weapon = (WeaponData)slot.itemData;
        float scale = status != null ? status.RangeMultiplier : 1f;
        float range = GetEngagementRange(weapon, scale);
        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        if (offset.magnitude > range * 0.75f)
            movement.SetDestination(target.transform.position, Mathf.Max(0.05f, range * 0.65f));
        else
            movement.ClearDestination(resetActions: false);

        var body = target.GetComponent<CharacterController>();
        Vector3 aimPoint = body != null && body.enabled ? body.bounds.center
            : target.transform.position + Vector3.up * fallbackAimHeight;
        actions.SetAimDirection(aimPoint - transform.position);
        bool canHit;
        if (weapon.weaponType == WeaponType.Melee)
        {
            actions.SetAimHeld(false);
            canHit = IsInMeleeRange(weapon, scale);
        }
        else
        {
            actions.SetAimHeld(true);
            float speed = weapon.throwSpeed * (weapon.FliesStraight ? scale : 1f);
            canHit = TryGetShotDirection(aimPoint - player.ProjectileOrigin, speed,
                weapon.gravityScale, player.ProjectileLifetime, out Vector3 direction);
            if (canHit) actions.SetAimDirection(direction);
            canHit &= offset.magnitude <= range;
        }

        if (!canHit)
        {
            actions.SetAttackHeld(false);
            return;
        }

        // 溜め中は押し続け、発射後に離す。次の押下は既存のクールダウン終了後。
        if (actions.AttackHeld)
        {
            if (!player.IsCharging)
            {
                actions.SetAttackHeld(false);
                nextAttack = Time.time + Mathf.Max(0.05f, attackInterval);
            }
            return;
        }

        if (slot.IsReady && Time.time >= nextAttack) actions.SetAttackHeld(true);
    }

    private bool IsValidTarget(PlayerController candidate)
    {
        return candidate != null && candidate != player && candidate.IsSpawned
            && candidate.isActiveAndEnabled && !candidate.IsEliminated && !candidate.IsTitleMode
            && candidate.LastPlacementRound == player.LastPlacementRound;
    }

    private PlayerController FindNearestEnemy()
    {
        if (PlayerManager.Instance == null) return null;
        PlayerController nearest = null;
        float distance = float.PositiveInfinity;
        foreach (var candidate in PlayerManager.Instance.Players)
        {
            if (!IsValidTarget(candidate)) continue;
            if (!movement.CanReach(candidate.transform.position, out _)) continue;
            float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
            if (sqr >= distance) continue;
            nearest = candidate;
            distance = sqr;
        }
        return nearest;
    }

    private float GetEngagementRange(WeaponData weapon, float scale)
    {
        if (weapon.weaponType == WeaponType.Melee)
        {
            float reach = weapon.attackRange switch
            {
                FanRangeData fan => fan.radius,
                SphereRangeData sphere => sphere.radius,
                BoxRangeData box => box.center.z + Mathf.Abs(box.size.z) * 0.5f,
                _ => 1.5f
            };
            return Mathf.Max(0.2f, reach * scale);
        }

        float speed = weapon.throwSpeed * (weapon.FliesStraight ? scale : 1f);
        float reachByTime = speed * player.ProjectileLifetime * 0.8f;
        float reachByGravity = weapon.FliesStraight ? reachByTime : speed * speed / weapon.gravityScale * 0.7f;
        return Mathf.Max(0.2f, Mathf.Min(maxRangedDistance, reachByTime, reachByGravity));
    }

    private bool IsInMeleeRange(WeaponData weapon, float scale)
    {
        foreach (var hit in weapon.attackRange.GetHitTargets(transform.position, transform.rotation,
            player.shootableLayers, scale))
            if (hit.GetComponentInParent<PlayerController>() == target) return true;
        return false;
    }

    internal static bool TryGetShotDirection(Vector3 delta, float speed, float gravity,
        float lifetime, out Vector3 direction)
    {
        direction = delta.normalized;
        if (speed <= 0f || lifetime <= 0f || delta.sqrMagnitude < 0.0001f) return false;
        if (gravity <= 0f) return delta.magnitude / speed <= lifetime;

        float horizontal = new Vector2(delta.x, delta.z).magnitude;
        if (horizontal < 0.01f) return false;
        float speedSquared = speed * speed;
        float discriminant = speedSquared * speedSquared
            - gravity * (gravity * horizontal * horizontal + 2f * delta.y * speedSquared);
        if (discriminant < 0f) return false;

        float angle = Mathf.Atan((speedSquared - Mathf.Sqrt(discriminant)) / (gravity * horizontal));
        float flightTime = horizontal / (speed * Mathf.Cos(angle));
        direction = new Vector3(delta.x / horizontal * Mathf.Cos(angle), Mathf.Sin(angle),
            delta.z / horizontal * Mathf.Cos(angle));
        return flightTime <= lifetime;
    }

    private void StopCombat()
    {
        target = null;
        nextSearch = 0f;
        if (player != null && player.IsSpawned && player.IsServer && player.IsBot)
            movement.ClearDestination();
    }

    private void OnDisable()
    {
        if (combatEnabled) StopCombat();
    }
}
