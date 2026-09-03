using UnityEngine;

/// <summary>
/// 回転する床・障害物。
///
/// 旧実装からの変更点:
/// - 自前の Update をやめ、StageDirector の一括ループから呼ばれるようにした。
/// - rotationAxis が宣言されているのに使われていなかったので、実際に軸として使うようにした。
/// - 速度を徐々に変える処理を Coroutine から状態変数に置き換えた（呼ぶたびのヒープ確保をなくすため）。
///   旧実装は SetRotationSpeedGradually を連打すると Coroutine が多重起動して速度が競合していた。
/// </summary>
public class Rotation : MonoBehaviour, IStageTick
{
    [SerializeField] private float rotationSpeed = 100f;

    [Tooltip("この Transform の up を軸に、その位置を中心に回る。未設定なら自分自身の up。")]
    [SerializeField] private Transform rotationAxis;

    // 速度の緩やかな変化（旧: RotateGradually コルーチン）
    private float speedFrom;
    private float speedTo;
    private float speedLerpElapsed;
    private float speedLerpDuration; // 0 なら補間中でない

    private Transform selfTransform;

    public float CurrentSpeed => this.rotationSpeed;

    private void Awake()
    {
        // Transform へのアクセスもネイティブ呼び出しなので、参照は 1 度だけ取る。
        this.selfTransform = this.transform;
    }

    private void OnEnable() => StageDirector.Register(this);
    private void OnDisable() => StageDirector.Unregister(this);

    public void Tick(float deltaTime)
    {
        // --- 速度の補間 ---
        if (this.speedLerpDuration > 0f)
        {
            this.speedLerpElapsed += deltaTime;
            float t = this.speedLerpElapsed / this.speedLerpDuration;

            if (t >= 1f)
            {
                this.rotationSpeed = this.speedTo;
                this.speedLerpDuration = 0f;
            }
            else
            {
                this.rotationSpeed = Mathf.Lerp(this.speedFrom, this.speedTo, t);
            }
        }

        // --- 回転 ---
        float angle = this.rotationSpeed * deltaTime;
        if (angle == 0f) return;

        if (this.rotationAxis != null && this.rotationAxis != this.selfTransform)
        {
            // 別 Transform を軸にする場合は、その位置を中心に公転させる。
            this.selfTransform.RotateAround(this.rotationAxis.position, this.rotationAxis.up, angle);
        }
        else
        {
            this.selfTransform.Rotate(Vector3.up, angle, Space.Self);
        }
    }

    /// <summary>即座に回転速度を設定する。補間中なら打ち切る。</summary>
    public void SetRotationSpeedInstant(float speed)
    {
        this.speedLerpDuration = 0f;
        this.rotationSpeed = speed;
    }

    /// <summary>duration 秒かけて targetSpeed まで回転速度を変える。</summary>
    public void SetRotationSpeedGradually(float targetSpeed, float duration)
    {
        if (duration <= 0f)
        {
            this.SetRotationSpeedInstant(targetSpeed);
            return;
        }

        this.speedFrom = this.rotationSpeed;
        this.speedTo = targetSpeed;
        this.speedLerpElapsed = 0f;
        this.speedLerpDuration = duration;
    }

    public void SetRotationAxis(Transform axis)
    {
        this.rotationAxis = axis;
    }
}
