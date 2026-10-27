using UnityEngine;
using Unity.Netcode;

public class BombSensorField : MonoBehaviour
{
    [SerializeField] private float loseTime = 5f;
    [SerializeField] private int rankDropPerExplosion = 1; // 爆発1回で順位を下げる段数

    private BombPushBomb bombPushBomb;
    private PlayerController territoryOwner;
    private float bombStayTime;
    private bool penaltyApplied;

    void Update()
    {
        // タイマーとペナルティはサーバーだけで確定する。
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (bombPushBomb == null || territoryOwner == null || penaltyApplied) return;

        bombStayTime += Time.deltaTime;
        if (bombStayTime >= loseTime)
        {
            penaltyApplied = true;
            ApplyTerritoryPenalty();
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
                penaltyApplied = false;
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
            penaltyApplied = false;
        }

    }

    // PlayerSpawnerがラウンド開始時に、その陣地に対応するプレイヤーを割り当てる。
    public void SetPlayerController(PlayerController player)
    {
        this.territoryOwner = player;
    }

    private void ApplyTerritoryPenalty()
    {
        if (territoryOwner == null) return;

        // 爆発のたびに順位低下回数を記録し、終了時の配点に反映する。
        BombPushRoundScoring.AddRankDrop(territoryOwner, rankDropPerExplosion);
    }
}
