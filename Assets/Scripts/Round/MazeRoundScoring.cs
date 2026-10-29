using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mazeラウンドの落下回数を記録し、回数の少ない順に順位ポイントを付けます。
/// 復帰地点はMazeシーンのInspectorで設定します。
/// </summary>
public class MazeRoundScoring : RoundScoring
{
    // 落下処理からInspector設定を参照できるよう、シーン上のこのコンポーネントを保持します。
    private static MazeRoundScoring Instance { get; set; }

    [Header("落下時の復帰地点")]
    [Tooltip("落下したプレイヤーを復帰させる候補。Mazeシーン上のRespawnPointを登録してください。")]
    [SerializeField] private Transform[] respawnPoints;

    // NetworkObjectIdごとに落下した回数を保存します。辞書にないプレイヤーは0回です。
    private static readonly Dictionary<ulong, int> fallsByPlayer = new();

    // MazeシーンのRoundScoringとしてラウンドごとに生成されるため、開始時に記録を初期化します。
    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer)
            fallsByPlayer.Clear();
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>プレイヤーをRespawnPointへ移動し、落下回数を1増やします。</summary>
    public static void HandleFall(PlayerController player)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || player == null)
            return;

        // 復帰地点がない場合は脱落させず、元のセンサーの位置へ戻す安全策を取ります。
        Vector3 position = player.transform.position + Vector3.up * 2f;
        Quaternion rotation = player.transform.rotation;

        // Inspectorで設定された地点から1つ選び、位置と向きをそこから取得します。
        if (Instance != null && Instance.respawnPoints != null)
        {
            // 未設定(null)の項目は候補から除外して、抽選時のエラーを防ぎます。
            var validPoints = new List<Transform>();
            foreach (Transform point in Instance.respawnPoints)
            {
                if (point != null)
                    validPoints.Add(point);
            }

            if (validPoints.Count > 0)
            {
                Transform selectedPoint = validPoints[Random.Range(0, validPoints.Count)];
                position = selectedPoint.position;
                rotation = selectedPoint.rotation;
            }
        }

        fallsByPlayer.TryGetValue(player.NetworkObjectId, out int falls);
        fallsByPlayer[player.NetworkObjectId] = falls + 1;
        player.Teleport(position, rotation);
    }

    /// <summary>ラウンド終了時、落下回数が少ない順に順位ポイントを付けます。</summary>
    public override void AwardPoints()
    {
        if (!IsServer
            || PlayerManager.Instance == null || GameManager.Instance == null
            || ConnectionContainer.Instance == null)
            return;

        var players = new List<PlayerController>(PlayerManager.Instance.AllPlayers);
        players.RemoveAll(player => player == null);
        players.Sort((left, right) =>
        {
            fallsByPlayer.TryGetValue(left.NetworkObjectId, out int leftFalls);
            fallsByPlayer.TryGetValue(right.NetworkObjectId, out int rightFalls);
            int comparison = leftFalls.CompareTo(rightFalls);
            return comparison != 0 ? comparison : left.OwnerClientId.CompareTo(right.OwnerClientId);
        });

        int rank = 1;
        int firstIndex = 0;
        while (firstIndex < players.Count)
        {
            fallsByPlayer.TryGetValue(players[firstIndex].NetworkObjectId, out int firstFalls);
            int nextIndex = firstIndex + 1;

            // 落下回数が同じ人は同順位にし、同じポイントを渡します。
            while (nextIndex < players.Count)
            {
                fallsByPlayer.TryGetValue(players[nextIndex].NetworkObjectId, out int nextFalls);
                if (nextFalls != firstFalls) break;
                nextIndex++;
            }

            int points = GameManager.Instance.GetPointsForRank(rank);
            for (int i = firstIndex; i < nextIndex; i++)
            {
                ConnectionContainer.Instance.GetConnection(players[i].OwnerClientId)?.AddScore(points);
            }

            // 同順位に入った人数だけ次の順位番号を進めます。
            rank += nextIndex - firstIndex;
            firstIndex = nextIndex;
        }

        // 次のMazeラウンドへ落下回数を持ち越さないよう、集計後に消去します。
        fallsByPlayer.Clear();
    }
}
