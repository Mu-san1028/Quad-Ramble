using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 弾道の計算を行う。
/// </summary>
public static class TrajectorySimulator
{
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
        float dt = 0.05f;
        flightTime = 0f;
        hitSomething = false;

        path?.Clear();
        path?.Add(pos);

        for (int i = 0; i < settings.maxSteps; i++)
        {
            Vector3 next = pos + vel * dt;
            vel.y -= gravity * dt;
            flightTime += dt;

            if (Physics.Linecast(pos, next, settings.groundLayers))
            {
                hitSomething = true;
                return pos;
            }

            if (Physics.Linecast(pos, next, out RaycastHit hit, settings.blockerLayers))
            {
                var hitNetObj = hit.collider.GetComponent<NetworkObject>();
                if (hitNetObj == null || hitNetObj.NetworkObjectId != settings.ignoreNetworkObjectId)
                {
                    hitSomething = true;
                    return pos;
                }
            }

            pos = next;
            path?.Add(pos);
        }
        return pos;
    }
}
