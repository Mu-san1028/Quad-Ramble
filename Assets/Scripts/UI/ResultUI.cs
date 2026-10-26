using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ResultUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI resultRank1Text;
    [SerializeField] private TextMeshProUGUI resultRank2Text;
    [SerializeField] private TextMeshProUGUI resultRank3Text;
    [SerializeField] private TextMeshProUGUI resultRank4Text;
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
        // 通信結果を管理するオブジェクトがまだ存在しない場合は、表示更新を行いません。
        if (ConnectionContainer.Instance == null)
        {
            return;
        }

        // 順位ごとのTextMeshProUGUIへ、それぞれ対応する結果を設定します。
        // ConnectionContainer.Resultはポイントの高い順に並んでいるため、
        // 1番目のデータが1位、2番目のデータが2位になります。
        SetRankText(resultRank1Text, 1);
        SetRankText(resultRank2Text, 2);
        SetRankText(resultRank3Text, 3);
        SetRankText(resultRank4Text, 4);
    }

    // 指定された順位の結果を、対応するTextMeshProUGUIに表示します。
    private void SetRankText(TextMeshProUGUI targetText, int rank)
    {
        // Inspectorでテキスト欄が未設定でも、NullReferenceExceptionを起こさないようにします。
        if (targetText == null)
        {
            return;
        }

        var results = ConnectionContainer.Instance.Result;

        // その順位のプレイヤーがいない場合は、前回の文字が残らないよう空欄にします。
        if (rank < 1 || rank > results.Count)
        {
            targetText.text = string.Empty;
            targetText.gameObject.SetActive(false);
            return;
        }

        // この順位のプレイヤーがいる場合は、前回非表示にしていても再表示します。
        targetText.gameObject.SetActive(true);

        // リストは0番目から始まるため、順位から1を引いて対象プレイヤーを取得します。
        var playerResult = results[rank - 1];

        // 順位、名前、獲得ポイントを同じ行に表示します。
        targetText.text = $"{playerResult.Name}　{playerResult.Score} pt";
    }
}
