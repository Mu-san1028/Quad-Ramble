using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>ホストがロビーでBot用Prefabを追加・削除する。接続端末やPlayerObjectは増やさない。</summary>
[RequireComponent(typeof(ConnectionContainer))]
public class BotParticipantManager : NetworkBehaviour
{
    [SerializeField] private LobbyBotControls controlsPrefab;
    [Tooltip("人間用Player Prefabとは別に生成する、登録済みのBot用Prefab。")]
    [SerializeField] private GameObject botPrefab;
    [SerializeField] private string lobbySceneName = "Title";
    private readonly List<NetworkObject> bots = new();
    private LobbyBotControls controls;
    private ConnectionContainer roster;

    public bool CanEdit => IsSpawned && IsServer && roster != null && !roster.IsTransitioning
        && roster.CurrentRound == 0 && SceneManager.GetActiveScene().name == lobbySceneName;
    public bool CanAdd => CanEdit && botPrefab != null && roster.GetConnectionsNum() < roster.RequiredPlayerCount
        && NetworkManager.ConnectedClientsIds.Count + roster.BotCount < roster.RequiredPlayerCount
        && FindFreeCharacter() >= 0;
    public bool CanRemove => CanEdit && bots.Count > 0;
    public string Status => roster == null ? string.Empty
        : $"BOT {roster.BotCount} / 参加者 {roster.GetConnectionsNum()}・{roster.RequiredPlayerCount}人枠";

    public override void OnNetworkSpawn()
    {
        roster = GetComponent<ConnectionContainer>();
        if (IsServer && controlsPrefab != null)
        {
            controls = Instantiate(controlsPrefab);
            controls.Bind(this);
        }
    }

    private int FindFreeCharacter()
    {
        var database = CharacterDatabase.Instance;
        if (database == null || roster == null) return -1;
        for (int i = 0; i < database.Count; i++)
            if (database.Get(i)?.modelPrefab != null && roster.GetOwnerOf(i) == null) return i;
        return -1;
    }

    public void AddBot()
    {
        if (!CanAdd) return;
        var prefab = botPrefab;
        if (prefab.GetComponent<PlayerIdentity>() == null || prefab.GetComponent<NetworkObject>() == null
            || prefab.GetComponent<PlayerController>() == null)
        {
            Debug.LogError("[Bot] Bot PrefabにはPlayerIdentity、NetworkObject、PlayerControllerが必要です。", this);
            return;
        }
        var entry = roster.RegisterBot(FindFreeCharacter());
        if (entry == null) return;

        GameObject instance = null;
        try
        {
            instance = Instantiate(prefab);
            instance.GetComponent<PlayerIdentity>().InitializeBot(entry.Id);
            var networkObject = instance.GetComponent<NetworkObject>();
            networkObject.Spawn(destroyWithScene: false);
            bots.Add(networkObject);
            roster.PublishBotChange();
        }
        catch (System.Exception exception)
        {
            roster.UnregisterBot(entry.Id);
            if (instance != null)
            {
                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned) networkObject.Despawn();
                else Destroy(instance);
            }
            roster.PublishBotChange();
            Debug.LogException(exception, this);
        }
    }

    public void RemoveBot()
    {
        if (!CanRemove) return;
        int last = bots.Count - 1;
        var bot = bots[last];
        bots.RemoveAt(last);
        if (bot != null)
        {
            roster.UnregisterBot(bot.GetComponent<PlayerIdentity>().ParticipantId);
            if (bot.IsSpawned) bot.Despawn();
        }
        roster.PublishBotChange();
    }

    public override void OnNetworkDespawn()
    {
        if (controls != null) Destroy(controls.gameObject);
        controls = null;
        bots.Clear();
    }
}
