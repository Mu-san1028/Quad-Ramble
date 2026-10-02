using UnityEngine;
using Unity.Netcode;
using TMPro;

public class LobbyController : MonoBehaviour
{
    [SerializeField]
    private TextMeshProUGUI matchingText;

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        ConnectionContainer.OnInstanceReady += HandleContainerReady;
        if (ConnectionContainer.Instance != null)
        {
            HandleContainerReady(ConnectionContainer.Instance);
        }
    }

    private void HandleContainerReady(ConnectionContainer container)
    {
        container.OnConnectionsChanged += UpdateMatchingText;
    }

    void OnDestroy()
    {
        ConnectionContainer.OnInstanceReady -= HandleContainerReady;
        if (ConnectionContainer.Instance != null)
        {
            ConnectionContainer.Instance.OnConnectionsChanged -= UpdateMatchingText;
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        // 名前はキャラ選択で決まる（選んだキャラの表示名がそのまま名前になる）
        ConnectionContainer.Instance.Append(clientId);
        matchingText.gameObject.SetActive(true);
    }

    private void UpdateMatchingText(int playerCount)
    {
        int required = GameManager.Instance.requiredPlayers;

        // 人数が揃ったら、あとは全員の決定待ち
        matchingText.text = playerCount < required
            ? $"Matching... ({playerCount} / {required})"
            : $"決定 {ConnectionContainer.Instance.ReadyCount} / {required}";
    }
}
