using UnityEngine;

/// <summary>共有のラウンド時刻を使い、指定順に予告して床を落とす。</summary>
public sealed class SurvivalFloorCollapse : MonoBehaviour
{
    [Tooltip("崩す順番に床を登録。外周・内側を混ぜて指定できます。登録しない床は残ります。")]
    [SerializeField] private Collider[] floors;
    [SerializeField, Min(0f)] private float startTime = 30f;
    [SerializeField, Min(0.1f)] private float interval = 4f;
    [SerializeField, Min(0.1f)] private float warningDuration = 4f;
    [SerializeField, Min(0f)] private float shakeDistance = 0.22f;
    [SerializeField, Min(0.1f)] private float shakeFrequency = 5f;
    [SerializeField, Min(0.1f)] private float fallDuration = 2f;
    [SerializeField, Min(0.1f)] private float fallAcceleration = 12f;

    private Vector3[] initialPositions;
    private Renderer[][] renderers;
    private bool[] initiallyEnabled;
    private bool[][] initiallyVisible;

    private void Awake()
    {
        int count = this.floors == null ? 0 : this.floors.Length;
        this.initialPositions = new Vector3[count];
        this.renderers = new Renderer[count][];
        this.initiallyEnabled = new bool[count];
        this.initiallyVisible = new bool[count][];

        for (int i = 0; i < count; i++)
        {
            if (this.floors[i] == null) continue;
            this.initialPositions[i] = this.floors[i].transform.localPosition;
            this.initiallyEnabled[i] = this.floors[i].enabled;
            this.renderers[i] = this.floors[i].GetComponentsInChildren<Renderer>();
            this.initiallyVisible[i] = new bool[this.renderers[i].Length];
            for (int j = 0; j < this.renderers[i].Length; j++)
                this.initiallyVisible[i][j] = this.renderers[i][j].enabled;
        }
    }

    private void Update()
    {
        RoundManager round = RoundManager.Instance;
        if (round == null || !round.IsSpawned || round.IsRoundOver)
            return;

        float elapsed = round.IsIntro || round.CountdownRemaining > RoundManager.StartDisplayDuration
            ? 0f : Mathf.Max(0f, round.RoundDuration - round.RemainingTime);
        for (int i = 0; i < this.initialPositions.Length; i++)
        {
            Collider floor = this.floors[i];
            if (floor == null) continue;

            float age = elapsed - this.startTime - i * this.interval;
            Vector3 position = this.initialPositions[i];
            if (age >= 0f && age < this.warningDuration)
            {
                float strength = Mathf.Lerp(0.35f, 1f, age / this.warningDuration);
                position.x += Mathf.Sin(age * this.shakeFrequency * Mathf.PI * 2f)
                    * this.shakeDistance * strength;
            }

            float falling = Mathf.Max(0f, age - this.warningDuration);
            position.y -= 0.5f * this.fallAcceleration * Mathf.Min(falling, this.fallDuration)
                * Mathf.Min(falling, this.fallDuration);
            floor.transform.localPosition = position;
            floor.enabled = this.initiallyEnabled[i] && age < this.warningDuration;
            for (int j = 0; j < this.renderers[i].Length; j++)
                if (this.renderers[i][j] != null)
                    this.renderers[i][j].enabled = this.initiallyVisible[i][j] && falling < this.fallDuration;
        }

        // 大砲のLateUpdateで行うRaycastにも現在の床位置を反映する。
        Physics.SyncTransforms();
    }

    private void OnDisable()
    {
        if (this.initialPositions == null) return;
        for (int i = 0; i < this.initialPositions.Length; i++)
        {
            if (this.floors[i] == null) continue;
            this.floors[i].transform.localPosition = this.initialPositions[i];
            this.floors[i].enabled = this.initiallyEnabled[i];
            for (int j = 0; j < this.renderers[i].Length; j++)
                if (this.renderers[i][j] != null)
                    this.renderers[i][j].enabled = this.initiallyVisible[i][j];
        }
    }
}
