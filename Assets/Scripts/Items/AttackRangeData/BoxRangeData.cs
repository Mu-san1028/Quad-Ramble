using UnityEngine;

/// <summary>縦振りなど、横幅・高さ・奥行きを個別に設定する近接判定。</summary>
[CreateAssetMenu(fileName = "NewBoxRange", menuName = "AttackRange/Box")]
public class BoxRangeData : AttackRangeData
{
    public Vector3 center = new Vector3(0f, 1f, 1.2f);
    public Vector3 size = new Vector3(1f, 2f, 2.4f);

    public override Collider[] GetHitTargets(Vector3 origin, Quaternion rotation,
        LayerMask targetLayer, float rangeScale = 1f)
    {
        float scale = Mathf.Max(0.01f, rangeScale);
        // 高さの中心は足元基準のまま、前方への距離を範囲強化に合わせる。
        Vector3 offset = new Vector3(center.x * scale, center.y, center.z * scale);
        Vector3 half = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * (0.5f * scale);
        return Physics.OverlapBox(origin + rotation * offset, half, rotation, targetLayer);
    }

    public override void DrawGizmos(Vector3 origin, Quaternion rotation)
    {
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(origin, rotation, Vector3.one);
        Gizmos.color = indicatorColor;
        Gizmos.DrawWireCube(center, size);
        Gizmos.matrix = previous;
    }
}
