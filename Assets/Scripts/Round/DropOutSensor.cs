using UnityEngine;
using Unity.Netcode;

/// <summary>
/// コース外に落ちたプレイヤーを拾うセンサー。
///
/// セーブポイント（<see cref="SavePoint"/>）を通過済みなら、そこへ戻して続行させる。
/// 記録が無ければ従来どおり脱落。セーブポイントを置いていないステージの動きは変わらない。
/// respawnAtStart を有効にしたステージ（Race など）では、未通過でもスタート地点へ戻し、脱落させない。
///
/// 判定は「そのプレイヤーを動かしている端末（Owner）」で行い、サーバーへ報告する。
/// 理由は <see cref="SavePoint"/> と同じで、サーバー上の他人のプレイヤーには OnTriggerEnter が来ないことがあるため。
/// </summary>
public class DropOutSensor : MonoBehaviour
{
    [Tooltip("セーブポイントから復帰できる回数。負の値なら無制限")]
    [SerializeField] private int respawnLimit = -1;

    [Tooltip("セーブポイント未通過で落ちたとき、脱落させずにスタート地点へ戻す")]
    [SerializeField] private bool respawnAtStart = false;

    void OnTriggerEnter(Collider other)
    {
        // タグ比較が一番安いので先に弾く
        if (!other.CompareTag("Player")) return;

        var network = NetworkManager.Singleton;
        if (network == null || !network.IsListening) return;

        // 結果キャッシュ付きの取得。落下判定は同じ Collider が何度も触れる。
        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        // 判定は動かしている本人の端末だけ。サーバーでも他人のプレイヤーは本人の報告を待つ（二重処理しない）
        if (!player.IsOwner) return;

        if (network.IsServer) Apply(player, this.respawnLimit, this.respawnAtStart);
        else player.ReportDropOutServerRpc(this.respawnLimit, this.respawnAtStart);
    }

    /// <summary>落下をサーバーで処理する。本人の端末からの報告もここに来る。</summary>
    public static void Apply(PlayerController player, int respawnLimit, bool respawnAtStart)
    {
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;

        if (GameManager.Instance == null) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        // 記録があれば、脱落させずに引き戻す
        if (SavePointRegistry.TryConsumeRespawn(player, respawnLimit, respawnAtStart, out Vector3 position, out Quaternion rotation))
        {
            player.Teleport(position, rotation);
            return;
        }

        if (PlayerManager.Instance == null) return;
        player.Eliminate();
        PlayerManager.Instance.DropOut(player);
    }
}
