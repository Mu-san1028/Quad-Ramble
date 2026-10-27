using UnityEngine;
using Unity.Netcode;

public class BombPushBomb : NetworkBehaviour
{
    [SerializeField] private GameObject explosionEffectPrefab; // 爆発エフェクトのプレハブ

    private bool hasExploded;

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
