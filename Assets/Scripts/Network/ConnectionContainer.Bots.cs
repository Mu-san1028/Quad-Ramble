using System.Linq;
using Unity.Netcode;

public partial class ConnectionContainer
{
    public int BotCount => connections.Count(c => c.IsBot);

    public Connection RegisterBot(int characterIndex)
    {
        if (!IsServer || IsTransitioning || CurrentRound != 0
            || connections.Count >= RequiredPlayerCount || CharacterDatabase.Instance == null
            || !CharacterDatabase.Instance.IsValidIndex(characterIndex) || GetOwnerOf(characterIndex) != null)
            return null;

        ulong id = 0;
        while (connections.Any(c => c.Id == id)) id++;
        var entry = new Connection(id, NetworkManager.ServerClientId)
        {
            IsBot = true,
            IsReady = true,
            CharacterIndex = characterIndex,
            Name = $"BOT {id + 1}"
        };
        connections.Add(entry);
        return entry;
    }

    public bool UnregisterBot(ulong participantId)
    {
        if (!IsServer || IsTransitioning || CurrentRound != 0) return false;
        return connections.RemoveAll(c => c.IsBot && c.Id == participantId) > 0;
    }

    public void PublishBotChange()
    {
        if (IsServer && IsSpawned) BroadcastRoster();
    }
}
