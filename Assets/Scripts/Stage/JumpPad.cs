using UnityEngine;

/// <summary>
/// 乗ったプレイヤーを真上に打ち上げるジャンプ台。空中の足場へ上がるための入口。
///
/// 判定は「そのプレイヤーを動かしている端末（Owner）」で行う。
/// 移動は Owner の CharacterController.Move で決まるため、打ち上げもそこで速度を書き換えれば
/// NetworkTransform 経由で他の端末にも反映される（SavePoint と同じ考え方）。
///
/// Bounce は TakeKnockback（すぐ減衰する衝撃）を使うので高さが出ない。
/// こちらは重力が効く縦速度そのものを上書きするので、狙った高さまで確実に届く。
/// </summary>
public class JumpPad : MonoBehaviour, IStageTimerTarget
{
    [Tooltip("打ち上げる縦速度。到達高さ ≒ 速度² / (2 × 9.8)")]
    [SerializeField] private float launchSpeed = 13f;

    [Tooltip("同じジャンプ台が続けて発動しないための待ち時間（秒）")]
    [SerializeField] private float cooldownSeconds = 0.3f;

    private bool onCooldown;

    private const int CooldownToken = 0;

    private void OnDisable()
    {
        StageDirector.CancelTimers(this);
        this.onCooldown = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (this.onCooldown) return;
        // タグ比較が一番安いので先に弾く
        if (!other.CompareTag("Player")) return;

        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        // 他人のプレイヤーは本人の端末に任せる（オフライン単体再生では Spawn されていないので通す）
        if (player.IsSpawned && !player.IsOwner) return;

        player.Launch(this.launchSpeed);

        if (this.cooldownSeconds > 0f)
        {
            this.onCooldown = true;
            StageDirector.Schedule(this, this.cooldownSeconds, CooldownToken);
        }
    }

    public void OnStageTimer(int token)
    {
        if (token == CooldownToken) this.onCooldown = false;
    }

#if UNITY_EDITOR
    // 打ち上げの到達高さをシーンビューで確認できるようにしておく
    private void OnDrawGizmosSelected()
    {
        float apex = this.launchSpeed * this.launchSpeed / (2f * 9.8f);
        Vector3 from = this.transform.position;
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);
        Gizmos.DrawLine(from, from + Vector3.up * apex);
        Gizmos.DrawWireSphere(from + Vector3.up * apex, 0.5f);
    }
#endif
}
