using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>Survival専用。ホストが弾道・命中を決め、各端末で既存Bombを表示する。</summary>
public sealed class SurvivalCannonFire : NetworkBehaviour
{
    [SerializeField] private SurvivalCannonOrbit[] cannons;
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private WeaponData bombData;
    [SerializeField] private SurvivalBlockLauncher blockLauncher;
    [SerializeField, Range(0f, 1f)] private float blockChance = 0.2f;
    [SerializeField] private Collider[] floors;
    [SerializeField] private Vector3 muzzleOffset = new Vector3(0f, 0f, 3.5f);
    [SerializeField, Min(0.5f)] private float flightTime = 2.5f;
    [SerializeField, Min(0.2f)] private float fireInterval = 3f;
    [SerializeField, Min(0.2f)] private float finalFireInterval = 1.6f;
    [SerializeField, Min(1f)] private float accelerationDuration = 60f;
    [SerializeField, Range(0f, 1f)] private float centerTargetChance = 0.35f;
    [SerializeField, Min(0f)] private float firstShotDelay = 2f;
    [SerializeField, Min(0.5f)] private float edgeMargin = 1.5f;
    [SerializeField, Min(0.05f)] private float collisionRadius = 0.5f;
    [SerializeField] private LayerMask groundLayers = 1 << 7;
    [SerializeField] private LayerMask playerLayers = 1 << 6;

    private sealed class Shot
    {
        public int id;
        public Vector3 origin;
        public Vector3 velocity;
        public Vector3 previous;
        public double launchedAt;
        public float gravity;
        public GameObject visual;

        public Vector3 Position(double now)
        {
            float t = Mathf.Max(0f, (float)(now - launchedAt));
            return origin + velocity * t + Vector3.down * (0.5f * gravity * t * t);
        }
    }

    private readonly List<Shot> shots = new();
    private int nextId;
    private double[] nextFireAt;
    private readonly List<Collider> targetFloors = new();
    private readonly List<Collider> centralFloors = new();

    private bool Playing => RoundManager.Instance != null && RoundManager.Instance.IsSpawned
        && !RoundManager.Instance.IsIntro && !RoundManager.Instance.IsRoundOver
        && RoundManager.Instance.CountdownRemaining <= RoundManager.StartDisplayDuration;

    public override void OnNetworkSpawn()
    {
        this.ClearShots();
        this.nextFireAt = new double[this.cannons == null ? 0 : this.cannons.Length];
        this.ResetFireTimes();
        this.nextId = 0;
    }

