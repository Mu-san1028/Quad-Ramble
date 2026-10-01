using UnityEngine;

public class DestroyToInstantiate : MonoBehaviour
{
    [SerializeField] private GameObject objectPrefab; // 出現させるオブジェクトのプレハブ
    [SerializeField] private Transform spawnPoint; // 出現させる位置

    void Awake()
    {
        if (spawnPoint == null)
        {
            spawnPoint = transform; // スポーンポイントが指定されていない場合は自身の位置を使用
        }
    }

    public void OnDestroy()
    {
        // 新しいオブジェクトを生成
        if (objectPrefab && spawnPoint)
        {
            Instantiate(objectPrefab, spawnPoint.position, spawnPoint.rotation);
        }
    }
}
