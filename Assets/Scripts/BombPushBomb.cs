using UnityEngine;

public class BombPushBomb : MonoBehaviour
{
    [SerializeField] private GameObject explosionEffectPrefab; // 爆発エフェクトのプレハブ
    [Tooltip("爆発エフェクトのワールド回転（度）。弾の向きには追従しません。")]
    [SerializeField] private Vector3 explosionEffectRotation = Vector3.zero;

    private bool hasExploded;

    public void Explode()
    {
        if (hasExploded) return;

        hasExploded = true;

        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position,
                Quaternion.Euler(this.explosionEffectRotation));
        }

        Destroy(gameObject);
    }
}
