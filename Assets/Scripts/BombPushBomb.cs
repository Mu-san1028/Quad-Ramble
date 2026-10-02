using UnityEngine;

public class BombPushBomb : MonoBehaviour
{
    public void Explode()
    {
        // 爆発の処理をここに実装
        Debug.Log("Bomb exploded!");
        Destroy(gameObject);
    }
}
