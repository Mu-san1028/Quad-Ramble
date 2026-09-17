using UnityEngine;

public class BombSensorField : MonoBehaviour
{
    BombPushBomb bombPushBomb;
    private PlayerController player;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter(Collider other)
    {
        
    }
    void OnTriggerExit(Collider other)
    {

    }

    public void SetPlayerController(PlayerController player)
    {
        this.player = player;
    }

    void LosePlayer()
    {
        PlayerManager.Instance.DropOut(player);
        player.Eliminate();
    }
}
