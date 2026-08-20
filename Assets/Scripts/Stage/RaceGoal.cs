using UnityEngine;
using Unity.Netcode;

/// <summary>
/// ゴールの門に置くトリガー。くぐったプレイヤーをゴール順に記録する。
/// </summary>
public class RaceGoal : MonoBehaviour
{
    [Tooltip("1人目がゴールした時点でラウンドを終わらせる")]
    [SerializeField] private bool endRoundOnFirstFinish = false;

    void OnTriggerEnter(Collider other)
    {
        if (!NetworkManager.Singleton.IsServer) return; // サーバーのみがこの処理を行う
        if (!other.CompareTag("Player")) return;
        if (GameManager.Instance.currentState != GameState.Playing) return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;
        if (!PlayerManager.Instance.Finish(player)) return; // 既にゴール済み・脱落済み

        // ゴールしたプレイヤーはその場で動きを止める
        player.StopOrMovePlayer(false);

        if (this.endRoundOnFirstFinish) RoundManager.Instance?.EndRound();
    }
}
