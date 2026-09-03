using Unity.Netcode;
using UnityEngine;

/// <summary>
/// コース途中のセーブポイント（チェックポイント）。
/// くぐったプレイヤーの復帰地点を <see cref="SavePointRegistry"/> に記録する。
///
/// 落下したときの扱いは DropOutSensor 側にある。
/// 記録があればそこへ戻し、無ければ従来どおり脱落させる。
/// つまりセーブポイントを置いていないステージの動きは変わらない。
///
/// 記録はサーバーだけが持つ。復帰位置を決めるのはサーバーの仕事で、
/// クライアントは Teleport の ClientRpc を受け取るだけ。
/// </summary>
public class SavePoint : MonoBehaviour
{
    [Tooltip("コース上の順番。戻る方向に通過しても記録が巻き戻らないよう、大きい番号だけ上書きする")]
    [SerializeField] private int order = 0;

    [Tooltip("復帰させる位置。未設定ならこのオブジェクトの位置を使う")]
    [SerializeField] private Transform respawnPoint;

    [Tooltip("respawnPoint 未設定のとき、床にめり込まないよう持ち上げる高さ")]
    [SerializeField] private float spawnHeightOffset = 1f;

    [SerializeField] private bool verboseLog = false;

    /// <summary>コース上の順番。大きいほどゴールに近い。</summary>
    public int Order => this.order;

    public Vector3 RespawnPosition => this.respawnPoint != null
        ? this.respawnPoint.position
        : this.transform.position + Vector3.up * this.spawnHeightOffset;

    public Quaternion RespawnRotation => this.respawnPoint != null
        ? this.respawnPoint.rotation
        : this.transform.rotation;

    private void OnTriggerEnter(Collider other)
    {
        // タグ比較が一番安いので先に弾く
        if (!other.CompareTag("Player")) return;

        // 記録を持つのはサーバーだけ
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;

        if (GameManager.Instance == null) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        // 結果キャッシュ付きの取得。同じ Collider が何度触れても走査は初回だけ。
        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        if (!SavePointRegistry.Record(player, this)) return; // 既に先のセーブポイントを通過済み

        if (this.verboseLog) Debug.Log($"[SavePoint] {player.name} がセーブポイント {this.order} を通過", this);
    }

#if UNITY_EDITOR
    // 判定の範囲と復帰地点をシーンビューで確認できるようにしておく（実行時のコストは無い）。
    private void OnDrawGizmosSelected()
    {
        var box = this.GetComponent<BoxCollider>();
        if (box != null)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.35f);
            Gizmos.matrix = this.transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
        }

        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 1f);
        Gizmos.DrawWireSphere(this.RespawnPosition, 0.5f);
        Gizmos.DrawRay(this.RespawnPosition, this.RespawnRotation * Vector3.forward * 2f);
    }
#endif
}
