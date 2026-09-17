using UnityEngine;

public class ObjectSpawner : MonoBehaviour
{
    [SerializeField] private GameObject objectPrefab; // 出現させるオブジェクトのプレハブ
    [SerializeField] private Transform spawnPoint; // 出現させる位置
    [SerializeField] private float initialDelay = 1f; // 初回出現までの遅延時間
    [SerializeField] private float spawnInterval = 2f; // 出現間隔
    [SerializeField] private int maxSpawnCount = 5; // 最大スポーン数
    private int currentSpawnCount = 0; // 現在のスポーン数
    private float spawnTimer = 0f; // スポーンタイマー
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (spawnPoint == null)
        {
            spawnPoint = transform; // スポーンポイントが指定されていない場合は自身の位置を使用
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (currentSpawnCount <= 0)
        {
            spawnTimer += Time.deltaTime;
            if (spawnTimer >= initialDelay)
            {
                Spawn();
                spawnTimer = 0f;
            }
        }
        else
        {
            spawnTimer += Time.deltaTime;
            if (spawnTimer >= spawnInterval && currentSpawnCount < maxSpawnCount)
            {
                Spawn();
                spawnTimer = 0f;
            }
        }
    }

    public void Spawn()
    {
        if (objectPrefab && spawnPoint)
        {
            Instantiate(objectPrefab, spawnPoint.position, spawnPoint.rotation);
            currentSpawnCount++;
        }
    }
}
