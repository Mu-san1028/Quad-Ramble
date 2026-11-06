using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;
using Unity.Netcode;
using UnityEngine;

/// <summary>NGO client/server用EOS P2P transport。NAT越えとRelayはEOSが選択する。</summary>
public sealed class EosNetworkTransport : NetworkTransport
{
    public override ulong ServerClientId => 0;
    public ProductUserId HostUserId { get; set; }
    public Func<ProductUserId, bool> CanAcceptPeer { get; set; }
    public Action SessionStopped { get; set; }

    private readonly SocketId socket = new SocketId { SocketName = "QuadRambleNGO1" };
    private readonly Dictionary<ulong, ProductUserId> peers = new();
    private readonly Dictionary<string, ulong> ids = new();
    private readonly Dictionary<(ulong, byte), EosPacketCodec.Reader> readers = new();
    private readonly Queue<(NetworkEvent type, ulong id)> events = new();
    private readonly byte[] receiveBuffer = new byte[P2PInterface.MAX_PACKET_SIZE];
    private P2PInterface p2p;
    private ProductUserId localUser;
    private ulong requestNotification, establishedNotification, closedNotification;
    private ulong nextId = 1;
    private uint messageId;
    private bool running, server;

    public override void Initialize(NetworkManager networkManager = null)
    {
        networkManager.MaximumTransmissionUnitSize = P2PInterface.MAX_PACKET_SIZE - EosPacketCodec.HeaderSize;
        networkManager.MaximumFragmentedMessageSize = EosPacketCodec.MaxMessageSize;
    }

