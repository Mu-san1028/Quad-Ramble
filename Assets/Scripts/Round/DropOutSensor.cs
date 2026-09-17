using UnityEngine;
using Unity.Netcode;

/// <summary>
/// コース外に落ちたプレイヤーを拾うセンサー。
///
/// セーブポイント（<see cref="SavePoint"/>）を通過済みなら、そこへ戻して続行させる。
/// 記録が無ければ従来どおり脱落。セーブポイントを置いていないステージの動きは変わらない。
/// </summary>
public class DropOutSensor : MonoBehaviour
{
    [Tooltip("セーブポイントから復帰できる回数。負の値なら無制限")]
    [SerializeField] private int respawnLimit = -1;

    void OnTriggerEnter(Collider other)
    {
        // タグ比較が一番安いので先に弾く
        if (!other.CompareTag("Player")) return;

        // サーバーのみがこの処理を行う
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;

        if (GameManager.Instance == null) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        // 結果キャッシュ付きの取得。落下判定は同じ Collider が何度も触れる。
        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        // セーブポイントの記録があれば、脱落させずに引き戻す
        if (SavePointRegistry.TryConsumeRespawn(player, this.respawnLimit, out Vector3 position, out Quaternion rotation))
        {
            player.Teleport(position, rotation);
            return;
        }

        if (PlayerManager.Instance == null) return;
        PlayerManager.Instance.DropOut(player);
        player.Eliminate();
    }
}
