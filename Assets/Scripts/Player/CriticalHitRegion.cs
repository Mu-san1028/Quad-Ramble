using System.Collections.Generic;
using UnityEngine;

/// <summary>物理衝突を起こさない球形の顔判定。PrefabのTransformとRadiusで調整する。</summary>
public class CriticalHitRegion : MonoBehaviour
{
    public static readonly HashSet<CriticalHitRegion> Active = new HashSet<CriticalHitRegion>();
    [SerializeField, Min(0.01f)] private float radius = 0.28f;
    public PlayerController Owner { get; private set; }
    public float WorldRadius => radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x),
        Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z));
    public bool IsValid => isActiveAndEnabled && Owner != null && Owner.IsSpawned
        && !Owner.IsEliminated && !Owner.IsTitleMode;

    private void OnEnable()
    {
        Owner = GetComponentInParent<PlayerController>();
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    public bool Contains(Vector3 point)
    {
        float r = WorldRadius + 0.001f;
        return IsValid && (point - transform.position).sqrMagnitude <= r * r;
    }

    public bool Overlaps(Vector3 point, float blastRadius)
    {
        float r = WorldRadius + Mathf.Max(0f, blastRadius) + 0.001f;
        return IsValid && (point - transform.position).sqrMagnitude <= r * r;
    }

    public bool Raycast(Vector3 start, Vector3 direction, float maxDistance, out float distance)
    {
        distance = 0f;
        if (!IsValid) return false;
        Vector3 delta = start - transform.position;
        float c = delta.sqrMagnitude - WorldRadius * WorldRadius;
        if (c <= 0f) return true;
        float b = Vector3.Dot(delta, direction);
        float discriminant = b * b - c;
        if (discriminant < 0f) return false;
        distance = -b - Mathf.Sqrt(discriminant);
        return distance >= 0f && distance <= maxDistance;
    }

    public static bool IsCritical(PlayerController target, Vector3 point)
    {
        foreach (var region in Active)
            if (region.Owner == target && region.Contains(point)) return true;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.25f, 0.15f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, WorldRadius);
    }
}
