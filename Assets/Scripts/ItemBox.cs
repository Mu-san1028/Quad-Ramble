using Unity.Netcode;
using UnityEngine;

public class ItemBox : NetworkBehaviour
{
    [SerializeField] Items item; 

    void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (other.TryGetComponent(out PlayerItem playerItem))
        {
            playerItem.ApplyItem(item);
            NetworkObject.Despawn();
        }
    }
}
