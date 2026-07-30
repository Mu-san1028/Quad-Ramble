using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class Connection
{
    public ulong Id { get; private set; }
    public ulong ClientId { get; private set; }
    public string Name { get; set; }
    public int Score { get; private set; }

    public Connection(ulong id, ulong clientId)
    {
        this.Id = id;
        this.ClientId = clientId;
    }

    public void AddScore(int delta)
    {
        this.Score += delta;
    }
}

public class ConnectionContainer : NetworkBehaviour
{
    public static ConnectionContainer Instance { get; private set; }
    private readonly List<Connection> connections = new();

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("ConnectionContainer follows the singleton pattern.");
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public void Append(ulong clientId, string name)
    {
        this.AppendServerRpc(clientId, name);
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AppendServerRpc(ulong clientId, string name)
    {
        this.AppendClientRpc(clientId, name);
    }

    [ClientRpc]
    private void AppendClientRpc(ulong clientId, string name)
    {
        this.connections.Add(new Connection((ulong)this.connections.Count, clientId) { Name = name });
    }

    public int GetConnectionsNum()
    {
        return this.connections.Count;
    }

    public int GetRank(ulong id)
    {
        List<(Connection, int)> sorted = this.connections.OrderBy(c => c.Score).Select((c, i) => (c, i)).ToList();
        return sorted.Find(pair => pair.Item1.Id == id).Item2 + 1;
    }
}
