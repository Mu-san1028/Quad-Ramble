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
    public IReadOnlyList<(string Name, int Score)> Result {get; private set;} = new List<(string Name, int Score)>();
    public event System.Action<int> OnConnectionsChanged;
    public event System.Action OnResultsReceived;
    public static event System.Action<ConnectionContainer> OnInstanceReady;
    private readonly NetworkVariable<int> currentRound = new ();
    public int CurrentRound => this.currentRound.Value;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        OnInstanceReady?.Invoke(this);

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += this.HandleClientDisconnect;
        }
    }
    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= this.HandleClientDisconnect;
        }

        if (Instance == this) Instance = null;
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (!IsServer) return;
        if (this.connections.RemoveAll(c => c.ClientId == clientId) == 0) return;
        this.BroadcastRoster();
    }

    public void Append(ulong clientId, string name)
    {
        this.AppendServerRpc(clientId, name);
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AppendServerRpc(ulong clientId, string name)
    {
        // 名簿の追加はサーバーだけが行う
        this.connections.Add(new Connection((ulong)this.connections.Count, clientId) { Name = name });

        this.BroadcastRoster();
    }

    [ClientRpc]
    private void SyncRosterClientRpc(RosterEntry[] roster, ClientRpcParams clientRpcParams = default)
    {
        if (!IsServer)
        {
            this.connections.Clear();
            foreach (var e in roster)
            {
                this.connections.Add(new Connection((ulong)this.connections.Count, e.ClientId)
                {
                    Name = e.Name.ToString()
                });
            }
        }

        OnConnectionsChanged?.Invoke(this.connections.Count);
    }

    private void BroadcastRoster()
    {
        var roster = this.connections
            .Select(c => new RosterEntry { ClientId = c.ClientId, Name = c.Name })
            .ToArray();

        var targets = NetworkManager.Singleton.ConnectedClientsIds.ToArray();
        this.SyncRosterClientRpc(roster, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = targets }
        });
    }


    public int GetConnectionsNum()
    {
        return this.connections.Count;
    }

    public Connection GetConnection(ulong id)
    {
        return this.connections.Find(c => c.ClientId == id);
    }

    /// <summary>表示用の名前。見つからなければ null</summary>
    public string GetName(ulong clientId) => this.GetConnection(clientId)?.Name;


    public int GetRank(ulong id)
    {
        List<(Connection, int)> sorted = this.connections.OrderBy(c => c.Score).Select((c, i) => (c, i)).ToList();
        return sorted.Find(pair => pair.Item1.Id == id).Item2 + 1;
    }

    public void BroadcastFinalScores()
    {
        if (!IsServer) return; // サーバーのみがこの処理を行う
        var entries = this.connections
            .Select(e => new ResultEntry { Name = e.Name, Score = e.Score })
            .ToArray();
        BroadcastFinalScoresClientRpc(entries);
    }

    [ClientRpc]
    private void BroadcastFinalScoresClientRpc(ResultEntry[] entries)
    {
        Result = entries
            .Select(e => (e.Name.ToString(), e.Score))
            .OrderByDescending(e => e.Item2)
            .ToList();
        OnResultsReceived?.Invoke();
    }

    public void ReturnToTitle()
    {
        if (!IsServer) return;
        this.ReturnToTitleClientRpc();
    }

    [ClientRpc]
    private void ReturnToTitleClientRpc()
    {
        GameManager.Instance.ReturnToTitle();
    }

    public void SetCurrentRound(int round)
    {
        if (!IsServer) return;
        this.currentRound.Value = round;
    }
}
