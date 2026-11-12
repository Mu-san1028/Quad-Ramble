using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 接近したプレイヤーに一定間隔でダメージとノックバックを与えます。
/// プレイヤー側の既存仕様に合わせ、ダメージはPlayerStatusの疲労として加算します。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAttack : NetworkBehaviour
{
    [SerializeField, Min(0.05f)] private float attackInterval = 1.2f;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Min(0f)] private float knockbackForce = 5f;
    [SerializeField, Min(0f)] private float upwardForce = 1.5f;

    private PlayerController target;
    private float nextAttackTime;

    public void SetTarget(PlayerController value) => target = value;

    private void Update()
    {
        if (IsSpawned && !IsServer) return;
        if (target == null || !target.isActiveAndEnabled || target.IsEliminated
            || Time.time < nextAttackTime)
            return;

        nextAttackTime = Time.time + Mathf.Max(0.05f, attackInterval);
        GetComponent<EnemyAnimator>()?.PlayAttack();
        Vector3 direction = target.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
        direction.Normalize();

        Vector3 force = direction * knockbackForce + Vector3.up * upwardForce;
        target.ApplyEnemyHitServer(damage, force);
    }
}
