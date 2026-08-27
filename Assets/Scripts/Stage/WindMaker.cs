using UnityEngine;

/// <summary>
/// 風の範囲。Trigger の Collider と一緒に置く。
/// 実際にプレイヤーを押すのは WindReceiver 側（プレイヤーに付ける）。
///
/// 旧実装からの変更点:
/// - コメントが文字化けしていたので書き直した（Shift-JIS のまま保存されていた）。
/// - 毎フレーム windDirection.normalized を計算していたのをやめ、
///   「向き × 強さ」を 1 本のベクトルとして持つようにした。
///   normalized は平方根を含むので、風の中にいる人数 × 風の数だけ毎物理フレームに効いてくる。
/// </summary>
public class WindArea : MonoBehaviour
{
    [Tooltip("風が吹く向き。正規化されていなくてよい。")]
    public Vector3 windDirection = Vector3.forward;

    [Tooltip("風が押し出す強さ")]
    public float force = 5f;

    // 「向き（正規化済み） × 強さ」。値が変わったときだけ作り直す。
    private Vector3 cachedAcceleration;
    private Vector3 lastDirection;
    private float lastForce = float.NaN;

    /// <summary>1 秒あたりの加速度。WindReceiver はこれに fixedDeltaTime を掛けて使う。</summary>
    public Vector3 Acceleration
    {
        get
        {
            // public フィールドなので外部から直接書き換えられる。値の変化を見て作り直す。
            if (this.windDirection != this.lastDirection || this.force != this.lastForce)
            {
                this.lastDirection = this.windDirection;
                this.lastForce = this.force;

                Vector3 dir = this.windDirection;
                float sqr = dir.sqrMagnitude;
                this.cachedAcceleration = sqr > 0.000001f
                    ? dir / Mathf.Sqrt(sqr) * this.force
                    : Vector3.zero;
            }

            return this.cachedAcceleration;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 from = this.transform.position;
        Gizmos.DrawLine(from, from + this.Acceleration * 0.2f);
    }
#endif
}
