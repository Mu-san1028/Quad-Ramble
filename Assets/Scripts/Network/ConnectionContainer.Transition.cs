using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ConnectionContainer
{
    [Header("シーン遷移の同期")]
    [SerializeField, Min(5f)] private float transitionAckTimeout = 30f;
    [SerializeField, Min(10f)] private float transitionLoadTimeout = 150f;

    public bool IsTransitioning { get; private set; }
    private readonly TransitionBarrier transitionBarrier = new();
    private readonly HashSet<ulong> transitionParticipants = new();
    private Coroutine transitionRoutine;
    private Coroutine localTransitionRoutine;
    private NetworkSceneManager transitionSceneManager;
    private string transitionScene;
    private int transitionToken;
    private bool transitionLoaded;
    private bool transitionFailed;


    public void BeginSceneTransition(string sceneName, int round, bool results)
    {
        if (!IsServer || !IsSpawned || this.IsTransitioning) return;

        this.IsTransitioning = true;
        this.transitionToken++;
        this.transitionScene = sceneName;
        this.transitionLoaded = false;
        this.transitionFailed = false;
        this.transitionParticipants.Clear();
        foreach (ulong id in NetworkManager.ConnectedClientsIds) this.transitionParticipants.Add(id);
        this.transitionRoutine = StartCoroutine(this.TransitionToScene(round, results));
    }


    private IEnumerator TransitionToScene(int round, bool results)
    {
        this.transitionBarrier.Begin(this.transitionToken, 0, this.transitionParticipants);
        this.CloseTransitionRpc(this.transitionToken, round, results);
        yield return this.WaitForTransitionAcks();
        if (this.transitionFailed || !IsSpawned) yield break;

        this.transitionSceneManager = NetworkManager.SceneManager;
        this.transitionSceneManager.OnLoadEventCompleted += this.TransitionLoadCompleted;
        var status = this.transitionSceneManager.LoadScene(this.transitionScene, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            this.FailTransition($"シーンを読み込めません: {this.transitionScene} ({status})");
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + this.transitionLoadTimeout;
        while (!this.transitionLoaded)
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                this.FailTransition("シーン読み込みが完了しませんでした。");
                yield break;
            }
            yield return null;
        }

        // RoundManagerの既存OnLoadEventCompleted→配置処理を再利用する。
        while (!results && (RoundManager.Instance == null || !RoundManager.Instance.IsReady))
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                this.FailTransition("ラウンドの初期化が完了しませんでした。");
                yield break;
            }
            yield return null;
        }

        this.RemoveDisconnectedParticipants();
        this.transitionBarrier.Begin(this.transitionToken, 1, this.transitionParticipants);
        this.PrepareTransitionRpc(this.transitionToken, this.transitionScene, round, results);
        yield return this.WaitForTransitionAcks();
        if (this.transitionFailed || !IsSpawned) yield break;

        this.RemoveDisconnectedParticipants();
        this.transitionBarrier.Begin(this.transitionToken, 2, this.transitionParticipants);
        // 暗転解除とStageIntroを同じサーバー時刻で開始する。
        double revealAt = NetworkManager.ServerTime.Time + 0.35d;
        if (!results) RoundManager.Instance.BeginPresentation(revealAt);
        this.OpenTransitionRpc(this.transitionToken, revealAt);
        yield return this.WaitForTransitionAcks();
        if (this.transitionFailed || !IsSpawned) yield break;

        this.FinishTransitionRpc(this.transitionToken);
        this.IsTransitioning = false;
        this.transitionRoutine = null;
        this.UnsubscribeTransitionScene();
    }


    private IEnumerator WaitForTransitionAcks()
    {
        float deadline = Time.realtimeSinceStartup + this.transitionAckTimeout;
        while (!this.transitionBarrier.Complete)
        {
            this.RemoveDisconnectedParticipants();
            if (Time.realtimeSinceStartup > deadline)
            {
                foreach (ulong id in this.transitionBarrier.Pending())
                {
                    if (id == NetworkManager.ServerClientId)
                    {
                        this.FailTransition("ホストの画面・プレイヤー準備が完了しませんでした。");
                        yield break;
                    }

                    Debug.LogWarning($"[Transition] 準備待機タイムアウト: ClientId={id}", this);
                    NetworkManager.DisconnectClient(id);
                    this.transitionParticipants.Remove(id);
                    this.transitionBarrier.Remove(id);
                }
            }
            yield return null;
        }
    }


    private void RemoveDisconnectedParticipants()
    {
        this.transitionParticipants.RemoveWhere(id => !NetworkManager.ConnectedClients.ContainsKey(id));
        foreach (ulong id in this.transitionBarrier.Pending())
            if (!this.transitionParticipants.Contains(id)) this.transitionBarrier.Remove(id);
    }


    private void TransitionLoadCompleted(string sceneName, LoadSceneMode mode,
        List<ulong> completed, List<ulong> timedOut)
    {
        if (!IsServer || sceneName != this.transitionScene) return;

        foreach (ulong id in timedOut)
        {
            if (id == NetworkManager.ServerClientId) continue;
            if (NetworkManager.ConnectedClients.ContainsKey(id)) NetworkManager.DisconnectClient(id);
            this.transitionParticipants.Remove(id);
            this.transitionBarrier.Remove(id);
        }
        this.transitionLoaded = completed.Contains(NetworkManager.ServerClientId);
    }


    [Rpc(SendTo.ClientsAndHost)]
    private void CloseTransitionRpc(int token, int round, bool results)
    {
        this.transitionToken = token;
        this.IsTransitioning = true;
        SceneTransitionUI.EnsureInstance().Close(round, results);
        this.StartLocalTransitionWait(this.WaitForCover(token));
    }


    private IEnumerator WaitForCover(int token)
    {
        while (!SceneTransitionUI.Instance.IsFullyCovered) yield return null;
        this.TransitionAcknowledgedRpc(token, 0);
    }


    [Rpc(SendTo.ClientsAndHost)]
    private void PrepareTransitionRpc(int token, string sceneName, int round, bool results)
    {
        if (token != this.transitionToken) return;
        this.StartLocalTransitionWait(this.WaitForLocalPreparation(token, sceneName, round, results));
    }


    private IEnumerator WaitForLocalPreparation(int token, string sceneName, int round, bool results)
    {
        while (true)
        {
            var scene = SceneManager.GetActiveScene();
            var playerObject = NetworkManager.LocalClient?.PlayerObject;
            var player = playerObject != null ? playerObject.GetComponent<PlayerController>() : null;
            bool ready = scene.name == sceneName && scene.isLoaded;
            if (!results)
            {
                var manager = RoundManager.Instance;
                ready &= manager != null && manager.IsSpawned && manager.IsReady
                    && manager.gameObject.scene.name == sceneName
                    && player != null && player.LastPlacementRound == round;
                if (ready) SceneTransitionUI.Instance.SetStageName(manager.StageName);
            }

            if (ready && SceneTransitionUI.Instance.MinimumHoldComplete) break;
            yield return null;
        }
        // Start/LateUpdateによるカメラやUIの初期化も、覆われた状態で1フレーム反映する。
        yield return null;
        this.TransitionAcknowledgedRpc(token, 1);
    }


    [Rpc(SendTo.ClientsAndHost)]
    private void OpenTransitionRpc(int token, double revealAt)
    {
        if (token != this.transitionToken) return;
        this.StartLocalTransitionWait(this.WaitForOpening(token, revealAt));
    }


    private IEnumerator WaitForOpening(int token, double revealAt)
    {
        while (NetworkManager.ServerTime.Time < revealAt) yield return null;
        if (token != this.transitionToken) yield break;
        SceneTransitionUI.Instance.Open();
        while (!SceneTransitionUI.Instance.IsVisuallyOpen) yield return null;
        this.TransitionAcknowledgedRpc(token, 2);
    }


    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TransitionAcknowledgedRpc(int token, byte step, RpcParams rpcParams = default)
    {
        if (!this.IsTransitioning) return;
        this.transitionBarrier.Acknowledge(token, step, rpcParams.Receive.SenderClientId);
    }


    [Rpc(SendTo.ClientsAndHost)]
    private void FinishTransitionRpc(int token)
    {
        if (token != this.transitionToken) return;
        this.IsTransitioning = false;
        SceneTransitionUI.Instance?.ReleaseInput();
    }


    private void StartLocalTransitionWait(IEnumerator routine)
    {
        if (this.localTransitionRoutine != null) StopCoroutine(this.localTransitionRoutine);
        this.localTransitionRoutine = StartCoroutine(routine);
    }


    private void FailTransition(string reason)
    {
        this.transitionFailed = true;
        Debug.LogError($"[Transition] {reason} タイトルへ戻ります。", this);
        this.UnsubscribeTransitionScene();
        this.AbortTransitionRpc();
    }


    [Rpc(SendTo.ClientsAndHost)]
    private void AbortTransitionRpc() => GameManager.Instance.ReturnToTitle();


    private void UnsubscribeTransitionScene()
    {
        if (this.transitionSceneManager == null) return;
        this.transitionSceneManager.OnLoadEventCompleted -= this.TransitionLoadCompleted;
        this.transitionSceneManager = null;
    }


    private void CleanupTransition()
    {
        this.UnsubscribeTransitionScene();
        if (this.transitionRoutine != null) StopCoroutine(this.transitionRoutine);
        if (this.localTransitionRoutine != null) StopCoroutine(this.localTransitionRoutine);
        this.transitionRoutine = this.localTransitionRoutine = null;
        this.IsTransitioning = false;
        if (SceneTransitionUI.InputBlocked) GameManager.Instance?.ReturnToTitle();
    }


    private void HandleTransitionJoin(ulong clientId)
    {
        if (IsServer && this.IsTransitioning && !this.transitionParticipants.Contains(clientId))
            NetworkManager.DisconnectClient(clientId);
    }
}
