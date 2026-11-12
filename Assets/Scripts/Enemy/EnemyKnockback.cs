using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 敵が受けた衝撃を保持し、CharacterControllerで徐々に減衰させます。
/// 耐性は倍率で指定し、1より小さいほど吹き飛びにくくなります。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class EnemyKnockback : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float resistance = 1f;
    [SerializeField, Min(0f)] private float damping = 5f;

    private CharacterController body;
    private Vector3 velocity;

    public float Resistance => resistance;

    private void Awake()
    {
        body = GetComponent<CharacterController>();
    }

    /// <summary>サーバー側から力を加えます。同期位置はNetworkTransformに任せます。</summary>
    public void AddForce(Vector3 force)
    {
        if (IsSpawned && !IsServer) return;
        velocity += force / Mathf.Max(0.01f, resistance);
    }

    private void Update()
    {
        // オフライン時はIsSpawned=falseなので動作し、ネットワーク時はサーバーだけが動かします。
        if (IsSpawned && !IsServer) return;
        if (body == null || !body.enabled || velocity.sqrMagnitude < 0.0025f) return;

        body.Move(velocity * Time.deltaTime);
        velocity = Vector3.Lerp(velocity, Vector3.zero, 1f - Mathf.Exp(-damping * Time.deltaTime));
    }
}
