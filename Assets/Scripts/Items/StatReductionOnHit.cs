using UnityEngine;

/// <summary>弾Prefabに設定する能力低下。爆発の命中判定からサーバーで適用する。</summary>
[DisallowMultipleComponent]
public sealed class StatReductionOnHit : MonoBehaviour
{
    [SerializeField] private UpgradeKind kind = UpgradeKind.Jump;
    [SerializeField, Range(0.1f, 1f)] private float multiplier = 0.6f;
    [SerializeField, Min(0f)] private float duration = 3f;


    public void Apply(PlayerStatus target)
    {
        if (!this.enabled || target == null)
            return;

        target.ApplyReduction(this.kind, this.multiplier, this.duration);
    }
}
