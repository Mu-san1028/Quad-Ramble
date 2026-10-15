using UnityEngine;

public class BombPushBomb : MonoBehaviour
{
    [SerializeField] private GameObject explosionEffectPrefab; // 爆発エフェクトのプレハブ
    public void Explode()
    {
        // 爆発の処理をここに実装
        Debug.Log("Bomb exploded!");
        Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }
}
