using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class DebugKick : MonoBehaviour
{
    void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (Keyboard.current == null || !Keyboard.current.f9Key.wasPressedThisFrame) return;

        foreach (var id in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (id == NetworkManager.ServerClientId) continue;
            Debug.Log($"[DebugKick] client {id} を切断します");
            NetworkManager.Singleton.DisconnectClient(id);
            return;   // 1人だけ
        }
    }
}
