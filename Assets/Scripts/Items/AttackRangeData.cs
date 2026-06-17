using UnityEngine;

public abstract class AttackRangeData : ScriptableObject
{
    public Color indicatorColor = new Color(1f, 0.3f, 0f, 0.3f);

    public abstract Collider[] GetHitTargets(Vector3 origin, Quaternion rotation, LayerMask targetLayer);

    public virtual void DrawGizmos(Vector3 origin, Quaternion rotation) { }

}
