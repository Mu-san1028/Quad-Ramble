using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class MazeManager : NetworkBehaviour
{
    [SerializeField] private MazeData[] mazeDatas;
    [SerializeField] private NetworkObject itemBoxPrefab;
    [SerializeField, Min(0.1f)] private float shuffleInterval = 15f;
    private readonly List<NetworkObject> itemBoxes = new();

    // The server chooses the maze; clients only apply the synchronized index.
    private readonly NetworkVariable<int> activeMazeIndex = new(-1);
    private float shuffleTimer;

    public override void OnNetworkSpawn()
    {
        activeMazeIndex.OnValueChanged += OnActiveMazeChanged;
        ApplyMaze(activeMazeIndex.Value);

        if (IsServer)
            ChangeMaze();
    }

    public override void OnNetworkDespawn()
    {
        activeMazeIndex.OnValueChanged -= OnActiveMazeChanged;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned || mazeDatas == null || mazeDatas.Length == 0)
            return;

        shuffleTimer += Time.deltaTime;
        if (shuffleTimer < Mathf.Max(0.1f, shuffleInterval))
            return;

        shuffleTimer = 0f;
        ChangeMaze();
    }

    private void ChangeMaze()
    {
        if (mazeDatas == null || mazeDatas.Length == 0)
            return;

        int nextIndex = Random.Range(0, mazeDatas.Length);
        if (mazeDatas.Length > 1 && nextIndex == activeMazeIndex.Value)
            nextIndex = (nextIndex + 1) % mazeDatas.Length;

        activeMazeIndex.Value = nextIndex;
    }

    private void OnActiveMazeChanged(int _, int newIndex)
    {
        if (IsServer)
        {
            DespawnItemBoxes();
            SpawnItemBoxes(newIndex);
        }

        ApplyMaze(newIndex);
    }

    private void ApplyMaze(int activeIndex)
    {
        if (mazeDatas == null)
            return;

        for (int i = 0; i < mazeDatas.Length; i++)
        {
            if (mazeDatas[i].mazeObject != null)
                mazeDatas[i].mazeObject.SetActive(i == activeIndex);
        }
    }

    private void SpawnItemBoxes(int mazeIndex)
    {
        if (!IsServer || itemBoxPrefab == null || mazeDatas == null
            || mazeIndex < 0 || mazeIndex >= mazeDatas.Length)
            return;

        Transform[] spawnPoints = mazeDatas[mazeIndex].itemBoxSpawnPoints;
        if (spawnPoints == null)
            return;

        foreach (Transform spawnPoint in spawnPoints)
        {
            if (spawnPoint == null)
                continue;

            NetworkObject itemBox = Instantiate(itemBoxPrefab, spawnPoint.position, spawnPoint.rotation);
            itemBox.Spawn(destroyWithScene: true);
            itemBoxes.Add(itemBox);
        }
    }

    private void DespawnItemBoxes()
    {
        if (!IsServer)
            return;

        for (int i = itemBoxes.Count - 1; i >= 0; i--)
        {
            NetworkObject itemBox = itemBoxes[i];
            if (itemBox != null && itemBox.IsSpawned)
                itemBox.Despawn(destroy: true);
            else if (itemBox != null)
                Destroy(itemBox.gameObject);
        }

        itemBoxes.Clear();
    }

    [System.Serializable]
    public struct MazeData
    {
        public GameObject mazeObject;
        public Transform[] itemBoxSpawnPoints;
    }
}
