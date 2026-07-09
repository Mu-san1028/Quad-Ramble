using UnityEngine;
using Unity.Netcode;

public class DropOutSensor : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う

        PlayerController player = other.GetComponent<PlayerController>();
        PlayerManager.Instance.DropOut(player);
        player.Despawn();
    }
}
