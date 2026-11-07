using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>足元位置による円柱判定。単独占領中だけサーバーが加点する。</summary>
public class CaptureZone : NetworkBehaviour
{
    [SerializeField, Min(.01f)] private float radius = 4f;
    [SerializeField, Min(0)] private float pointsPerSecond = 1f;
    [SerializeField] private LayerMask playerLayers;
    [Tooltip("床面の原点から足元を認める高さ。ワールドm単位")]
    [SerializeField, Min(0)] private float heightAbove = 2.5f;
    [SerializeField, Min(0)] private float heightBelow = .25f;
    [Header("見た目（既存の参照を維持）")]
    [SerializeField] private Renderer zoneRenderer;
    [SerializeField] private Color neutralColor = Color.gray;
    [SerializeField] private Color capturedColor = Color.green;
    [SerializeField] private CaptureZoneVisual visual;

    private readonly NetworkVariable<CaptureZoneSnapshot> snapshot = new(new CaptureZoneSnapshot(CaptureZoneState.Neutral));
    private Collider[] hits = new Collider[16];
    private readonly HashSet<ulong> seen = new();
    private readonly CaptureZoneScoreClock scoreClock = new();
    [Header("エリア切り替え")]
    [Tooltip("指定した場合、管理者の初期化・同期前は閉鎖する。未指定は常時有効エリア用")]
    [SerializeField] private CaptureZoneDirector director;
    private bool scheduled;
    private float previousMultiplier;

    public CaptureZoneState State => snapshot.Value.State;
    public ulong OccupantClientId => snapshot.Value.Owner;
    public float ScoreMultiplier => director ? director.MultiplierFor(this) : (scheduled ? 0f : 1f);
    public bool IsAreaActive => ScoreMultiplier > 0f;
    public event System.Action<CaptureZoneSnapshot> StateChanged;
    public static event System.Action<CaptureZone, ulong> PointsAwarded;
    public bool IsScoring => IsSpawned && isActiveAndEnabled && IsAreaActive
        && pointsPerSecond > 0f && State == CaptureZoneState.Occupied;

    private void Awake()
    {
        scheduled = director != null;
        BindVisual();
    }

    private void BindVisual()
    {
        if (!visual) visual = GetComponent<CaptureZoneVisual>();
        if (!visual) visual = gameObject.AddComponent<CaptureZoneVisual>();
        visual.Bind(this, zoneRenderer, neutralColor, capturedColor);
    }

    public bool RegisterDirector(CaptureZoneDirector source)
    {
        if (director && director != source)
            return false;

        director = source;
        scheduled = true;
        return true;
    }

    public void UnregisterDirector(CaptureZoneDirector source)
    {
        if (director == source)
            director = null; // 管理者が消えても勝手に常時有効へ戻さない。
    }

    public override void OnNetworkSpawn()
    {
        BindVisual();
        scoreClock.Reset();
        snapshot.OnValueChanged += OnSnapshotChanged;
        if (IsServer) snapshot.Value = new CaptureZoneSnapshot(CaptureZoneState.Neutral);
        if (IsServer && playerLayers.value == 0)
            Debug.LogWarning("CaptureZone: Player LayersにプレイヤーColliderのレイヤーを指定してください。", this);
    }

    public override void OnNetworkDespawn()
    {
        snapshot.OnValueChanged -= OnSnapshotChanged;
        scoreClock.Reset();
        if (visual) visual.Unbind();
    }

    private void Update()
    {
        if (!IsSpawned || !IsServer) return;

        if (director)
            director.RefreshServerSchedule();

        float multiplier = ScoreMultiplier;
        if (multiplier != previousMultiplier)
        {
            scoreClock.Reset();
            previousMultiplier = multiplier;
        }

        if (!IsAreaActive || GameManager.Instance == null || GameManager.Instance.currentState != GameState.Playing)
        {
            snapshot.Value = new CaptureZoneSnapshot(IsAreaActive ? CaptureZoneState.Neutral : CaptureZoneState.Inactive);
            scoreClock.Reset();
            return;
        }
        PlayerStatus occupant = FindOccupant(out CaptureZoneSnapshot next);
        snapshot.Value = next;
        int points = scoreClock.Step(next.State == CaptureZoneState.Occupied, next.Owner, pointsPerSecond * multiplier, Time.deltaTime);
        if (occupant && points > 0)
        {
            occupant.AddRoundScore(points);
            NotifyPointsAwardedClientRpc(next.Owner);
        }
    }

