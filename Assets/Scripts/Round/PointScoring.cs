using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PointScoring : RoundScoring
{
    public override void AwardPoints()
    {
        if (!IsServer) return;

        foreach (var player in PlayerManager.Instance.AllPlayers)
        {
            var status = player.GetComponent<PlayerStatus>();
            if (status == null) continue;

            var connection = ConnectionContainer.Instance.GetConnectionByPlayerId(player.ParticipantId);
            connection?.AddScore(status.RoundScore);
        }
    }
}
