using UnityEngine;

/// <summary>
/// 振り子のように往復するギミック。
///
/// 旧実装からの変更点:
/// - 自前の Update をやめ、StageDirector の一括ループから呼ばれるようにした。
/// - initialRotation を取っていたのに使っておらず、localRotation を毎フレーム絶対値で
///   上書きしていたため、配置時に付けた他軸の回転が起動と同時に消えていた。
///   初期姿勢を基準にした相対回転に直した。
/// - 毎フレームの switch をやめ、軸ベクトルは起動時に 1 度だけ決める。
/// - Time.time 基準だと全ての振り子が同位相で揃ってしまうので、位相のずらしを足せるようにした。
/// </summary>
public class Pendulum : MonoBehaviour, IStageTick
{
    [SerializeField] private float maxSwingAngle = 30f; // 最大振れ角度
    [SerializeField] private float swingSpeed = 30f;    // 振れ速度

    public enum SwingDirection
    {
        Clockwise,
        CounterClockwise
    }
    [SerializeField] private SwingDirection swingDirection = SwingDirection.Clockwise;

    public enum SwingAxis
    {
        XAxis,
        YAxis,
        ZAxis
    }
    [SerializeField] private SwingAxis swingAxis = SwingAxis.YAxis;

    [Tooltip("同じ設定の振り子を並べたときに動きを揃えないためのずらし（秒）")]
    [SerializeField] private float phaseOffset = 0f;

    private Transform selfTransform;
    private Quaternion initialLocalRotation; // 配置時の姿勢。ここを基準に振る。
    private Vector3 axisVector;              // 起動時に確定させる
    private float signedAmplitude;           // maxSwingAngle に回転方向の符号を織り込んだもの
    private float elapsed;

    private void Awake()
    {
        this.selfTransform = this.transform;
        this.initialLocalRotation = this.selfTransform.localRotation;
        this.CacheSwingSettings();
        this.elapsed = this.phaseOffset;
    }

    private void OnEnable() => StageDirector.Register(this);
    private void OnDisable() => StageDirector.Unregister(this);

    private void CacheSwingSettings()
    {
        switch (this.swingAxis)
        {
            case SwingAxis.XAxis: this.axisVector = Vector3.right; break;
            case SwingAxis.ZAxis: this.axisVector = Vector3.forward; break;
            default:              this.axisVector = Vector3.up; break;
        }

        this.signedAmplitude = this.swingDirection == SwingDirection.CounterClockwise
            ? -this.maxSwingAngle
            : this.maxSwingAngle;
    }

    public void Tick(float deltaTime)
    {
        this.elapsed += deltaTime;

        float angle = Mathf.Sin(this.elapsed * this.swingSpeed) * this.signedAmplitude;

        // 初期姿勢 × 振れ角。絶対値で上書きしないので、配置時の傾きが保たれる。
        this.selfTransform.localRotation =
            this.initialLocalRotation * Quaternion.AngleAxis(angle, this.axisVector);
    }

    /// <summary>実行中に振れ幅・速度を変えたい場合はこちらを使う。</summary>
    public void SetSwing(float newMaxAngle, float newSpeed)
    {
        this.maxSwingAngle = newMaxAngle;
        this.swingSpeed = newSpeed;
        this.CacheSwingSettings();
    }

#if UNITY_EDITOR
    // Inspector で値をいじったときにキャッシュを追従させる（エディタ専用）
    private void OnValidate()
    {
        if (Application.isPlaying) this.CacheSwingSettings();
    }
#endif
}
