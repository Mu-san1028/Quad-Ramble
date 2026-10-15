using UnityEngine;

/// <summary>
/// 乗ったプレイヤーを台の向き（forward）へ押し出す加速台。
///
/// 判定は JumpPad と同じく「そのプレイヤーを動かしている端末（Owner）」で行う。
/// 押し出しは PlayerController.Boost でノックバック用の衝撃に加えるので、
/// 入力による移動に上乗せされ、時間で自然に減衰する。
/// </summary>
public class BoostPad : MonoBehaviour, IStageTimerTarget
{
    [Tooltip("押し出す初速。進む距離 ≒ 初速 / 5（ノックバックの減衰係数）")]
    [SerializeField] private float boostSpeed = 30f;

    [Tooltip("同じ加速台が続けて発動しないための待ち時間（秒）")]
    [SerializeField] private float cooldownSeconds = 0.5f;

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
        if (!other.CompareTag("Player")) return;

        PlayerController player = StageCache<PlayerController>.Get(other);
        if (player == null) return;

        // 他人のプレイヤーは本人の端末に任せる（オフライン単体再生では Spawn されていないので通す）
        if (player.IsSpawned && !player.IsOwner) return;

        player.Boost(this.transform.forward * this.boostSpeed);

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
    // 押し出す向きと大まかな距離をシーンビューで確認できるようにしておく
    private void OnDrawGizmosSelected()
    {
        Vector3 from = this.transform.position + Vector3.up * 0.5f;
        Vector3 to = from + this.transform.forward * (this.boostSpeed / 5f);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        Gizmos.DrawLine(from, to);
        Gizmos.DrawWireSphere(to, 0.5f);
    }
#endif
}
