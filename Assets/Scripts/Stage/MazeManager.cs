using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class MazeManager : NetworkBehaviour
{
    [SerializeField] private GameObject[] mazeObjects;
    [SerializeField, Min(0.1f)] private float shuffleInterval = 15f;

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
        if (!IsServer || mazeObjects == null || mazeObjects.Length == 0)
            return;

        shuffleTimer += Time.deltaTime;
        if (shuffleTimer < Mathf.Max(0.1f, shuffleInterval))
            return;

        shuffleTimer = 0f;
        ChangeMaze();
    }

    private void ChangeMaze()
    {
        if (mazeObjects == null || mazeObjects.Length == 0)
            return;

        int nextIndex = Random.Range(0, mazeObjects.Length);
        if (mazeObjects.Length > 1 && nextIndex == activeMazeIndex.Value)
            nextIndex = (nextIndex + 1) % mazeObjects.Length;

        activeMazeIndex.Value = nextIndex;
    }

    private void OnActiveMazeChanged(int _, int newIndex)
    {
        ApplyMaze(newIndex);
    }

    private void ApplyMaze(int activeIndex)
    {
        if (mazeObjects == null)
            return;

        for (int i = 0; i < mazeObjects.Length; i++)
        {
            if (mazeObjects[i] != null)
                mazeObjects[i].SetActive(i == activeIndex);
        }
    }
}
