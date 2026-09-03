using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゴールの門に置くトリガー。くぐったプレイヤーをゴール順に記録する。
///
/// 旧実装からの変更点:
/// - GetComponent&lt;PlayerController&gt;() を StageCache 経由にした。
///   ゴール門はプレイヤー以外の物も通るので、空振りの走査が減る。
/// - NetworkManager.Singleton / GameManager.Instance の null チェックを足した。
///   オフラインでシーンを単体再生したときに例外で落ちていた。
/// - 判定の順序を、軽い条件から順に並べ替えた（タグ比較 → 状態確認 → コンポーネント取得）。
/// </summary>
public class RaceGoal : MonoBehaviour
{
    [Tooltip("1人目がゴールした時点でラウンドを終わらせる")]
    [SerializeField] private bool endRoundOnFirstFinish = false;

    private void OnTriggerEnter(Collider other)
    {
        // タグ比較が一番安いので先に弾く
        if (!other.CompareTag("Player")) return;

        // サーバーのみがゴール判定を行う
        var network = NetworkManager.Singleton;
        if (network == null || !network.IsServer) return;

        if (GameManager.Instance == null) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        if (PlayerManager.Instance == null) return;
        if (!PlayerManager.Instance.Finish(player)) return; // 既にゴール済み・脱落済み

        // ゴールしたプレイヤーはその場で動きを止める
        player.StopOrMovePlayer(false);

        if (this.endRoundOnFirstFinish) RoundManager.Instance?.EndRound();
    }
}
