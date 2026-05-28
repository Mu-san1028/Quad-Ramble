using System.Runtime.CompilerServices;
using UnityEngine;

public class DropOutSensor : MonoBehaviour
{
    private RoundManager roundManager;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        roundManager = FindObjectOfType<RoundManager>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null)
        {
            PlayerManager.Instance.DropOut(player);
        }

        if (other.CompareTag("Player"))
        {
            Debug.Log("Player was judged to have falled");

            if (roundManager != null)
            {
                roundManager.OnPlayerDeath();
            }
            else
            {
                Debug.LogWarning("Can't find roundManager");
            }

            //  óéâ∫ÇµÇΩÉvÉåÉCÉÑÅ[Çè¡ãé
            Destroy(other.gameObject);
        }
    }
}
