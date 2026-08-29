using Unity.Netcode;
using UnityEngine;

public class RespawnSensor : MonoBehaviour
{
    [SerializeField] private Transform[] respawnPoints;

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (!other.CompareTag("Player")) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        var player = other.GetComponent<PlayerController>();
        if (player == null || respawnPoints.Length == 0) return;

        // ランダムにリスポーンポイントを選択
        int randomIndex = Random.Range(0, respawnPoints.Length);
        Transform respawnPoint = respawnPoints[randomIndex];

        player.Teleport(respawnPoint.position, respawnPoint.rotation);
    }
}
