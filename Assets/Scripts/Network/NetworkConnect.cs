using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnect : MonoBehaviour
{
    public enum ConnectionMode { LAN, Relay, EOS }

    public enum ConnectionState
    {
        Idle,
        Connecting,
        Connected,
        Stopping,
        Preparing
    }

    [SerializeField] private GameObject connectionContainerPrefab;

    [Header("既存のボタン")]
    public Button createServerButton;
    public Button joinServerButton;
    public Button leaveRoomButton;
    public Button onlineButton;

    [Header("接続設定")]
    [SerializeField, Min(1f)] private float connectionTimeout = 15f;

    [SerializeField] private ConnectionMode connectionMode = ConnectionMode.LAN;

    [Header("Relay：参加コード")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField, Min(1f)] private float preparationTimeout = 45f;

    public bool UsesRelay => this.connectionMode == ConnectionMode.Relay && !this.UsesEOS;
    public bool IsEditingJoinCode => this.UsesRelay && this.joinCodeInput != null
        && this.joinCodeInput.isActiveAndEnabled && this.joinCodeInput.isFocused;
    public string JoinCode { get; private set; } = "";

    [Header("参加人数の初期設定")]
    [SerializeField, Range(1, 4)] private int initialPlayerCount = 1;

    public ConnectionState State { get; private set; }
        = ConnectionState.Idle;

    public int SelectedPlayerCount { get; private set; }

    public string ErrorMessage { get; private set; } = "";

    private NetworkManager manager;
    private float connectionDeadline;
    private UnityTransport transport;
    private int attemptVersion;
    private EosMatchmakingService eos;
    public bool UsesEOS { get; private set; }
    public string OnlineStatus => this.eos != null ? this.eos.Status : "";


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

        if (this.leaveRoomButton != null)
            this.leaveRoomButton.onClick.AddListener(this.LeaveRoom);

        if (this.onlineButton != null)
            this.onlineButton.onClick.AddListener(this.StartOnlineMatching);

        this.transport = this.manager.GetComponent<UnityTransport>();
        this.eos = this.manager.GetComponent<EosMatchmakingService>();

        this.RefreshButtons();
    }


    private void Update()
    {
        if (this.manager == null)
            return;

        this.RefreshRelayUI();

        switch (this.State)
        {
            case ConnectionState.Preparing:
                if (Time.unscaledTime >= this.connectionDeadline)
                    this.Fail("オンライン接続の準備がタイムアウトしました。通信環境を確認して再試行してください。");
                break;

            case ConnectionState.Connecting:
                if (this.manager.IsConnectedClient && (!this.UsesEOS || this.eos == null || !this.eos.IsBusy))
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
                    && !this.manager.ShutdownInProgress
                    && (this.eos == null || !this.eos.IsBusy))
                {
                    GameManager.Instance?.ResetForNewGame();
                    this.UsesEOS = false;
                    this.SetState(ConnectionState.Idle);
                }

                break;
        }
    }


    public void ChangePlayerCount(int delta)
    {
        if (this.State != ConnectionState.Idle)
            return;

        int nextCount = Mathf.Clamp(
            this.SelectedPlayerCount + delta,
            1,
            4
        );

        if (nextCount == this.SelectedPlayerCount)
            return;

        this.SelectedPlayerCount = nextCount;
        SEAudio.Instance?.Play(SEType.PlayerCountChange);
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
        this.StartConnection(true);
    }


    private void StartConfiguredHost()
    {

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

        this.StartConnection(false);
    }


    private void StartConnection(bool host)
    {
        if (this.connectionMode == ConnectionMode.EOS)
        {
            this.StartOnlineMatching();
            return;
        }
        this.UsesEOS = false;
        if (this.transport == null)
        {
            this.ErrorMessage = "NetworkManagerにUnityTransportを設定してください。";
            return;
        }

        string code = this.joinCodeInput != null
            ? this.joinCodeInput.text.Trim().ToUpperInvariant() : "";
        if (this.UsesRelay && !host && string.IsNullOrWhiteSpace(code))
        {
            this.ErrorMessage = "参加コードを入力してください。";
            return;
        }

        this.ErrorMessage = "";
        this.JoinCode = "";
        int attempt = ++this.attemptVersion;

        if (this.UsesRelay)
        {
            this.connectionDeadline = Time.unscaledTime + this.preparationTimeout;
            this.SetState(ConnectionState.Preparing);
        }

        // 例外はConnectAsync内で扱い、UIイベントのvoidシグネチャを維持する。
        _ = this.ConnectAsync(host, this.UsesRelay, code, attempt);
    }

    public void StartOnlineMatching()
    {
        if (!this.CanStartConnection()) return;
        if (this.connectionContainerPrefab == null
            || !this.connectionContainerPrefab.TryGetComponent<ConnectionContainer>(out _)
            || !this.connectionContainerPrefab.TryGetComponent<NetworkObject>(out _))
        {
            this.ErrorMessage = "ConnectionContainerのPrefab設定を確認してください。";
            return;
        }
        this.UsesEOS = true;
        this.ErrorMessage = "";
        this.JoinCode = "";
        this.eos = EosMatchmakingService.GetOrCreate(this.manager);
        int attempt = ++this.attemptVersion;
        this.connectionDeadline = Time.unscaledTime + this.preparationTimeout;
        this.SetState(ConnectionState.Preparing);
        _ = this.ConnectEosAsync(attempt);
    }

    private async Task ConnectEosAsync(int attempt)
    {
        try
        {
            bool host = await this.eos.MatchAsync(this.SelectedPlayerCount);
            if (!this.IsCurrentAttempt(attempt)) return;
            this.manager.NetworkConfig.NetworkTransport = this.eos.Transport;
            this.BeginConnection();
            if (host)
            {
                this.StartConfiguredHost();
                if (attempt != this.attemptVersion) return;
                await this.eos.PublishAsync();
            }
            else if (!this.manager.StartClient()) this.Fail("EOSの部屋への接続を開始できませんでした。");
        }
        catch (Exception exception)
        {
            if (this == null || attempt != this.attemptVersion || !this.isActiveAndEnabled) return;
            this.Fail(exception is InvalidOperationException ? exception.Message
                : "EOSに接続できませんでした。通信環境とEOS Configurationを確認してください。");
        }
    }


    private async Task ConnectAsync(bool host, bool relay, string code, int attempt)
    {
        try
        {
            this.manager.NetworkConfig.NetworkTransport = this.transport;
            if (relay)
            {
                await RelayConnectionService.EnsureReadyAsync();
                if (!this.IsCurrentAttempt(attempt)) return;

                if (host)
                {
                    // ゲームは最大4人。Relayの接続枠はホストを除いた人数。
                    var allocation = await RelayService.Instance.CreateAllocationAsync(3);
                    if (!this.IsCurrentAttempt(attempt)) return;

                    string createdCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                    if (!this.IsCurrentAttempt(attempt)) return;

                    this.transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                    this.JoinCode = createdCode;
                }
                else
                {
                    var allocation = await RelayService.Instance.JoinAllocationAsync(code);
                    if (!this.IsCurrentAttempt(attempt)) return;

                    this.transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));
                }
            }
            else
            {
                // Relay使用後でも通常のUDPへ戻す。既存のIP・ポート設定は維持する。
                var address = this.transport.ConnectionData;
                this.transport.SetConnectionData(address.Address, address.Port, address.ServerListenAddress);
            }

            this.BeginConnection();
            if (host)
                this.StartConfiguredHost();
            else if (!this.manager.StartClient())
                this.Fail("参加処理を開始できませんでした。もう一度お試しください。");
        }
        catch (Exception exception)
        {
            if (this == null || attempt != this.attemptVersion || !this.isActiveAndEnabled)
                return;

            // 接続情報や認証トークンをログへ出さない。
            Debug.LogWarning($"接続処理に失敗しました ({exception.GetType().Name})。", this);
            this.Fail(relay
                ? "Relayに接続できませんでした。参加コード・部屋の空き・通信環境・Unity Servicesの設定を確認してください。"
                : "接続を開始できませんでした。IPアドレスと接続設定を確認してください。");
        }
    }


    private bool IsCurrentAttempt(int attempt)
    {
        if (this == null || !this.isActiveAndEnabled || this.manager == null
            || attempt != this.attemptVersion || this.State != ConnectionState.Preparing)
            return false;

        if (Time.unscaledTime >= this.connectionDeadline)
        {
            this.Fail("オンライン接続の準備がタイムアウトしました。もう一度お試しください。");
            return false;
        }

        return true;
    }


    private bool CanStartConnection()
    {
        return this.manager != null
            && this.isActiveAndEnabled
            && this.State == ConnectionState.Idle
            && !this.manager.IsListening
            && !this.manager.ShutdownInProgress
            && (this.eos == null || !this.eos.IsBusy);
    }


    private void BeginConnection()
    {
        this.ErrorMessage = "";

        this.connectionDeadline =
            Time.unscaledTime + this.connectionTimeout;

        this.SetState(ConnectionState.Connecting);
    }


    public void LeaveRoom()
    {
        if (this.State != ConnectionState.Connected && this.State != ConnectionState.Preparing
            && this.State != ConnectionState.Connecting) return;

        SEAudio.Instance?.Play(SEType.Cancel);
        if (UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        if (this.joinCodeInput != null) this.joinCodeInput.SetTextWithoutNotify("");
        this.Fail("");
    }

    private void Fail(string message)
    {
        ++this.attemptVersion;
        this.JoinCode = "";
        this.ErrorMessage = message;
        this.SetState(ConnectionState.Stopping);
        if (this.UsesEOS) this.eos?.Stop();

        if (this.manager != null
            && !this.manager.ShutdownInProgress)
        {
            this.manager.Shutdown();
        }
    }


    private void SetState(ConnectionState state)
    {
        bool connectedNow = this.State != ConnectionState.Connected
            && state == ConnectionState.Connected;

        this.State = state;
        this.RefreshButtons();

        if (connectedNow)
            SEAudio.Instance?.Play(SEType.RoomConnect);
    }


    private void RefreshButtons()
    {
        bool interactable = this.State == ConnectionState.Idle;

        if (this.createServerButton != null)
            this.createServerButton.interactable = interactable;

        if (this.joinServerButton != null)
            this.joinServerButton.interactable = interactable;

        if (this.onlineButton != null)
        {
            this.onlineButton.interactable = interactable;
            this.onlineButton.gameObject.SetActive(this.State != ConnectionState.Connected);
        }

        if (this.leaveRoomButton != null)
        {
            bool connected = this.State == ConnectionState.Connected
                || this.State == ConnectionState.Preparing || this.State == ConnectionState.Connecting;
            this.leaveRoomButton.interactable = connected;
            this.leaveRoomButton.gameObject.SetActive(connected);
        }

        this.RefreshRelayUI();
    }


    private void RefreshRelayUI()
    {
        if (this.joinCodeInput != null)
        {
            this.joinCodeInput.interactable = this.UsesRelay && this.State == ConnectionState.Idle;
            if (this.joinCodeInput.gameObject != this.gameObject)
                this.joinCodeInput.gameObject.SetActive(this.UsesRelay && this.State != ConnectionState.Connected);
        }

        if (this.joinCodeText != null)
        {
            this.joinCodeText.text = this.UsesRelay && this.State == ConnectionState.Connected
                && !string.IsNullOrEmpty(this.JoinCode) ? $"Room Code：{this.JoinCode}" : "";
        }
    }


    private void OnDisable()
    {
        // ラウンド移行時の接続済み通信は維持し、未完了の準備・接続だけ中止する。
        if (this.State == ConnectionState.Preparing || this.State == ConnectionState.Connecting)
            this.Fail("接続を中止しました。");
    }


    private void OnDestroy()
    {
        ++this.attemptVersion;
        if (this.createServerButton != null)
            this.createServerButton.onClick.RemoveListener(
                this.CreateSelectedHost
            );

        if (this.joinServerButton != null)
            this.joinServerButton.onClick.RemoveListener(this.JoinHost);

        if (this.leaveRoomButton != null)
            this.leaveRoomButton.onClick.RemoveListener(this.LeaveRoom);
        if (this.onlineButton != null)
            this.onlineButton.onClick.RemoveListener(this.StartOnlineMatching);
    }
}
