using UnityEngine;

/// <summary>
/// 遠景を親のローカル座標で一方向へ流す。開始位置から一定距離で折り返さずループする。
/// 更新はStageDirectorに委ね、ネットワーク同期は行わない。
/// </summary>
[DisallowMultipleComponent]
public sealed class BackgroundDrifter : MonoBehaviour, IStageTick
{
    [SerializeField] private Vector3 direction = Vector3.right;
    [SerializeField] private float speed = 8f;
    [SerializeField] private float loopDistance = 400f;
    [Tooltip("開始時刻のずらし（秒）。距離へ変換してループ範囲内に収める。")]
    [SerializeField] private float phaseOffset = 0f;
    [Tooltip("このTransformのローカル+Zを進行方向へ向ける。モデルの軸補正は子で行う。")]
    [SerializeField] private bool faceTravelDirection = false;

    private Transform selfTransform;
    private Vector3 startPosition;
    private Vector3 travelDirection;
    private float distance;
    private bool canTravel;

    private void Awake()
    {
        // Transformへのネイティブアクセスを毎フレーム繰り返さない。
        this.selfTransform = this.transform;
        this.startPosition = this.selfTransform.localPosition;
        this.travelDirection = this.direction.normalized;
        this.canTravel = this.loopDistance > 0f && this.travelDirection.sqrMagnitude > 0f;
        if (!this.canTravel)
        {
            this.enabled = false;
            return;
        }

        this.distance = Mathf.Repeat(this.phaseOffset * this.speed, this.loopDistance);
        this.selfTransform.localPosition = this.startPosition + this.travelDirection * this.distance;
        if (this.faceTravelDirection && this.speed != 0f)
        {
            Vector3 forward = this.travelDirection * Mathf.Sign(this.speed);
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            this.selfTransform.localRotation = Quaternion.LookRotation(forward, up);
        }
    }

    private void OnEnable() => StageDirector.Register(this);
    private void OnDisable() => StageDirector.Unregister(this);

    /// <summary>親座標で移動する。部屋のルートを回転・拡縮しても飛行経路が追従する。</summary>
    public void Tick(float deltaTime)
    {
        if (!this.canTravel || this.loopDistance <= 0f) return;
        this.distance = Mathf.Repeat(this.distance + this.speed * deltaTime, this.loopDistance);
        this.selfTransform.localPosition = this.startPosition + this.travelDirection * this.distance;
    }
}
