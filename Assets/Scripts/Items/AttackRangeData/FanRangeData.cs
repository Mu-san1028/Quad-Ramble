using UnityEngine;

[CreateAssetMenu(fileName = "NewFanRange", menuName = "AttackRange/Fan")]
public class FanRangeData : AttackRangeData
{
    public float radius = 2f;
    public float angle = 120f;

    public override Collider[] GetHitTargets(Vector3 origin, Quaternion rotation,
                                            LayerMask targetLayer, float rangeScale = 1f)
    {
        // 角度は広げない。半径だけ伸ばす
        var candidates = Physics.OverlapSphere(origin, radius * rangeScale, targetLayer);
        Vector3 forward = rotation * Vector3.forward;
        // 配列candidatesから、originから見てforward方向に対してangleの範囲内にあるコライダーを返す
        // forwardからみて正面から60度以内の方向にあるコライダーを返す
        return System.Array.FindAll(candidates, col =>
        {
            Vector3 dir = (col.transform.position - origin).normalized;
            return Vector3.Angle(forward, dir) < angle * 0.5f;
        });
    }

    public override void DrawGizmos(Vector3 origin, Quaternion rotation)
    {
        Gizmos.color = indicatorColor;
        int segments = 20;
        float halfAngle = angle * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float deg = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 dir = Quaternion.Euler(0, deg, 0) * (rotation * Vector3.forward);
            Gizmos.DrawLine(origin, origin + dir * radius);
        }
    }

}
