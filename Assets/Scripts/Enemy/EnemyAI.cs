using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 生存中のプレイヤーを探し、NavMeshで追いかけて近距離攻撃を行う簡易AIです。
/// NetworkObjectのサーバー権限とNetworkTransformを使う構成を想定しています。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent), typeof(EnemyAttack))]
public sealed class EnemyAI : NetworkBehaviour
{
    [SerializeField, Min(0.1f)] private float searchInterval = 0.4f;
    [SerializeField, Min(0.1f)] private float detectionRange = 15f;
    [SerializeField, Min(0.1f)] private float stoppingDistance = 1.2f;

    private NavMeshAgent agent;
    private EnemyAttack attack;
    private PlayerController target;
    private float nextSearchTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        attack = GetComponent<EnemyAttack>();
    }

    private void Update()
    {
        // ネットワーク中はサーバーだけが意思決定します。
        if (IsSpawned && !IsServer) return;
        if (GetComponent<EnemyHealth>()?.IsDead == true) return;

        if (target == null || Time.time >= nextSearchTime)
        {
            target = FindNearestPlayer();
            nextSearchTime = Time.time + Mathf.Max(0.1f, searchInterval);
        }

        if (target == null)
        {
            if (agent.isOnNavMesh) agent.ResetPath();
            attack.SetTarget(null);
            return;
        }

        Vector3 offset = target.transform.position - transform.position;
        offset.y = 0f;
        bool inRange = offset.magnitude <= stoppingDistance;

        if (agent.isOnNavMesh)
        {
            agent.stoppingDistance = stoppingDistance * 0.8f;
            agent.SetDestination(target.transform.position);
        }

        if (offset.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(offset), 8f * Time.deltaTime);

        attack.SetTarget(inRange ? target : null);
    }

    private PlayerController FindNearestPlayer()
    {
        var manager = PlayerManager.Instance;
        if (manager == null) return null;

        PlayerController nearest = null;
        float nearestDistance = detectionRange * detectionRange;
        foreach (var player in manager.Players)
        {
            if (player == null || !player.IsSpawned || !player.isActiveAndEnabled
                || player.IsEliminated || player.IsTitleMode)
                continue;

            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearest = player;
            nearestDistance = distance;
        }
        return nearest;
    }
}
