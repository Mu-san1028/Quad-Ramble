using TMPro;
using UnityEngine;
using System.Linq;
using UnityEngine.UI;
using Unity.Netcode;

public class ResultUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private Button returnToTitleButton;

    void Start()
    {
        ConnectionContainer.Instance.OnResultsReceived += Render;
        Render();

        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        returnToTitleButton.gameObject.SetActive(isServer);
        returnToTitleButton.onClick.AddListener(() => ConnectionContainer.Instance.ReturnToTitle());
    }

    void OnDestroy()
    {
        if (ConnectionContainer.Instance != null)
            ConnectionContainer.Instance.OnResultsReceived -= Render;
    }

    private void Render()
    {
        var lines = ConnectionContainer.Instance.Result
            .Select((r, i) => $"{i + 1}:  {r.Name}  {r.Score}pt");
        resultText.text = string.Join("\n", lines);
    }
}
