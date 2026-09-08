using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 占拠ゾーン。中にプレイヤーが1人だけのとき、その人に毎秒加点する。
/// </summary>
public class CaptureZone : NetworkBehaviour
{
    [SerializeField] private float radius = 4f;
    [SerializeField] private float pointsPerSecond = 1f;
    [SerializeField] private LayerMask playerLayers;

    [Header("見た目")]
    [SerializeField] private Renderer zoneRenderer;
    [SerializeField] private Color neutralColor = Color.gray;
    [SerializeField] private Color capturedColor = Color.green;

    private const ulong NoOwner = ulong.MaxValue;
    private readonly NetworkVariable<ulong> ownerClientId = new (NoOwner);

    private readonly Collider[] hits = new Collider[16];
    private readonly HashSet<ulong> seen = new ();
    private float carry;   // 1点に満たない端数の持ち越し

    public override void OnNetworkSpawn()
    {
        this.ownerClientId.OnValueChanged += this.OnOwnerChanged;
        this.ApplyColor(this.ownerClientId.Value);
    }

    public override void OnNetworkDespawn()
    {
        this.ownerClientId.OnValueChanged -= this.OnOwnerChanged;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (GameManager.Instance == null || GameManager.Instance.currentState != GameState.Playing)
        {
            this.ownerClientId.Value = NoOwner;
            return;
        }

        PlayerStatus occupant = this.FindSoleOccupant(out ulong clientId);
        this.ownerClientId.Value = clientId;

        if (occupant == null) { this.carry = 0f; return; }

        this.carry += this.pointsPerSecond * Time.deltaTime;
        int whole = Mathf.FloorToInt(this.carry);
        if (whole <= 0) return;

        this.carry -= whole;
        occupant.AddRoundScore(whole);
    }

    /// <summary>ゾーン内が1人だけならその PlayerStatus。0人・2人以上なら null</summary>
    private PlayerStatus FindSoleOccupant(out ulong clientId)
    {
        clientId = NoOwner;

        int count = Physics.OverlapSphereNonAlloc(transform.position, this.radius, this.hits, this.playerLayers);
        this.seen.Clear();
        PlayerController found = null;

        for (int i = 0; i < count; i++)
        {
            var player = this.hits[i].GetComponentInParent<PlayerController>();
            if (player == null) continue;
            if (!this.seen.Add(player.NetworkObjectId)) continue;   // Player_v2 はコライダーが2つ
            if (this.seen.Count > 1) return null;                   // 複数人 = 拮抗

            found = player;
        }

        if (found == null) return null;
        clientId = found.OwnerClientId;
        return found.GetComponent<PlayerStatus>();
    }

    private void OnOwnerChanged(ulong previous, ulong current) => this.ApplyColor(current);

    private void ApplyColor(ulong owner)
    {
        if (this.zoneRenderer == null) return;
        this.zoneRenderer.material.color = (owner == NoOwner) ? this.neutralColor : this.capturedColor;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, this.radius);
    }
}
