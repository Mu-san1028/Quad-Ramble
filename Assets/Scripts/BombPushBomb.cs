using UnityEngine;

public class BombPushBomb : MonoBehaviour
{
    [SerializeField] private GameObject explosionEffectPrefab; // 爆発エフェクトのプレハブ

    private bool hasExploded;

    public void Explode()
    {
        if (hasExploded) return;

        hasExploded = true;

        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        Destroy(gameObject);
    }
}
