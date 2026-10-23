using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>既存Blockをそのまま生成し、ホストの物理演算結果だけを同期する。</summary>
public sealed class SurvivalBlockLauncher : NetworkBehaviour
{
    [SerializeField] private GameObject blockPrefab;
    [SerializeField, Min(0.05f)] private float syncInterval = 0.1f;
    [SerializeField] private float cleanupHeight = -40f;

    private sealed class Block
    {
        public NetworkObject networkObject;
        public Rigidbody body;
        public Vector3 position;
        public Quaternion rotation;
    }

    private readonly Dictionary<ulong, Block> blocks = new();
    private readonly List<ulong> removed = new();
    private float nextSync;

    public bool Launch(Vector3 origin, Vector3 target, float duration)
    {
        if (!IsServer || this.blockPrefab == null
            || !NetworkManager.NetworkConfig.Prefabs.Contains(this.blockPrefab))
            return false;

        GameObject instance = Instantiate(this.blockPrefab, origin, this.blockPrefab.transform.rotation);
        if (!instance.TryGetComponent(out NetworkObject net) || !instance.TryGetComponent(out Rigidbody body))
        {
            Destroy(instance);
            return false;
        }

        SceneManager.MoveGameObjectToScene(instance, gameObject.scene);
        net.Spawn(destroyWithScene: true);
        body.isKinematic = false;
        body.useGravity = true;
        body.linearDamping = 0f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        float t = Mathf.Max(0.5f, duration);
        body.linearVelocity = (target - origin) / t - Physics.gravity * (0.5f * t);
        this.blocks.Add(net.NetworkObjectId, new Block { networkObject = net, body = body });
        this.SyncBlockRpc(net.NetworkObjectId, origin, instance.transform.rotation);
        return true;
    }

    private void Update()
    {
        if (!IsSpawned) return;

        this.removed.Clear();
        bool sync = IsServer && Time.unscaledTime >= this.nextSync;
        if (sync) this.nextSync = Time.unscaledTime + this.syncInterval;

        foreach (var entry in this.blocks)
        {
            Block block = entry.Value;
            if (block.networkObject == null || !block.networkObject.IsSpawned || block.body == null)
            {
                this.removed.Add(entry.Key);
                continue;
            }

            if (IsServer)
            {
                if (block.body.position.y < this.cleanupHeight)
                {
                    block.networkObject.Despawn();
                    this.removed.Add(entry.Key);
                    continue;
                }

                if (RoundManager.Instance != null && RoundManager.Instance.IsRoundOver)
                    block.body.isKinematic = true;

                if (sync) this.SyncBlockRpc(entry.Key, block.body.position, block.body.rotation);
            }
            else
            {
                float blend = 1f - Mathf.Exp(-25f * Time.deltaTime);
                block.body.position = Vector3.Lerp(block.body.position, block.position, blend);
                block.body.rotation = Quaternion.Slerp(block.body.rotation, block.rotation, blend);
            }
        }

        foreach (ulong id in this.removed) this.blocks.Remove(id);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncBlockRpc(ulong id, Vector3 position, Quaternion rotation)
    {
        if (IsServer) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject net))
            return; // Spawnがまだ届いていなければ次の同期で取得する。

        if (!this.blocks.TryGetValue(id, out Block block))
        {
            if (!net.TryGetComponent(out Rigidbody body)) return;
            body.isKinematic = true;
            body.useGravity = false;
            body.position = position;
            body.rotation = rotation;
            block = new Block { networkObject = net, body = body };
            this.blocks.Add(id, block);
        }

        block.position = position;
        block.rotation = rotation;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            foreach (Block block in this.blocks.Values)
                if (block.networkObject != null && block.networkObject.IsSpawned)
                    block.networkObject.Despawn();
        }

        this.blocks.Clear();
    }
}
