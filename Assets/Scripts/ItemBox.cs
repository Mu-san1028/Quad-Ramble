using UnityEngine;

public class ItemBox : MonoBehaviour
{
    [SerializeField] ItemBase itemData; 

    void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out PlayerItem playerItem))
        {
            playerItem.ApplyItem(itemData);
            Destroy(gameObject);
        }
    }
}
