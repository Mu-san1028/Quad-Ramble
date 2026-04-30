using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    public Button createServerButton;
    public Button joinServerButton;

    private void Start()
    {
        createServerButton.onClick.AddListener(CreateServer);
        joinServerButton.onClick.AddListener(JoinServer);
    }

    private void CreateServer()
    {
        NetworkManager.Singleton.StartHost();
    }

    private void JoinServer()
    {
        NetworkManager.Singleton.StartClient();
    }
}