    private bool Begin(bool isServer)
    {
        if (running) return false;
        localUser = EOSManager.Instance.GetProductUserId();
        p2p = EOSManager.Instance.GetEOSP2PInterface();
        if (localUser == null || !localUser.IsValid() || p2p == null) return false;
        server = isServer;
        running = true;
        var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = localUser, SocketId = socket };
        requestNotification = p2p.AddNotifyPeerConnectionRequest(ref request, null,
            (ref OnIncomingConnectionRequestInfo info) =>
            {
                if (!running || !Allowed(info.RemoteUserId)) return;
                var accept = new AcceptConnectionOptions
                { LocalUserId = localUser, RemoteUserId = info.RemoteUserId, SocketId = socket };
                p2p.AcceptConnection(ref accept);
            });
        var established = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = localUser, SocketId = socket };
        establishedNotification = p2p.AddNotifyPeerConnectionEstablished(ref established, null,
            (ref OnPeerConnectionEstablishedInfo info) =>
            {
                if (!running || !Allowed(info.RemoteUserId) || ids.ContainsKey(info.RemoteUserId.ToString())) return;
                ulong id = server ? nextId++ : ServerClientId;
                ids.Add(info.RemoteUserId.ToString(), id);
                peers.Add(id, info.RemoteUserId);
                events.Enqueue((NetworkEvent.Connect, id));
            });
        var closed = new AddNotifyPeerConnectionClosedOptions { LocalUserId = localUser, SocketId = socket };
        closedNotification = p2p.AddNotifyPeerConnectionClosed(ref closed, null,
            (ref OnRemoteConnectionClosedInfo info) =>
            {
                if (running && ids.TryGetValue(info.RemoteUserId.ToString(), out ulong id)) RemovePeer(id);
            });
        if (requestNotification == 0 || establishedNotification == 0 || closedNotification == 0)
        {
            Shutdown();
            return false;
        }
        return true;
    }

    private bool Allowed(ProductUserId peer) => server
        ? CanAcceptPeer != null && CanAcceptPeer(peer)
        : HostUserId != null && peer.Equals(HostUserId);

    public override bool StartServer() => Begin(true);

    public override bool StartClient()
    {
        if (HostUserId == null || !HostUserId.IsValid() || HostUserId.Equals(EOSManager.Instance.GetProductUserId())) return false;
        if (!Begin(false)) return false;
        // 最初の送信でEOSの接続要求を発生させる。ゲームデータとは別channel。
        return SendPacket(HostUserId, new byte[] { 1 }, 255, true) == Result.Success;
    }

    private Result SendPacket(ProductUserId peer, byte[] packet, byte channel, bool delayed = false)
    {
        var options = new SendPacketOptions
        {
            LocalUserId = localUser, RemoteUserId = peer, SocketId = socket,
            Channel = channel, Data = new ArraySegment<byte>(packet), AllowDelayedDelivery = delayed,
            Reliability = channel == 1 || channel == 2 ? PacketReliability.UnreliableUnordered : PacketReliability.ReliableOrdered,
            DisableAutoAcceptConnection = !delayed
        };
        return p2p.SendPacket(ref options);
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
    {
        if (!running || !peers.TryGetValue(clientId, out var peer) || payload.Count == 0) return;
        byte channel = delivery == NetworkDelivery.Unreliable ? (byte)1
            : delivery == NetworkDelivery.UnreliableSequenced ? (byte)2 : (byte)0;
        int capacity = P2PInterface.MAX_PACKET_SIZE - EosPacketCodec.HeaderSize;
        if (payload.Count > EosPacketCodec.MaxMessageSize || (channel != 0 && payload.Count > capacity))
            throw new InvalidOperationException("EOS送信サイズ超過。大きいデータにはReliableFragmentedSequencedを使用してください。");
        uint id = ++messageId;
        for (int offset = 0; offset < payload.Count; offset += capacity)
        {
            var packet = EosPacketCodec.Fragment(payload, id, offset, P2PInterface.MAX_PACKET_SIZE);
            if (SendPacket(peer, packet, channel) == Result.Success) continue;
            DisconnectRemoteClient(clientId);
            break;
        }
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        clientId = 0;
        payload = default;
        receiveTime = Time.realtimeSinceStartup;
        if (!running) return NetworkEvent.Nothing;
        if (events.Count > 0)
        {
            var item = events.Dequeue();
            clientId = item.id;
            return item.type;
        }
        // 途中のfragmentでNothingを返すと、NGOのpollが止まり毎フレーム1個しか読めない。
        for (int i = 0; i < 256; i++)
        {
            var options = new ReceivePacketOptions { LocalUserId = localUser, MaxDataSizeBytes = (uint)receiveBuffer.Length };
            ProductUserId sender = null;
            SocketId receivedSocket = default;
            var result = p2p.ReceivePacket(ref options, ref sender, ref receivedSocket, out byte channel,
                new ArraySegment<byte>(receiveBuffer), out uint size);
            if (result != Result.Success) break;
            if (receivedSocket.SocketName != socket.SocketName || channel > 2 || sender == null
                || !ids.TryGetValue(sender.ToString(), out ulong id)) continue;
            var key = (id, channel);
            if (!readers.TryGetValue(key, out var reader)) readers[key] = reader = new EosPacketCodec.Reader();
            var message = reader.Receive(receiveBuffer, (int)size, channel == 2);
            if (message == null) continue;
            clientId = id;
            payload = new ArraySegment<byte>(message);
            return NetworkEvent.Data;
        }
        return NetworkEvent.Nothing;
    }

    private void RemovePeer(ulong id)
    {
        if (!peers.TryGetValue(id, out var peer)) return;
        ids.Remove(peer.ToString());
        peers.Remove(id);
        for (byte channel = 0; channel < 3; channel++) readers.Remove((id, channel));
        events.Enqueue((NetworkEvent.Disconnect, id));
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (!running || !peers.TryGetValue(clientId, out var peer)) return;
        var options = new CloseConnectionOptions { LocalUserId = localUser, RemoteUserId = peer, SocketId = socket };
        p2p.CloseConnection(ref options);
        RemovePeer(clientId);
    }

    public override void DisconnectLocalClient() => DisconnectRemoteClient(ServerClientId);
    public override ulong GetCurrentRtt(ulong clientId) => 0; // EOS P2PはRTTを公開しない。

    public override void Shutdown()
    {
        if (!running) return;
        running = false;
        bool platformAlive = EOSManager.Instance.GetEOSPlatformInterface() != null;
        if (platformAlive)
        {
            p2p.RemoveNotifyPeerConnectionRequest(requestNotification);
            p2p.RemoveNotifyPeerConnectionEstablished(establishedNotification);
            p2p.RemoveNotifyPeerConnectionClosed(closedNotification);
            var options = new CloseConnectionsOptions { LocalUserId = localUser, SocketId = socket };
            p2p.CloseConnections(ref options);
        }
        peers.Clear(); ids.Clear(); readers.Clear(); events.Clear();
        nextId = 1;
        HostUserId = null;
        p2p = null;
        if (platformAlive) SessionStopped?.Invoke();
    }
}
