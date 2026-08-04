using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    [SerializeField] private GameObject connectionContainerPrefab;
    public Button createServerButton;
    public Button joinServerButton;

    private void Start()
    {
        createServerButton.onClick.AddListener(CreateServer);
        joinServerButton.onClick.AddListener(JoinServer);
    }

    private void CreateServer()
    {
        NetworkManager.Singleton.OnServerStarted += SpawnConnectionContainer;
        NetworkManager.Singleton.StartHost();
        createServerButton.interactable = false;
        joinServerButton.interactable = false;
    }

    private void JoinServer()
    {
        NetworkManager.Singleton.StartClient();
        createServerButton.interactable = false;
        joinServerButton.interactable = false;
    }

    private void SpawnConnectionContainer()
    {
        NetworkManager.Singleton.OnServerStarted -= SpawnConnectionContainer;
        var obj = Instantiate(connectionContainerPrefab);
        obj.GetComponent<NetworkObject>().Spawn(destroyWithScene: false);
    }
}