    private void LateUpdate()
    {
        if (!IsSpawned)
            return;

        if (!this.Playing)
        {
            this.ClearShots();
            this.ResetFireTimes();
            return;
        }

        double now = NetworkManager.ServerTime.Time;
        if (IsServer)
        {
            float elapsed = RoundManager.Instance.RoundDuration - RoundManager.Instance.RemainingTime;
            float interval = Mathf.Lerp(this.fireInterval, this.finalFireInterval,
                Mathf.Clamp01(elapsed / Mathf.Max(1f, this.accelerationDuration)));
            for (int c = 0; c < this.nextFireAt.Length; c++)
            {
                SurvivalCannonOrbit cannon = this.cannons[c];
                if (cannon == null || elapsed < cannon.ActivationTime) continue;

                if (this.nextFireAt[c] < 0d)
                    this.nextFireAt[c] = now + this.firstShotDelay + c * 0.35f;

                if (now >= this.nextFireAt[c])
                {
                    this.nextFireAt[c] = now + Mathf.Max(0.2f, interval);
                    this.Fire(cannon.transform, now);
                }
            }
        }

        for (int i = this.shots.Count - 1; i >= 0; i--)
        {
            Shot shot = this.shots[i];
            Vector3 position = shot.Position(now);
            if (IsServer)
            {
                Vector3 delta = position - shot.previous;
                if (delta.sqrMagnitude > 0.000001f
                    && Physics.SphereCast(shot.previous, this.collisionRadius, delta.normalized,
                        out RaycastHit hit, delta.magnitude, this.groundLayers | this.playerLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    Vector3 impact = shot.previous + delta.normalized * hit.distance;
                    this.Explode(shot.id, impact);
                    continue;
                }

                // 床が削除された場合などにも弾を残し続けない。
                if (now - shot.launchedAt >= this.flightTime + 2f)
                {
                    this.Explode(shot.id, position);
                    continue;
                }
            }

            shot.previous = position;
            if (shot.visual != null)
                shot.visual.transform.position = position;
        }
    }

    private void ResetFireTimes()
    {
        if (this.nextFireAt == null) return;
        for (int i = 0; i < this.nextFireAt.Length; i++) this.nextFireAt[i] = -1d;
    }

    private void Fire(Transform cannon, double now)
    {
        if (cannon == null || this.bombPrefab == null || this.bombData == null
            || this.floors == null || this.floors.Length == 0)
            return;

        // 実際の床Colliderから範囲を取る。床のサイズ変更にも追従する。
        Bounds bounds = default;
        bool found = false;
        this.targetFloors.Clear();
        this.centralFloors.Clear();
        foreach (Collider floor in this.floors)
        {
            if (floor == null || !floor.enabled || !floor.gameObject.activeInHierarchy)
                continue;

            if (!found) bounds = floor.bounds;
            else bounds.Encapsulate(floor.bounds);
            found = true;
            this.targetFloors.Add(floor);
        }

        if (!found) return;

        // 中央の床を優先する抽選を混ぜ、中央にも定期的に危険が来るようにする。
        float centerRadius = Mathf.Min(bounds.size.x, bounds.size.z) * 0.22f;
        foreach (Collider floor in this.targetFloors)
        {
            Vector3 offset = floor.bounds.center - bounds.center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= centerRadius * centerRadius)
                this.centralFloors.Add(floor);
        }

        List<Collider> candidates = this.centralFloors.Count > 0 && Random.value < this.centerTargetChance
            ? this.centralFloors : this.targetFloors;
        Collider chosen = candidates[Random.Range(0, candidates.Count)];
        Bounds tile = chosen.bounds;
        float marginX = Mathf.Min(this.edgeMargin, tile.extents.x * 0.8f);
        float marginZ = Mathf.Min(this.edgeMargin, tile.extents.z * 0.8f);
        Vector3 target = new Vector3(
            Random.Range(tile.min.x + marginX, tile.max.x - marginX),
            tile.max.y + 1f,
            Random.Range(tile.min.z + marginZ, tile.max.z - marginZ));

        // 範囲内でも実際に床がない地点には発射しない。
        if (!chosen.Raycast(new Ray(target, Vector3.down), out RaycastHit ground, tile.size.y + 2f))
            return;

        target = ground.point + Vector3.up * this.collisionRadius;
        Vector3 origin = cannon.TransformPoint(this.muzzleOffset);
        float gravity = Mathf.Max(0.01f, this.bombData.gravityScale);
        float duration = Mathf.Max(0.5f, this.flightTime);
        if (this.blockLauncher != null && Random.value < this.blockChance
            && this.blockLauncher.Launch(origin, target, duration))
            return;

        Vector3 velocity = (target - origin) / duration + Vector3.up * (0.5f * gravity * duration);
        this.ShowShotRpc(++this.nextId, origin, velocity, gravity, now);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ShowShotRpc(int id, Vector3 origin, Vector3 velocity, float gravity, double launchedAt)
    {
        GameObject visual = Instantiate(this.bombPrefab, origin, Quaternion.identity);
        // Prefabは編集せず、表示用インスタンスだけ物理から除外する。
        foreach (Collider col in visual.GetComponentsInChildren<Collider>())
            col.enabled = false;

        foreach (Rigidbody body in visual.GetComponentsInChildren<Rigidbody>())
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        this.shots.Add(new Shot
        {
            id = id, origin = origin, velocity = velocity, previous = origin,
            gravity = gravity, launchedAt = launchedAt, visual = visual
        });
    }

    private void Explode(int id, Vector3 position)
    {
        this.ExplodeRpc(id, position);
        if (PlayerManager.Instance == null)
            return;

        // プレイヤー投擲と同じ疲労・ノックバック計算。大砲には所有者除外がない。
        foreach (PlayerController player in PlayerManager.Instance.Players)
        {
            if (player == null || !player.IsSpawned
                || Vector3.Distance(player.transform.position + Vector3.up * 0.6f, position) > this.bombData.blastRadius)
                continue;

            PlayerStatus status = player.GetComponent<PlayerStatus>();
            status?.AddFatigue(this.bombData.Power);
            float power = this.bombData.Power * (status != null ? status.KnockbackMultiplier : 1f);
            Vector3 direction = player.transform.position - position;
            direction.y = 0f;
            Vector3 force = direction.normalized * power + Vector3.up * (power * 0.3f);
            this.KnockbackRpc(player.NetworkObjectId, force);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ExplodeRpc(int id, Vector3 position)
    {
        int index = this.shots.FindIndex(shot => shot.id == id);
        if (index < 0)
            return;

        GameObject visual = this.shots[index].visual;
        this.shots.RemoveAt(index);
        if (visual == null)
            return;

        visual.transform.position = position;
        if (visual.TryGetComponent(out BombPushBomb bomb)) bomb.Explode();
        else Destroy(visual);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void KnockbackRpc(ulong targetId, Vector3 force)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject obj)
            && obj.TryGetComponent(out PlayerController player) && player.IsOwner)
            player.TakeKnockback(force);
    }

    private void ClearShots()
    {
        foreach (Shot shot in this.shots)
            if (shot.visual != null) Destroy(shot.visual);

        this.shots.Clear();
    }

    public override void OnNetworkDespawn() => this.ClearShots();
}
