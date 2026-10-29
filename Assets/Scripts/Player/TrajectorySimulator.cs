using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 弾道の計算を行う。
/// </summary>
public static class TrajectorySimulator
{
    public const float StepDuration = 0.05f;
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    public struct ProjectileHit
    {
        public Vector3 point;
        public float distance;
    }

    public struct Settings
    {
        public LayerMask groundLayers;
        public LayerMask blockerLayers;
        public int maxSteps;
        public ulong ignoreNetworkObjectId;   // 自分の弾は自分に当てない
    }

    public static Vector3 Simulate(Vector3 start, Vector3 vel, float gravity, in Settings settings,
                                   out float flightTime, out bool hitSomething,
                                   List<Vector3> path = null)
    {
        Vector3 pos = start;
        float dt = StepDuration;
        flightTime = 0f;
        hitSomething = false;

        path?.Clear();
        path?.Add(pos);

        for (int i = 0; i < settings.maxSteps; i++)
        {
            Vector3 next = PositionAtTime(start, vel, gravity, flightTime + dt);
            if (TryGetHit(pos, next, settings, out var hit))
            {
                float distance = Vector3.Distance(pos, next);
                flightTime += distance > 0f ? dt * hit.distance / distance : 0f;
                hitSomething = true;
                path?.Add(hit.point);
                return hit.point;
            }

            flightTime += dt;
            pos = next;
            path?.Add(pos);
        }
        return pos;
    }

    public static Vector3 PositionAtTime(Vector3 start, Vector3 velocity, float gravity, float time)
    {
        return start + velocity * time + Vector3.down * (0.5f * gravity * time * time);
    }

    // 床とプレイヤーのうち最も近い実体を採用し、自分とコースのトリガーは無視する。
    public static bool TryGetHit(Vector3 start, Vector3 end, in Settings settings, out ProjectileHit nearest)
    {
        nearest = default;
        Vector3 displacement = end - start;
        float distance = displacement.magnitude;
        if (distance <= 0f) return false;

        int mask = settings.groundLayers | settings.blockerLayers;
        int count = Physics.RaycastNonAlloc(start, displacement / distance, hitBuffer, distance,
            mask, QueryTriggerInteraction.Ignore);
        // バッファが埋まった場合も、近い壁や相手を取りこぼさない。
        var hits = count < hitBuffer.Length ? hitBuffer : Physics.RaycastAll(start, displacement / distance, distance,
            mask, QueryTriggerInteraction.Ignore);
        if (hits != hitBuffer) count = hits.Length;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            var netObj = hit.collider.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == settings.ignoreNetworkObjectId) continue;
            if (found && hit.distance >= nearest.distance) continue;
            nearest = new ProjectileHit { point = hit.point, distance = hit.distance };
            found = true;
        }

        // 顔は通常の物理判定へ追加せず、弾道に対してだけ検出する。手前の壁を優先する。
        foreach (var region in CriticalHitRegion.Active)
        {
            if (!region.IsValid || region.Owner.NetworkObjectId == settings.ignoreNetworkObjectId
                || (settings.blockerLayers.value & (1 << region.Owner.gameObject.layer)) == 0) continue;
            if (!region.Raycast(start, displacement / distance, distance, out float faceDistance)
                || (found && faceDistance >= nearest.distance)) continue;

            nearest = new ProjectileHit { point = start + displacement / distance * faceDistance, distance = faceDistance };
            found = true;
        }
        return found;
    }
}
