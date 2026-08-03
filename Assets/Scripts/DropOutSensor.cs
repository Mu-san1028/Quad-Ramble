using UnityEngine;
using Unity.Netcode;

public class DropOutSensor : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う
        if (!other.CompareTag("Player")) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;
        PlayerController player = other.GetComponent<PlayerController>();
        PlayerManager.Instance.DropOut(player);
        player.Eliminate();
    }
}
