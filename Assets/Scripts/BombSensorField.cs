using UnityEngine;

public class BombSensorField : MonoBehaviour
{
    [SerializeField] private float loseTime = 5f;

    private BombPushBomb bombPushBomb;
    private PlayerController player;
    private float bombStayTime;
    private bool playerLost;

    void Update()
    {
        if (bombPushBomb == null || player == null || playerLost) return;

        bombStayTime += Time.deltaTime;
        if (bombStayTime >= loseTime)
        {
            playerLost = true;
            LosePlayer();
            bombPushBomb.Explode();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (bombPushBomb == null)
        {
            bombPushBomb = other.GetComponentInParent<BombPushBomb>();
            if (bombPushBomb != null)
            {
                bombStayTime = 0f;
                playerLost = false;
            }
        }

        if (player == null)
        {
            player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                Debug.Log($"Player {player.name} entered the bomb sensor field.");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        BombPushBomb exitingBomb = other.GetComponentInParent<BombPushBomb>();
        if (exitingBomb == bombPushBomb)
        {
            bombPushBomb = null;
            bombStayTime = 0f;
            playerLost = false;
        }

    }

    public void SetPlayerController(PlayerController player)
    {
        this.player = player;
    }

    void LosePlayer()
    {
        if (player == null || PlayerManager.Instance == null) return;

        player.Eliminate();
        PlayerManager.Instance.DropOut(player);
    }
}
