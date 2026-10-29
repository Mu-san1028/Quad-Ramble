using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器の演出を担当する。判定は一切しない。
/// 発砲音や爆発パーティクルの参照は、ここに [SerializeField] で足していく。
/// </summary>
[System.Serializable]
public class WeaponEffects
{
    private readonly Dictionary<int, GameObject> projectiles = new();
    // TODO: 発砲音（AudioSource）・銃口の光（ParticleSystem）・爆発エフェクトをここに持たせる

    public void PlayShoot(string ownerName)
    {
        Debug.Log($"【{ownerName}】が撃った！");
    }

    /// <summary>溜めの見た目。attackWindup が 0 の武器では呼ばれない</summary>
    public void PlayWindup(string ownerName, float duration)
    {
        // TODO: 構えモーション・チャージ音・溜めエフェクトをここに
        Debug.Log($"【{ownerName}】が構えた（{duration}秒）");
    }

    public void PlayExplosion(Vector3 center, float radius)
    {
        Debug.Log($"[Explosion] center: {center}, radius: {radius}");
    }

    /// <summary>弾が飛ぶ見た目。コルーチンの起動は呼び出し側に任せる</summary>
    public IEnumerator Projectile(int projectileId, GameObject prefab, Vector3 start, Vector3 vel, float duration, float gravity, bool faceFlightDirection)
    {
        Quaternion initialRotation = (vel.sqrMagnitude > 0.0001f) ? Quaternion.LookRotation(vel) : Quaternion.identity;
        var obj = Object.Instantiate(prefab, start, initialRotation);
        this.projectiles[projectileId] = obj;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (obj == null) yield break;
            elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);
            obj.transform.position = TrajectorySimulator.PositionAtTime(start, vel, gravity, elapsed);

            if (faceFlightDirection)
            {
                Vector3 currentVelocity = vel + Vector3.down * gravity * elapsed;

                if (currentVelocity.sqrMagnitude > 0.0001f)
                {
                    obj.transform.rotation = Quaternion.LookRotation(currentVelocity);
                }
            }

            yield return null;
        }

        // 寿命到達後も、サーバーから確定した着弾位置が届くまで待つ。
    }

    public void FinishProjectile(int projectileId, Vector3 position, bool explode)
    {
        if (!this.projectiles.TryGetValue(projectileId, out var obj)) return;
        this.projectiles.Remove(projectileId);
        if (obj == null) return;
        obj.transform.position = position;

        if (explode && obj.TryGetComponent<BombPushBomb>(out var bomb))
        {
            bomb.Explode();
        }
        else
        {
            Object.Destroy(obj);
        }
    }

    public void ClearProjectiles()
    {
        foreach (var obj in this.projectiles.Values)
            if (obj != null) Object.Destroy(obj);
        this.projectiles.Clear();
    }
}
