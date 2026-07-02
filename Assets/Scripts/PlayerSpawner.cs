using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    void Start()
    {
        
    }
    public void SpawnPlayer(PlayerController player, int spawnIndex)
    {
        player.CurrentMoveMode = PlayerController.MoveMode.WorldAbsolute;
        // スポーンポイントのインデックスが有効かどうかを確認
        if (spawnIndex < 0 || spawnIndex >= spawnPoints.Length)
        {
            Debug.LogError("Invalid spawn index: " + spawnIndex);
            return;
        }

        //　スポーンポイントは事前に設定され、　spawnIndexに基づいて選択される
        Transform spawnPoint = spawnPoints[spawnIndex];

        {
            // プレイヤーをスポーンさせる
            player.transform.position = spawnPoint.position;
            player.transform.rotation = spawnPoint.rotation;

            //　PlayerItemを初期化する
            PlayerItem playerItem = player.GetComponent<PlayerItem>();
            playerItem.ClearAllWeapons();

        }
    }
}
