using System.Collections;
using UnityEngine;

/// <summary>
/// 武器の演出を担当する。判定は一切しない。
/// 発砲音や爆発パーティクルの参照は、ここに [SerializeField] で足していく。
/// </summary>
[System.Serializable]
public class WeaponEffects
{
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
    public IEnumerator Projectile(GameObject prefab, Vector3 start, Vector3 vel, float duration, float gravity, Vector3 landing, bool faceFlightDirection)
    {
        Quaternion initialRotation = (vel.sqrMagnitude > 0.0001f) ? Quaternion.LookRotation(vel) : Quaternion.identity;
        var obj = Object.Instantiate(prefab, start, initialRotation);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (obj == null) yield break;
            elapsed = Mathf.Min(elapsed + Time.deltaTime, duration);
            Vector3 pos = start + vel * elapsed;
            pos.y -= 0.5f * gravity * elapsed * elapsed;
            obj.transform.position = pos;

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

        if (obj == null) yield break;

        // 衝突の有無にかかわらず、サーバーの爆発判定と同じ位置で演出する。
        obj.transform.position = landing;

        if (obj.TryGetComponent<BombPushBomb>(out var bomb))
        {
            bomb.Explode();
        }
        else
        {
            Object.Destroy(obj);
        }
    }
}
