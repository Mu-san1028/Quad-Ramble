using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class LobbyController : MonoBehaviour
{
    [Header("接続状況のテキスト")]
    [FormerlySerializedAs("matchingText")]
    [SerializeField] private TextMeshProUGUI connectText;
    [SerializeField] private TextMeshProUGUI getReadyText;

    [SerializeField] private NetworkConnect networkConnect;
    [SerializeField] private CharacterSelectUI characterSelect;

    [Header("接続状態による表示切り替え（テキスト以外）")]
    [Tooltip("部屋作成・部屋参加ボタンなど。管理スクリプトが付いたオブジェクトは登録しないでください。")]
    [SerializeField] private GameObject[] beforeConnectionObjects = new GameObject[0];
    [Tooltip("接続後専用のUI。キャラ選択Panelの表示はCharacterSelectUIに任せます。")]
    [SerializeField] private GameObject[] afterConnectionObjects = new GameObject[0];

    [Header("背景画像")]
    [SerializeField] private RawImage background;
    [SerializeField] private Texture characterSelectBackground;
    [Tooltip("接続後の背景の表示範囲。正方形の柄を16:9で表示する場合はW=1.778、H=1が目安です。")]
    [SerializeField] private Rect characterSelectUVRect = new Rect(0f, 0f, 1f, 1f);

    private Texture initialBackground;
    private RawImageUVScroller backgroundScroller;
    private bool? showingCharacterBackground;
    private ConnectionContainer registeredContainer;


    private void Awake()
    {
        this.RefreshConnectionObjects();

        if (this.background == null) return;

        // 接続前の画像はRawImageに設定したものを使用する。
        this.initialBackground = this.background.texture;
        this.backgroundScroller = this.background.GetComponent<RawImageUVScroller>();
        this.RefreshBackground();
    }


    private void Update()
    {
        this.RefreshConnectionObjects();
        this.RefreshBackground();

        if (this.networkConnect == null)
            return;

        if (this.networkConnect.State
            != NetworkConnect.ConnectionState.Connected)
        {
            this.registeredContainer = null;
            if (this.characterSelect != null)
                this.characterSelect.HidePanel();

            this.RefreshConnectionText();
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        ConnectionContainer container = ConnectionContainer.Instance;

        if (manager == null || container == null || !container.IsSpawned)
        {
            SetText(this.getReadyText, "ロビーを準備しています…");
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
            SetText(this.getReadyText, $"参加人数：{connected} / {required}人");
        }
        else
        {
            SetText(this.getReadyText, $"決定：{container.ReadyCount} / {required}人");
        }
    }


    private void RefreshConnectionObjects()
    {
        bool connected = this.networkConnect != null
            && this.networkConnect.State == NetworkConnect.ConnectionState.Connected;

        SetObjectsActive(this.beforeConnectionObjects, !connected);
        SetObjectsActive(this.afterConnectionObjects, connected);

        if (this.connectText != null)
            SetObjectActive(this.connectText.gameObject, !connected);

        if (this.getReadyText != null)
            SetObjectActive(this.getReadyText.gameObject, connected);
    }


    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        foreach (GameObject target in objects)
        {
            SetObjectActive(target, active);
        }
    }


    private static void SetObjectActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }


    private void RefreshBackground()
    {
        if (this.background == null)
            return;

        bool connected = this.networkConnect != null
            && this.networkConnect.State
            == NetworkConnect.ConnectionState.Connected;

        // 柄が未設定なら、ロゴ入り背景を静止表示したままにする。
        bool showCharacterBackground = connected && this.characterSelectBackground != null;

        // 毎フレームUVをリセットするとスクロールできないため、切り替え時だけ適用する。
        if (this.showingCharacterBackground == showCharacterBackground)
            return;

        if (this.backgroundScroller != null)
            this.backgroundScroller.enabled = false;

        this.background.texture = showCharacterBackground
            ? this.characterSelectBackground
            : this.initialBackground;

        this.background.uvRect = showCharacterBackground
            ? this.characterSelectUVRect
            : new Rect(0f, 0f, 1f, 1f);

        if (this.backgroundScroller != null)
            this.backgroundScroller.enabled = showCharacterBackground;

        this.showingCharacterBackground = showCharacterBackground;
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
                    SetText(this.connectText, countText);
                }
                else
                {
                    SetText(this.connectText,
                        this.networkConnect.ErrorMessage
                        + "\n"
                        + countText
                    );
                }

                break;

            case NetworkConnect.ConnectionState.Connecting:
                SetText(this.connectText, "接続しています…");
                break;

            case NetworkConnect.ConnectionState.Stopping:
                SetText(this.connectText,
                    this.networkConnect.ErrorMessage
                    + "\n接続を終了しています…"
                );

                break;
        }
    }


    private static void SetText(TextMeshProUGUI target, string message)
    {
        // 表示切り替えはRefreshConnectionObjectsに任せ、文章だけを更新する。
        if (target != null && target.text != message)
            target.text = message;
    }
}
