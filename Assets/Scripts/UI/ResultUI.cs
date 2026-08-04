using TMPro;
using UnityEngine;
using System.Linq;

public class ResultUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI resultText;

    void Start()
    {
        ConnectionContainer.Instance.OnResultsReceived += Render;
        Render();   // 既に届いていた場合に備えて
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
