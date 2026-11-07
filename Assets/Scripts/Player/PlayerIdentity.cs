using Unity.Netcode;
using UnityEngine;

/// <summary>試合の参加者IDと通信上の所有者を分離する。Botはサーバー所有。</summary>
[DisallowMultipleComponent]
public class PlayerIdentity : NetworkBehaviour
{
    private readonly NetworkVariable<ulong> participantId = new(ulong.MaxValue);
    private readonly NetworkVariable<bool> bot = new(false);
    private bool pendingBot;
    private ulong pendingParticipantId = ulong.MaxValue;

    // 他コンポーネントのOnNetworkSpawnが先に呼ばれても、Botを人間として扱わない。
    public ulong ParticipantId => pendingBot ? pendingParticipantId : participantId.Value;
    public bool IsBot => pendingBot || bot.Value;

    public void InitializeBot(ulong id)
    {
        if (IsSpawned) return;
        pendingParticipantId = id;
        pendingBot = true;
    }

    public override void OnNetworkSpawn()
    {
        // この時点ではNetcodeが全NetworkBehaviourの同期変数を初期化済み。
        if (IsServer && pendingBot)
        {
            participantId.Value = pendingParticipantId;
            bot.Value = true;
            pendingBot = false;
            pendingParticipantId = ulong.MaxValue;
        }

        ConnectionContainer.OnInstanceReady += BindContainer;
        BindContainer(ConnectionContainer.Instance);
    }

    private void BindContainer(ConnectionContainer container)
    {
        if (container == null) return;
        container.OnConnectionsChanged -= RefreshIdentity;
        container.OnConnectionsChanged += RefreshIdentity;
        RefreshIdentity(0);
    }

    private void RefreshIdentity(int count)
    {
        if (!IsServer || IsBot) return;
        var entry = ConnectionContainer.Instance?.GetConnection(OwnerClientId);
        if (entry != null) participantId.Value = entry.Id;
    }

    public override void OnNetworkDespawn()
    {
        ConnectionContainer.OnInstanceReady -= BindContainer;
        if (ConnectionContainer.Instance != null)
            ConnectionContainer.Instance.OnConnectionsChanged -= RefreshIdentity;
    }
}
