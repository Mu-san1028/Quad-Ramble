using UnityEngine;

public class ItemGet : MonoBehaviour
{
    public ItemBase itemData; 

    // いったんとりあえずの処理です
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            
            if (player != null && itemData != null)
            {
                player.GetItem(itemData);
                Destroy(gameObject); 
            }
        }
    }
}
