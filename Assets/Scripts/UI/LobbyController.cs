using TMPro;
using Unity.Netcode;
using UnityEngine;

public class LobbyController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI matchingText;
    [SerializeField] private NetworkConnect networkConnect;
    [SerializeField] private CharacterSelectUI characterSelect;

    private ConnectionContainer registeredContainer;


    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;

        if (manager == null)
            return;

        if (this.networkConnect.State
            != NetworkConnect.ConnectionState.Connected)
        {
            this.registeredContainer = null;
            this.characterSelect.HidePanel();
            this.RefreshConnectionText();
            return;
        }

        ConnectionContainer container = ConnectionContainer.Instance;

        if (container == null || !container.IsSpawned)
        {
            this.SetText("ロビーを準備しています…");
            return;
        }

        // 接続成功とコンテナ到着の両方を待って、一度だけ登録する。
        if (this.registeredContainer != container)
        {
            this.registeredContainer = container;
            container.Append(manager.LocalClientId);
        }

        int required = container.RequiredPlayerCount;
        int connected = container.GetConnectionsNum();

        if (connected < required)
        {
            this.SetText($"参加人数：{connected} / {required}人");
        }
        else
        {
            this.SetText($"決定：{container.ReadyCount} / {required}人");
        }
    }


    private void RefreshConnectionText()
    {
        switch (this.networkConnect.State)
        {
            case NetworkConnect.ConnectionState.Idle:
                string countText =
                    $"参加人数：◀ {this.networkConnect.SelectedPlayerCount}人 ▶";

                if (string.IsNullOrEmpty(this.networkConnect.ErrorMessage))
                {
                    this.SetText(countText);
                }
                else
                {
                    this.SetText(
                        this.networkConnect.ErrorMessage
                        + "\n"
                        + countText
                    );
                }

                break;

            case NetworkConnect.ConnectionState.Connecting:
                this.SetText("接続しています…");
                break;

            case NetworkConnect.ConnectionState.Stopping:
                this.SetText(
                    this.networkConnect.ErrorMessage
                    + "\n接続を終了しています…"
                );

                break;
        }
    }


    private void SetText(string message)
    {
        if (!this.matchingText.gameObject.activeSelf)
            this.matchingText.gameObject.SetActive(true);

        if (this.matchingText.text != message)
            this.matchingText.text = message;
    }
}
