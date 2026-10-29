using UnityEngine;

/// <summary>Survivalの大砲を足場の周囲で回す。発射処理は持たない。</summary>
public sealed class SurvivalCannonOrbit : MonoBehaviour
{
    [SerializeField] private Transform center;
    [SerializeField, Min(1f)] private float radius = 24f;
    [SerializeField] private float height = 3f;
    [SerializeField] private float degreesPerSecond = 18f;
    [SerializeField] private float startAngle = 90f;
    [SerializeField] private float modelYawOffset;
    [SerializeField] private bool previewWithoutRound = true;
    [SerializeField, Min(0f)] private float activationTime;

    private float previewElapsed;
    private Renderer[] modelRenderers;

    public float ActivationTime => this.activationTime;

    private void Awake()
    {
        this.modelRenderers = GetComponentsInChildren<Renderer>();
        this.SetVisible(this.activationTime <= 0f);
    }

    private void SetVisible(bool visible)
    {
        foreach (Renderer model in this.modelRenderers)
            if (model != null) model.enabled = visible;
    }

    private void Start()
    {
        this.ApplyPose(0f);
    }

    private void Update()
    {
        RoundManager round = RoundManager.Instance;
        if (round != null && round.IsSpawned)
        {
            if (round.IsRoundOver)
                return;

            // 同期済みの残り時間から算出。端末ごとの積算によるずれを避ける。
            float elapsed = round.IsIntro || round.CountdownRemaining > RoundManager.StartDisplayDuration
                ? 0f : Mathf.Max(0f, round.RoundDuration - round.RemainingTime);
            this.ApplyPose(elapsed);
            this.SetVisible(elapsed >= this.activationTime);
            return;
        }

        // Survival単独のPlayでも配置と周回を確認できる。
        if (this.previewWithoutRound)
        {
            this.previewElapsed += Time.deltaTime;
            this.ApplyPose(this.previewElapsed);
            this.SetVisible(this.previewElapsed >= this.activationTime);
        }
    }

    private void ApplyPose(float elapsed)
    {
        if (this.center == null)
            return;

        float angle = (this.startAngle + elapsed * this.degreesPerSecond) * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        this.transform.position = this.center.position + outward * this.radius + Vector3.up * this.height;
        this.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up)
            * Quaternion.Euler(0f, this.modelYawOffset, 0f);
    }
}