    [ClientRpc]
    private void NotifyPointsAwardedClientRpc(ulong clientId)
    {
        PointsAwarded?.Invoke(this, clientId);
    }

    private void OnDisable()
    {
        scoreClock.Reset();
        if (IsSpawned && IsServer) snapshot.Value = new CaptureZoneSnapshot(CaptureZoneState.Neutral);
    }

    private PlayerStatus FindOccupant(out CaptureZoneSnapshot result)
    {
        result = new CaptureZoneSnapshot(CaptureZoneState.Neutral);
        float above = Mathf.Max(0, heightAbove), below = Mathf.Max(0, heightBelow), r = Mathf.Max(.01f, radius);
        Vector3 center = transform.position + Vector3.up * ((above - below) * .5f);
        Vector3 extents = new(r, Mathf.Max(.01f, (above + below) * .5f), r);
        int count;
        // 満杯なら拡張して再検索。Collider数による人数の見落としを防ぐ。
        while ((count = Physics.OverlapBoxNonAlloc(center, extents, hits, Quaternion.identity, playerLayers, QueryTriggerInteraction.Collide)) == hits.Length)
            System.Array.Resize(ref hits, hits.Length * 2);
        seen.Clear();
        PlayerStatus sole = null;
        ulong owner = CaptureZoneSnapshot.NoOwner;
        for (int i = 0; i < count; i++)
        {
            var player = hits[i].GetComponentInParent<PlayerController>();
            if (!player || !player.IsSpawned || !player.isActiveAndEnabled) continue;
            if (PlayerManager.Instance && !PlayerManager.Instance.Players.Contains(player)) continue;
            if (!player.TryGetComponent<PlayerStatus>(out var status) || !status.IsSpawned) continue;
            // Player_v2のルートは足元。体の端が触れただけでは占領しない。
            Vector3 offset = player.transform.position - transform.position;
            if (!CaptureZoneRules.Contains(offset.x, offset.y, offset.z, r, below, above)) continue;
            if (!seen.Add(player.NetworkObjectId)) continue;

            if (seen.Count > 1)
            {
                result = new CaptureZoneSnapshot(CaptureZoneState.Contested);
                return null;
            }

            sole = status;
            owner = player.ParticipantId;
        }
        if (sole) result = new CaptureZoneSnapshot(CaptureZoneState.Occupied, owner);
        return sole;
    }

    private void OnSnapshotChanged(CaptureZoneSnapshot previous, CaptureZoneSnapshot current) => StateChanged?.Invoke(current);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 1, .7f);
        Vector3 bottom = transform.position - Vector3.up * Mathf.Max(0, heightBelow);
        Vector3 top = transform.position + Vector3.up * Mathf.Max(0, heightAbove);
        for (int i = 0; i < 64; i++)
        {
            float a = i * Mathf.PI / 32, b = (i + 1) * Mathf.PI / 32;
            Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Mathf.Max(.01f, radius);
            Vector3 q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * Mathf.Max(.01f, radius);
            Gizmos.DrawLine(bottom + p, bottom + q); Gizmos.DrawLine(top + p, top + q);
            if (i % 16 == 0) Gizmos.DrawLine(bottom + p, top + p);
        }
    }
}

public struct CaptureZoneSnapshot : INetworkSerializable, System.IEquatable<CaptureZoneSnapshot>
{
    public const ulong NoOwner = ulong.MaxValue;
    public CaptureZoneState State;
    public ulong Owner;
    public CaptureZoneSnapshot(CaptureZoneState state, ulong owner = NoOwner) { State = state; Owner = owner; }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref Owner);
    }

    public bool Equals(CaptureZoneSnapshot other) => State == other.State && Owner == other.Owner;
}


