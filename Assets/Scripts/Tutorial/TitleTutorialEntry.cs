using System.Collections;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleTutorialEntry : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private NetworkConnect connection;
    private bool starting;

    private void Awake() => button.onClick.AddListener(Begin);

    private void Update()
    {
        bool available = !starting && connection != null
            && connection.State == NetworkConnect.ConnectionState.Idle
            && !connection.IsEditingJoinCode && !SceneTransitionUI.InputBlocked;
        button.interactable = available;
        if (available && ((LocalGamepad.Current?.selectButton.wasPressedThisFrame ?? false)
            || (LocalGamepad.Seat == 0 && (Keyboard.current?.tKey.wasPressedThisFrame ?? false)))) Begin();
    }

    public void Begin()
    {
        var manager = NetworkManager.Singleton;
        if (starting || SceneTransitionUI.InputBlocked || connection == null
            || connection.State != NetworkConnect.ConnectionState.Idle
            || connection.IsEditingJoinCode || manager == null || manager.IsListening) return;
        if (!Application.CanStreamedLevelBeLoaded("Tutorial"))
        {
            label.text = "チュートリアルを読み込めません";
            return;
        }
        starting = true;
        // タイトルUIと切り離して、読み込み・ホストの終了まで設定を保持する。
        new GameObject("TutorialSession").AddComponent<TutorialSession>().Begin();
    }
}

public class TutorialSession : MonoBehaviour
{
    public static bool Active { get; private set; }
    private UnityTransport transport;
    private UnityTransport.ConnectionAddressData address;
    private NetworkTransport previousTransport;

    public void Begin()
    {
        DontDestroyOnLoad(gameObject);
        Active = true;
        StartCoroutine(StartSession());
    }

    private IEnumerator StartSession()
    {
        var manager = NetworkManager.Singleton;
        transport = manager.GetComponent<UnityTransport>();
        previousTransport = manager.NetworkConfig.NetworkTransport;
        manager.NetworkConfig.NetworkTransport = transport;
        address = transport.ConnectionData;
        // 空きポートをOSに割り当ててもらい、展示用の他プロセスと競合させない。
        transport.SetConnectionData(true, "127.0.0.1", 0, "127.0.0.1");
        GameManager.Instance.ResetForNewGame();
        yield return SceneManager.LoadSceneAsync("Tutorial");
        if (!manager.StartHost())
        {
            Debug.LogError("チュートリアルのローカルセッションを開始できませんでした。");
            GameManager.Instance.ReturnToTitle();
        }
        // 通常のReturnToTitleがShutdownを完了してから接続先を復元する。
        while (SceneManager.GetActiveScene().name != "Title") yield return null;
        transport.SetConnectionData(false, address.Address, address.Port, address.ServerListenAddress);
        transport.ConnectionData = address;
        manager.NetworkConfig.NetworkTransport = previousTransport;
        Active = false;
        Destroy(gameObject);
    }

    private void OnDestroy() => Active = false;
}
