using UnityEngine;
using Unity.Netcode;

public class BombPushBomb : NetworkBehaviour
{
    [SerializeField] private GameObject explosionEffectPrefab; // 爆発エフェクトのプレハブ

    private bool hasExploded;
    private Rigidbody bombRigidbody;

    private void Awake()
    {
        // Bombの物理移動はサーバー側Rigidbodyに力を加え、NetworkRigidbodyで共有する。
        this.bombRigidbody = GetComponent<Rigidbody>();
    }

    /// <summary>武器を受けたBombを、威力に応じてサーバー上で吹き飛ばす。</summary>
    public void ApplyWeaponKnockback(Vector3 direction, float power)
    {
        if (!IsServer || !IsSpawned || this.hasExploded || this.bombRigidbody == null)
            return;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.up;

        // PlayerControllerのノックバックと同じく、威力に応じた水平力と少しの上向き力を加える。
        Vector3 impulse = direction.normalized * Mathf.Max(0f, power);
        impulse.y += Mathf.Max(0f, power) * 0.3f;
        this.bombRigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    public void Explode()
    {
        // Projectile演出など、ネットワークSpawnされていない見た目専用Bombは
        // 呼び出し端末で演出を出して消す。共有Bombの処理とは分けておく。
        if (NetworkObject == null || !NetworkObject.IsSpawned)
        {
            if (hasExploded) return;
            hasExploded = true;
            if (explosionEffectPrefab != null)
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
            return;
        }

        // Bombの削除と爆発確定はサーバーだけが行う。
        if (!IsServer) return;
        if (hasExploded) return;

        hasExploded = true;

        if (explosionEffectPrefab != null)
        {
            // 演出用オブジェクトはNetworkObjectにせず、全端末で同じ場所に表示する。
            ShowExplosionClientRpc(transform.position);
        }

        // Spawn済みNetworkObjectをDestroyすると各端末で状態がずれるため、Despawnで通知する。
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(destroy: true);
        else
            Destroy(gameObject);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowExplosionClientRpc(Vector3 position)
    {
        if (explosionEffectPrefab != null)
            Instantiate(explosionEffectPrefab, position, Quaternion.identity);
    }
}
