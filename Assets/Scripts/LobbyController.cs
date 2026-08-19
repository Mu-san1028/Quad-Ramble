using UnityEngine;
using Unity.Netcode;
using TMPro;

public class LobbyController : MonoBehaviour
{
    [SerializeField]
    private TMP_InputField playerNameInput;
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

        string playerName = string.IsNullOrEmpty(playerNameInput.text) ? "Player" : playerNameInput.text;
        ConnectionContainer.Instance.Append(clientId, playerName);
        matchingText.gameObject.SetActive(true);
    }

    private void UpdateMatchingText(int playerCount)
    {
        matchingText.text = $"Matching... ({playerCount} / {GameManager.Instance.requiredPlayers})";
    }
}
