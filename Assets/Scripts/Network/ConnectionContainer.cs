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
    /// <summary>選んだキャラ。-1 は未選択</summary>
    public int CharacterIndex { get; set; } = -1;
    public bool IsReady { get; set; }
    public bool IsBot { get; set; }

    public Connection(ulong id, ulong clientId, int score = 0)
    {
        this.Id = id;
        this.ClientId = clientId;
        this.Score = score;
    }

    public void AddScore(int delta)
    {
        this.Score += delta;
    }
}

public partial class ConnectionContainer : NetworkBehaviour
{
    public static ConnectionContainer Instance { get; private set; }
    private readonly List<Connection> connections = new();
    public IReadOnlyList<(string Name, int Score)> Result {get; private set;} = new List<(string Name, int Score)>();
    public event System.Action<int> OnConnectionsChanged;
    public event System.Action OnResultsReceived;
    public static event System.Action<ConnectionContainer> OnInstanceReady;
    private readonly NetworkVariable<int> currentRound = new ();
    public int CurrentRound => this.currentRound.Value;

    private readonly NetworkVariable<int> requiredPlayerCount = new(4);

    public int RequiredPlayerCount => this.requiredPlayerCount.Value;

    private int initialRequiredPlayers = 4;

    public void InitializeRequiredPlayers(int count)
    {
        // NetworkConnectから、Spawn前に呼ぶ。
        if (this.IsSpawned)
            return;

        this.initialRequiredPlayers = Mathf.Clamp(count, 1, 4);
    }


    public override void OnNetworkSpawn()
    {
        // ホスト側で初期値を確定してから、利用側へ準備完了を通知する。
        if (this.IsServer)
        {
            this.requiredPlayerCount.Value = this.initialRequiredPlayers;

            NetworkManager.Singleton.OnClientDisconnectCallback
                += this.HandleClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback += this.HandleTransitionJoin;
        }

        Instance = this;
        OnInstanceReady?.Invoke(this);
    }
    public override void OnNetworkDespawn()
    {
        this.CleanupTransition();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= this.HandleClientDisconnect;
            NetworkManager.Singleton.OnClientConnectedCallback -= this.HandleTransitionJoin;
        }

        if (Instance == this) Instance = null;
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (!IsServer) return;
        this.transitionBarrier.Remove(clientId);
        if (this.connections.RemoveAll(c => !c.IsBot && c.ClientId == clientId) == 0) return;
        this.BroadcastRoster();
    }

    public void Append(ulong clientId)
    {
        this.AppendServerRpc(clientId);
    }
    
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
private void AppendServerRpc(ulong clientId, RpcParams rpcParams = default)
    {
        // 引数で別人のIDを渡されても、登録するのは送信者本人。
        ulong senderId = rpcParams.Receive.SenderClientId;

        if (this.GetConnection(senderId) != null)
            return;

        if (this.IsTransitioning || this.CurrentRound > 0 || this.connections.Count >= this.RequiredPlayerCount)
        {
            if (senderId != NetworkManager.ServerClientId) NetworkManager.DisconnectClient(senderId);
            return;
        }

        // 途中離脱しても、既存プレイヤーの番号は詰め直さない。
        ulong playerId = 0;
        while (this.connections.Any(c => c.Id == playerId)) playerId++;

        this.connections.Add(
            new Connection(playerId, senderId)
            {
                Name = "Player"
            }
        );

        this.BroadcastRoster();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncRosterClientRpc(RosterEntry[] roster)
    {
        if (!IsServer)
        {
            this.connections.Clear();
            foreach (var e in roster)
            {
                this.connections.Add(new Connection(e.PlayerId, e.ClientId, e.Score)
                {
                    Name = e.Name.ToString(),
                    CharacterIndex = e.CharacterIndex,
                    IsReady = e.IsReady,
                    IsBot = e.IsBot
                });
            }
        }

        OnConnectionsChanged?.Invoke(this.connections.Count);
    }

    private void BroadcastRoster()
    {
        var roster = this.connections
            .Select(c => new RosterEntry
            {
                ClientId = c.ClientId,
                PlayerId = c.Id,
                Score = c.Score,
                Name = c.Name,
                CharacterIndex = c.CharacterIndex,
                IsReady = c.IsReady,
                IsBot = c.IsBot
            })
            .ToArray();

        this.SyncRosterClientRpc(roster);
    }


    public int GetConnectionsNum()
    {
        return this.connections.Count;
    }

    public Connection GetConnection(ulong id)
    {
        return this.connections.Find(c => !c.IsBot && c.ClientId == id);
    }

    public Connection GetConnectionByPlayerId(ulong playerId)
    {
        return this.connections.Find(c => c.Id == playerId);
    }

    /// <summary>表示用の名前。見つからなければ null</summary>
    public string GetName(ulong clientId) => this.GetConnection(clientId)?.Name;

    /// <summary>そのキャラを確保している人。空いていれば null</summary>
    public Connection GetOwnerOf(int characterIndex)
    {
        if (characterIndex < 0) return null;
        return this.connections.Find(c => c.CharacterIndex == characterIndex);
    }

    /// <summary>キャラを確保する。先着順で、すでに他人が取っていたら無視される</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SelectCharacterRpc(int index, RpcParams rpcParams = default)
    {
        if (this.IsTransitioning) return;
        ulong senderId = rpcParams.Receive.SenderClientId;

        Connection me = this.GetConnection(senderId);
        if (me == null) return;
        if (CharacterDatabase.Instance == null || !CharacterDatabase.Instance.IsValidIndex(index)) return;

        // 重複禁止。自分がすでに持っているぶんには通す
        Connection owner = this.GetOwnerOf(index);
        if (owner != null && owner.Id != me.Id) return;

        me.CharacterIndex = index;
        me.Name = CharacterDatabase.Instance.Get(index).displayName;
        me.IsReady = false;   // 選び直したら決定は取り消し

        this.BroadcastRoster();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetReadyRpc(bool ready, RpcParams rpcParams = default)
    {
        if (this.IsTransitioning) return;
        Connection me = this.GetConnection(rpcParams.Receive.SenderClientId);
        if (me == null) return;
        if (ready && me.CharacterIndex < 0) return;   // 選ぶ前に決定はできない

        me.IsReady = ready;
        this.BroadcastRoster();
    }

    public int ReadyCount => this.connections.Count(c => c.IsReady);

    /// <summary>人数が揃っていて、全員がキャラを選んで決定しているか</summary>
    public bool IsEveryoneReady(int requiredPlayers)
    {
        if (this.connections.Count < requiredPlayers) return false;
        return this.connections.All(c => c.CharacterIndex >= 0 && c.IsReady);
    }


    public int GetRank(ulong id)
    {
        Connection player = this.connections.Find(c => c.Id == id);
        return player == null ? 0 : 1 + this.connections.Count(c => c.Score > player.Score);
    }

    /// <summary>ラウンドの全得点加算後に、累計成績をまとめて同期する。</summary>
    public void BroadcastScores()
    {
        if (!IsServer || !IsSpawned) return;

        this.BroadcastRoster();
    }

    public void BroadcastFinalScores()
    {
        if (!IsServer) return; // サーバーのみがこの処理を行う
        var entries = this.connections
            .Select(e => new ResultEntry { Name = e.Name, Score = e.Score })
            .ToArray();
        BroadcastFinalScoresClientRpc(entries);
    }

    [Rpc(SendTo.ClientsAndHost)]
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

    [Rpc(SendTo.ClientsAndHost)]
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
