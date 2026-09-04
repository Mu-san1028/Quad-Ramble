using UnityEngine;

public abstract class AttackRangeData : ScriptableObject
{
    public Color indicatorColor = new Color(1f, 0.3f, 0f, 0.3f);

    /// <summary>rangeScale は範囲強化の倍率。ScriptableObject の値は書き換えない</summary>
    public abstract Collider[] GetHitTargets(Vector3 origin, Quaternion rotation,
                                             LayerMask targetLayer, float rangeScale = 1f);

    public virtual void DrawGizmos(Vector3 origin, Quaternion rotation) { }

}
