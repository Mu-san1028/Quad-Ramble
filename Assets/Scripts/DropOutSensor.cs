using System.Runtime.CompilerServices;
using UnityEngine;

public class DropOutSensor : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
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
    }
}
