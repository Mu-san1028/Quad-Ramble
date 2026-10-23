using UnityEngine;

/// <summary>弾Prefabに設定する減速効果。命中判定はサーバーから呼び出す。</summary>
[DisallowMultipleComponent]
public sealed class SlowOnHit : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)] private float speedMultiplier = 0.6f;
    [SerializeField, Min(0f)] private float duration = 3f;

    public void Apply(PlayerStatus target)
    {
        if (!this.enabled || target == null)
            return;

        target.ApplySlow(this.speedMultiplier, this.duration);
    }
}
