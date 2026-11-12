using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 敵のHPと死亡者をサーバーで管理します。
/// HPが0で死なない敵は dieAtZero を外し、HPを持たない敵は useHealth を外します。
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyHealth : NetworkBehaviour
{
    [SerializeField, Min(1f)] private float maximumHealth = 100f;
    [SerializeField] private bool useHealth = true;
    [SerializeField] private bool dieAtZero = true;
    [SerializeField] private bool destroyOnDeath = true;
    [SerializeField, Min(0f)] private float deathDespawnDelay = 0.8f;

    // NetworkVariableにすることで、HPと死亡状態を接続中の全クライアントへ同期します。
    private readonly NetworkVariable<float> health = new();
    private readonly NetworkVariable<bool> dead = new();
    private readonly NetworkVariable<ulong> lastPlayerId = new(ulong.MaxValue);

    public float MaximumHealth => maximumHealth;
    public float CurrentHealth => useHealth ? health.Value : maximumHealth;
    public bool IsDead => dead.Value;
    public ulong LastPlayerId => lastPlayerId.Value;

    public override void OnNetworkSpawn()
    {
        // 初期HPはサーバーが一度だけ設定し、以降の変更もサーバーに限定します。
        if (IsServer)
            health.Value = Mathf.Max(1f, maximumHealth);

        dead.OnValueChanged += OnDeadChanged;
        ApplyDeadState(dead.Value);
    }

    public override void OnNetworkDespawn()
    {
        dead.OnValueChanged -= OnDeadChanged;
    }

    /// <summary>攻撃を受けたとき、攻撃者の安定した参加者IDと一緒に呼び出します。</summary>
    public void TakeDamage(float amount, ulong playerId)
    {
        if (!IsServer || IsDead || amount <= 0f) return;

        // 環境ダメージなど、プレイヤーIDがない場合は以前の撃破者を消しません。
        if (playerId != ulong.MaxValue)
            lastPlayerId.Value = playerId;

        if (!useHealth) return;

        health.Value = Mathf.Max(0f, health.Value - amount);
        if (health.Value <= 0f && dieAtZero)
            dead.Value = true;
    }

    private void OnDeadChanged(bool previous, bool current)
    {
        ApplyDeadState(current);
    }

    private void ApplyDeadState(bool value)
    {
        if (!value) return;

        // 死亡時は攻撃・移動を止め、任意でNetworkObjectごと破棄します。
        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = false;

        var ai = GetComponent<EnemyAI>();
        if (ai != null) ai.enabled = false;

        // Animatorが設定されていれば死亡トリガーを再生してから破棄します。
        GetComponent<EnemyAnimator>()?.PlayDeath();

        if (destroyOnDeath && IsServer && IsSpawned)
            StartCoroutine(DespawnAfterDeathAnimation());
    }

    private System.Collections.IEnumerator DespawnAfterDeathAnimation()
    {
        yield return new WaitForSeconds(deathDespawnDelay);
        if (IsSpawned) NetworkObject.Despawn(true);
    }
}
