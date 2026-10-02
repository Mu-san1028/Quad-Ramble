using UnityEngine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private IInteractable interactableObject; // リンクするオブジェクトを指定するためのフィールド
    
    public void SpawnPlayer(PlayerController player, int spawnIndex)
    {
        player.CurrentMoveMode = PlayerController.MoveMode.CameraRelative;
        // スポーンポイントのインデックスが有効かどうかを確認
        if (spawnIndex < 0 || spawnIndex >= spawnPoints.Length)
        {
            Debug.LogError("Invalid spawn index: " + spawnIndex);
            return;
        }

        //　スポーンポイントは事前に設定され、　spawnIndexに基づいて選択される
        Transform spawnPoint = spawnPoints[spawnIndex];

        // 前のラウンドのセーブポイントを持ち越さない
        SavePointRegistry.Clear(player);
        SavePointRegistry.RecordStart(player, spawnPoint.position, spawnPoint.rotation);

        // プレイヤーをスポーンさせる
        player.Teleport(spawnPoint.position, spawnPoint.rotation);
        player.Revive();
        player.GetComponent<PlayerStatus>()?.ResetForRound();

        //　PlayerItemを初期化する
        PlayerItem playerItem = player.GetComponent<PlayerItem>();
        playerItem.ClearAllWeapons();

        if (interactableObject != null)
        {
            interactableObject.Interact(player);
        }
    }
}
