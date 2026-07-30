using UnityEngine;
using Unity.Netcode;
using TMPro;

public class LobbyController : MonoBehaviour
{
    [SerializeField]
    private TMP_InputField playerNameInput;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    void OnDestroy()
    {
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
    }
}
