using UnityEngine;

/// <summary>
/// 触れたプレイヤーを弾き飛ばすギミック。
///
/// 旧実装からの変更点:
/// - 中身が空の Update を削除した。空でも MonoBehaviour ごとに毎フレーム呼ばれるので、
///   ステージに何十個も置くとそれだけで固定費になる。
/// - GetComponent を Awake の 1 回に減らした（旧: 接触のたびに Rigidbody を取得）。
/// - 弾き先を Rigidbody から PlayerController.TakeKnockback に変えた。
///   PlayerController は CharacterController で動いており Rigidbody を持たないため、
///   旧実装は Rigidbody が見つからず何も起きていなかった。
/// - 速度は Collision.relativeVelocity から取る。こちらは物理エンジンが既に計算済みの値で、
///   取得にコストがかからないうえ、実際の衝突の激しさを正しく反映する。
/// </summary>
public class Bounce : MonoBehaviour, IStageTimerTarget
{
    [SerializeField] private float bounceRatio = 1f;

    [Tooltip("Trigger 判定のときは衝突速度が取れないので、この速度で当たったものとして扱う")]
    [SerializeField] private float triggerAssumedSpeed = 8f;

    [Tooltip("同じプレイヤーを続けて弾かないための待ち時間（秒）")]
    [SerializeField] private float cooldownSeconds = 0.15f;

    private enum collisionType { Trigger, Collision }
    private collisionType collisionDetection = collisionType.Collision;

    private Transform selfTransform;
    private bool onCooldown;

    private const int CooldownToken = 0;

    private void Awake()
    {
        this.selfTransform = this.transform;

        // Collider の isTrigger を見て、Trigger 判定と Collision 判定のどちらを使うか決める。
        Collider col = this.GetComponent<Collider>();
        this.collisionDetection = (col != null && col.isTrigger)
            ? collisionType.Trigger
            : collisionType.Collision;
    }

    private void OnDisable()
    {
        StageDirector.CancelTimers(this);
        this.onCooldown = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (this.collisionDetection != collisionType.Trigger) return;
        if (this.onCooldown) return;
        if (!other.CompareTag("Player")) return;

        this.ApplyBounce(other, other.transform.position, this.triggerAssumedSpeed);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (this.collisionDetection != collisionType.Collision) return;
        if (this.onCooldown) return;
        if (!collision.gameObject.CompareTag("Player")) return;

        // relativeVelocity は物理エンジンが衝突解決時に持っている値。追加計算が要らない。
        this.ApplyBounce(collision.collider, collision.transform.position, collision.relativeVelocity.magnitude);
    }

    private void ApplyBounce(Collider target, Vector3 targetPosition, float impactSpeed)
    {
        // 結果キャッシュ付きの取得。同じ Collider が何度触れても走査は初回だけ。
        PlayerController player = StageCache<PlayerController>.Get(target);
        if (player == null) return;

        // 中心から外向きへ。真上に重なっている場合は上方向に逃がす。
        Vector3 offset = targetPosition - this.selfTransform.position;
        Vector3 bounceDirection = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.up;

        float bounceForce = impactSpeed * this.bounceRatio;
        player.TakeKnockback(bounceDirection * bounceForce);

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
}
