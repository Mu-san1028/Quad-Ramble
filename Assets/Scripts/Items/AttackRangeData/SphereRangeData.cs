using UnityEngine;

[CreateAssetMenu(fileName = "NewSphereRange", menuName = "AttackRange/Sphere")]
public class SphereRangeData : AttackRangeData
{
    public float radius = 3f;

    public override Collider[] GetHitTargets(Vector3 origin, Quaternion rotation, LayerMask targetLayer)
    {
        return Physics.OverlapSphere(origin, radius, targetLayer);
    }

    public override void DrawGizmos(Vector3 origin, Quaternion rotation)
    {
        Gizmos.color = indicatorColor;
        Gizmos.DrawWireSphere(origin, radius);
    }

}
