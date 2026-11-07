using System.Collections.Generic;
using Unity.Netcode;

/// <summary>
/// BombPush専用の順位配点と爆発ペナルティを管理する。
/// 共通のPlayerStatusやRoundManagerを変更せず、このラウンドだけで配点を調整する。
/// </summary>
public class BombPushRoundScoring : RoundScoring
{
    // サーバー上でのみ使う。キーはプレイヤーのNetworkObjectId、値は順位を下げる回数。
    private static readonly Dictionary<ulong, int> rankDropsByPlayer = new();

    public override void OnNetworkSpawn()
    {
        // 前ラウンドの値が残らないよう、このシーンのサーバー起動時に初期化する。
        if (IsServer)
            rankDropsByPlayer.Clear();
    }

    /// <summary>爆発した陣地のプレイヤーに、順位を下げる回数をサーバー側で記録する。</summary>
    public static void AddRankDrop(PlayerController player, int amount)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || player == null)
            return;

        ulong playerId = player.NetworkObjectId;
        int rankDrop = System.Math.Abs(amount);
        rankDropsByPlayer.TryGetValue(playerId, out int currentRankDrop);
        rankDropsByPlayer[playerId] = currentRankDrop + rankDrop;
    }

    /// <summary>RoundManagerから呼ばれ、爆発回数が少ない順に順位を決めて配点する。</summary>
    public override void AwardPoints()
    {
        if (!IsServer || PlayerManager.Instance == null || GameManager.Instance == null
            || ConnectionContainer.Instance == null)
            return;

        // 爆発回数が少ないプレイヤーを先の順位にする。
        // 同じ回数の場合は同順位にするため、順序はClientIdで安定させておく。
        var players = new List<PlayerController>(PlayerManager.Instance.AllPlayers);
        players.RemoveAll(player => player == null);
        players.Sort((left, right) =>
        {
            rankDropsByPlayer.TryGetValue(left.NetworkObjectId, out int leftDrops);
            rankDropsByPlayer.TryGetValue(right.NetworkObjectId, out int rightDrops);
            int byExplosionCount = leftDrops.CompareTo(rightDrops);
            return byExplosionCount != 0
                ? byExplosionCount
                : left.ParticipantId.CompareTo(right.ParticipantId);
        });

        int rank = 1;

        int firstPlayerIndex = 0;
        while (firstPlayerIndex < players.Count)
        {
            PlayerController firstPlayer = players[firstPlayerIndex];
            rankDropsByPlayer.TryGetValue(firstPlayer.NetworkObjectId, out int firstPlayerDrops);

            // 爆発回数が同じプレイヤーは同じ順位・同じRank Pointsを受け取る。
            int nextRankIndex = firstPlayerIndex + 1;
            while (nextRankIndex < players.Count)
            {
                rankDropsByPlayer.TryGetValue(players[nextRankIndex].NetworkObjectId, out int nextPlayerDrops);
                if (nextPlayerDrops != firstPlayerDrops) break;
                nextRankIndex++;
            }

            int awardedPoints = GameManager.Instance.GetPointsForRank(rank);
            for (int playerIndex = firstPlayerIndex; playerIndex < nextRankIndex; playerIndex++)
            {
                PlayerController player = players[playerIndex];
                ConnectionContainer.Instance.GetConnectionByPlayerId(player.ParticipantId)?.AddScore(awardedPoints);
            }

            // 同順位の人数分、次に使う順位を進める。
            rank += nextRankIndex - firstPlayerIndex;
            firstPlayerIndex = nextRankIndex;
        }

        // 次のラウンドで順位低下回数を持ち越さない。
        rankDropsByPlayer.Clear();
    }
}
