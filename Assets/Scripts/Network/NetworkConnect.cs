using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    public enum ConnectionState
    {
        Idle,
        Connecting,
        Connected,
        Stopping
    }

    [SerializeField] private GameObject connectionContainerPrefab;

    [Header("既存のボタン")]
    public Button createServerButton;
    public Button joinServerButton;

    [Header("接続設定")]
    [SerializeField, Min(1f)] private float connectionTimeout = 15f;

    [Header("参加人数の初期設定")]
    [SerializeField, Range(1, 4)] private int initialPlayerCount = 1;

    public ConnectionState State { get; private set; }
        = ConnectionState.Idle;

    public int SelectedPlayerCount { get; private set; }

    public string ErrorMessage { get; private set; } = "";

    private NetworkManager manager;
    private float connectionDeadline;


    private void Awake()
    {
        this.SelectedPlayerCount = Mathf.Clamp(this.initialPlayerCount, 1, 4);
    }


    private void Start()
    {
        this.manager = NetworkManager.Singleton;

        if (this.manager == null)
        {
            Debug.LogError(
                "NetworkConnect: NetworkManagerが見つかりません。",
                this
            );

            this.enabled = false;
            return;
        }

        if (this.createServerButton != null)
            this.createServerButton.onClick.AddListener(this.CreateSelectedHost);

        if (this.joinServerButton != null)
            this.joinServerButton.onClick.AddListener(this.JoinHost);

        this.RefreshButtons();
    }


    private void Update()
    {
        if (this.manager == null)
            return;

        switch (this.State)
        {
            case ConnectionState.Connecting:
                if (this.manager.IsConnectedClient)
                {
                    this.SetState(ConnectionState.Connected);
                }
                else if (Time.unscaledTime >= this.connectionDeadline)
                {
                    this.Fail(
                        "接続できませんでした。接続先とホストの起動を確認してください。"
                    );
                }
                else if (!this.manager.IsListening)
                {
                    this.Fail("接続に失敗しました。もう一度お試しください。");
                }

                break;

            case ConnectionState.Connected:
                if (!this.manager.IsConnectedClient)
                    this.Fail("接続が切れました。");

                break;

            case ConnectionState.Stopping:
                // 終了途中で再接続を始めない。
                if (!this.manager.IsListening
                    && !this.manager.ShutdownInProgress)
                {
                    this.SetState(ConnectionState.Idle);
                }

                break;
        }
    }


    public void ChangePlayerCount(int delta)
    {
        if (this.State != ConnectionState.Idle)
            return;

        this.SelectedPlayerCount = Mathf.Clamp(
            this.SelectedPlayerCount + delta,
            1,
            4
        );
    }


    private void CreateSelectedHost()
    {
        this.CreateHost(this.SelectedPlayerCount);
    }


    public void CreateHost(int requiredPlayers)
    {
        if (!this.CanStartConnection())
            return;

        if (this.connectionContainerPrefab == null
            || !this.connectionContainerPrefab.TryGetComponent(
                out ConnectionContainer _)
            || !this.connectionContainerPrefab.TryGetComponent(
                out NetworkObject _))
        {
            this.ErrorMessage =
                "ConnectionContainerのPrefab設定を確認してください。";

            Debug.LogError(this.ErrorMessage, this);
            return;
        }

        this.SelectedPlayerCount = Mathf.Clamp(requiredPlayers, 1, 4);
        this.BeginConnection();

        if (!this.manager.StartHost())
        {
            this.Fail("ホストを開始できませんでした。");
            return;
        }

        // ホスト起動後、共有するロビー情報を生成する。
        GameObject instance = Instantiate(this.connectionContainerPrefab);

        ConnectionContainer container =
            instance.GetComponent<ConnectionContainer>();

        container.InitializeRequiredPlayers(this.SelectedPlayerCount);

        instance.GetComponent<NetworkObject>().Spawn(
            destroyWithScene: false
        );
    }


    public void JoinHost()
    {
        if (!this.CanStartConnection())
            return;

        this.BeginConnection();

        if (!this.manager.StartClient())
            this.Fail("参加処理を開始できませんでした。");
    }


    private bool CanStartConnection()
    {
        return this.manager != null
            && this.State == ConnectionState.Idle
            && !this.manager.IsListening
            && !this.manager.ShutdownInProgress;
    }


    private void BeginConnection()
    {
        this.ErrorMessage = "";

        this.connectionDeadline =
            Time.unscaledTime + this.connectionTimeout;

        this.SetState(ConnectionState.Connecting);
    }


    private void Fail(string message)
    {
        this.ErrorMessage = message;
        this.SetState(ConnectionState.Stopping);

        if (this.manager != null
            && !this.manager.ShutdownInProgress)
        {
            this.manager.Shutdown();
        }
    }


    private void SetState(ConnectionState state)
    {
        this.State = state;
        this.RefreshButtons();
    }


    private void RefreshButtons()
    {
        bool interactable = this.State == ConnectionState.Idle;

        if (this.createServerButton != null)
            this.createServerButton.interactable = interactable;

        if (this.joinServerButton != null)
            this.joinServerButton.interactable = interactable;
    }


    private void OnDestroy()
    {
        if (this.createServerButton != null)
            this.createServerButton.onClick.RemoveListener(
                this.CreateSelectedHost
            );

        if (this.joinServerButton != null)
            this.joinServerButton.onClick.RemoveListener(this.JoinHost);
    }
}
