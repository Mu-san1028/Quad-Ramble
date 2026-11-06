using System;
using System.IO;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>NetworkManager上で生存し、ラウンド遷移後もEOSロビーを保持する。</summary>
public sealed class EosMatchmakingService : MonoBehaviour
{
    // ネットワーク互換性を破る変更時は更新する。別バージョンの部屋を混ぜない。
    private const string Protocol = "quadramble-ngo-v1";
    public string LobbyId { get; private set; }
    public bool IsHost { get; private set; }
    public bool IsBusy => busy || pendingLeaves > 0;
    public string Status { get; private set; } = "";
    public EosNetworkTransport Transport { get; private set; }

    private LobbyInterface lobbies;
    private ProductUserId user;
    private ProductUserId owner;
    private int generation, pendingLeaves;
    private bool busy, closingLobby, quitting;
    private ulong memberNotification;
    private Task initialization;

    public static EosMatchmakingService GetOrCreate(NetworkManager manager)
    {
        var service = manager.GetComponent<EosMatchmakingService>();
        return service != null ? service : manager.gameObject.AddComponent<EosMatchmakingService>();
    }

    private async Task EnsureReadyAsync()
    {
        if (initialization == null || initialization.IsFaulted || initialization.IsCanceled)
            initialization = InitializeAsync();
        await initialization;
    }

    private async Task InitializeAsync()
    {
        Status = "";
        if (!File.Exists(Path.Combine(Application.streamingAssetsPath, "EOS", "eos_product_config.json")))
            throw new InvalidOperationException("EOS未設定です。EOS Plugin → EOS Configurationで設定してください。");
        var product = Config.Get<ProductConfig>();
        if (product.ProductId == Guid.Empty)
            throw new InvalidOperationException("EOS未設定です。EOS Plugin → EOS ConfigurationでProduct・Deployment・Clientを設定してください。");
        var platformConfig = PlatformManager.GetPlatformConfig();
        if (!platformConfig.deployment.IsComplete || platformConfig.clientCredentials == null
            || !platformConfig.clientCredentials.IsComplete)
            throw new InvalidOperationException("EOSのDeploymentとClient Credentialsを設定してください。");
        if (FindFirstObjectByType<EOSManager>() == null)
            new GameObject("EOSManager").AddComponent<EOSManager>();
        if (EOSManager.Instance.GetEOSPlatformInterface() == null)
            throw new InvalidOperationException("EOSを初期化できません。EOS Configurationの設定を確認してください。");
        user = EOSManager.Instance.GetProductUserId();
        if (user == null || !user.IsValid())
        {
            var device = new TaskCompletionSource<Result>();
            var options = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel };
            EOSManager.Instance.GetEOSPlatformInterface().GetConnectInterface().CreateDeviceId(ref options, null,
                (ref CreateDeviceIdCallbackInfo info) => device.TrySetResult(info.ResultCode));
            var result = await device.Task;
            if (result != Result.Success && result != Result.DuplicateNotAllowed) Check(result, "デバイス認証");
            var login = new TaskCompletionSource<LoginCallbackInfo>();
            EOSManager.Instance.StartConnectLoginWithDeviceToken("Player", info => login.TrySetResult(info));
            var loggedIn = await login.Task;
            if (loggedIn.ResultCode == Result.InvalidUser)
            {
                var create = new TaskCompletionSource<CreateUserCallbackInfo>();
                EOSManager.Instance.CreateConnectUserWithContinuanceToken(loggedIn.ContinuanceToken,
                    info => create.TrySetResult(info));
                Check((await create.Task).ResultCode, "ユーザー作成");
                // 再ログインでEOSManagerのトークン更新通知も登録する。
                login = new TaskCompletionSource<LoginCallbackInfo>();
                EOSManager.Instance.StartConnectLoginWithDeviceToken("Player", info => login.TrySetResult(info));
                loggedIn = await login.Task;
            }
            Check(loggedIn.ResultCode, "ログイン");
            user = EOSManager.Instance.GetProductUserId();
        }
        lobbies = EOSManager.Instance.GetEOSPlatformInterface().GetLobbyInterface();
        Transport = GetComponent<EosNetworkTransport>();
        if (Transport == null) Transport = gameObject.AddComponent<EosNetworkTransport>();
        Transport.CanAcceptPeer = IsLobbyMember;
        Transport.SessionStopped = Stop;
        GetComponent<NetworkManager>().OnClientStopped += HandleClientStopped;
        var notify = new AddNotifyLobbyMemberStatusReceivedOptions();
        memberNotification = lobbies.AddNotifyLobbyMemberStatusReceived(ref notify, null,
            (ref LobbyMemberStatusReceivedCallbackInfo info) =>
            {
                if (info.LobbyId.ToString() != LobbyId || info.CurrentStatus == LobbyMemberStatus.Joined) return;
                bool lostLocal = info.TargetUserId.Equals(user) && info.CurrentStatus != LobbyMemberStatus.Promoted;
                bool lostHost = info.TargetUserId.Equals(owner) && info.CurrentStatus != LobbyMemberStatus.Promoted;
                if (lostLocal || lostHost)
                {
                    Status = "EOSロビーとの接続が切れました。";
                    GetComponent<NetworkManager>().Shutdown();
                    Stop();
                }
            });
    }

    public async Task<bool> MatchAsync(int players)
    {
        if (IsBusy || !string.IsNullOrEmpty(LobbyId)) throw new InvalidOperationException("EOSの接続終了を待ってください。");
        busy = true;
        int attempt = ++generation;
        try
        {
            await EnsureReadyAsync();
            EnsureCurrent(attempt);
            Status = "空いている部屋を探しています…";
            string bucket = $"{Protocol}:{Application.version}:{players}";
            // 同時検索時に全員が即座にホストになる確率を下げる。
            await Task.Delay(UnityEngine.Random.Range(150, 850));
            EnsureCurrent(attempt);
            if (await TryJoinAsync(bucket, attempt)) return false;
            EnsureCurrent(attempt);
            Status = "オンラインの部屋を作成しています…";
            var create = new CreateLobbyOptions
            {
                LocalUserId = user, MaxLobbyMembers = (uint)players, BucketId = bucket,
                PermissionLevel = LobbyPermissionLevel.Inviteonly,
                DisableHostMigration = true, PresenceEnabled = false, AllowInvites = false
            };
            var completion = new TaskCompletionSource<CreateLobbyCallbackInfo>();
            lobbies.CreateLobby(ref create, null, (ref CreateLobbyCallbackInfo info) => completion.TrySetResult(info));
            var created = await completion.Task;
            Check(created.ResultCode, "部屋作成");
            string id = created.LobbyId.ToString();
            if (attempt != generation) { ReleaseLobby(id); EnsureCurrent(attempt); }
            LobbyId = id;
            IsHost = true;
            owner = user;
            closingLobby = false;
            // NGOホストの起動が完了するまでは検索結果に公開しない。
            return true;
        }
        catch
        {
            if (attempt == generation) Stop();
            throw;
        }
        finally { busy = false; }
    }

    private async Task<bool> TryJoinAsync(string bucket, int attempt)
    {
        var options = new CreateLobbySearchOptions { MaxResults = 20 };
        Check(lobbies.CreateLobbySearch(ref options, out var search), "部屋検索の準備");
        try
        {
            var parameter = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = LobbyInterface.SEARCH_BUCKET_ID, Value = new AttributeDataValue { AsUtf8 = bucket } },
                ComparisonOp = ComparisonOp.Equal
            };
            Check(search.SetParameter(ref parameter), "検索条件設定");
            var find = new LobbySearchFindOptions { LocalUserId = user };
            var completion = new TaskCompletionSource<Result>();
            search.Find(ref find, null, (ref LobbySearchFindCallbackInfo info) => completion.TrySetResult(info.ResultCode));
            Check(await completion.Task, "部屋検索");
            EnsureCurrent(attempt);
            var countOptions = new LobbySearchGetSearchResultCountOptions();
            uint count = search.GetSearchResultCount(ref countOptions);
            for (uint i = 0; i < count; i++)
            {
                var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                if (search.CopySearchResultByIndex(ref copy, out var details) != Result.Success) continue;
                try
                {
                    var infoOptions = new LobbyDetailsCopyInfoOptions();
                    if (details.CopyInfo(ref infoOptions, out var info) != Result.Success || !info.HasValue
                        || info.Value.AvailableSlots == 0 || info.Value.LobbyOwnerUserId.Equals(user)) continue;
                    var join = new JoinLobbyOptions { LocalUserId = user, LobbyDetailsHandle = details };
                    var joined = new TaskCompletionSource<JoinLobbyCallbackInfo>();
                    lobbies.JoinLobby(ref join, null, (ref JoinLobbyCallbackInfo result) => joined.TrySetResult(result));
                    var result = await joined.Task;
                    if (attempt != generation)
                    {
                        if (result.ResultCode == Result.Success) ReleaseLobby(result.LobbyId.ToString());
                        EnsureCurrent(attempt);
                    }
                    // 検索後に満室・開始済みになった部屋はスキップする。
                    if (result.ResultCode == Result.LobbyTooManyPlayers || result.ResultCode == Result.NotFound
                        || result.ResultCode == Result.LobbyNoPermission || result.ResultCode == Result.LobbySessionInProgress) continue;
                    Check(result.ResultCode, "部屋参加");
                    LobbyId = result.LobbyId.ToString();
                    owner = info.Value.LobbyOwnerUserId;
                    IsHost = false;
                    Transport.HostUserId = owner;
                    Status = "見つかった部屋に接続しています…";
                    return true;
                }
                finally { details.Release(); }
            }
            return false;
        }
        finally { search.Release(); }
    }

    public async Task PublishAsync()
    {
        busy = true;
        int attempt = generation;
        try
        {
            await SetPublishedAsync(true);
            EnsureCurrent(attempt);
            Status = "参加者を待っています…";
        }
        finally { busy = false; }
    }

    private async Task SetPublishedAsync(bool published)
    {
        var update = new UpdateLobbyModificationOptions { LobbyId = LobbyId, LocalUserId = user };
        Check(lobbies.UpdateLobbyModification(ref update, out var modification), "部屋更新準備");
        try
        {
            var permission = new LobbyModificationSetPermissionLevelOptions
            { PermissionLevel = published ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly };
            Check(modification.SetPermissionLevel(ref permission), "公開設定");
            var commit = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var done = new TaskCompletionSource<Result>();
            lobbies.UpdateLobby(ref commit, null, (ref UpdateLobbyCallbackInfo info) => done.TrySetResult(info.ResultCode));
            Check(await done.Task, "部屋公開更新");
        }
        finally { modification.Release(); }
    }

    public bool IsLobbyMember(ProductUserId peer)
    {
        if (string.IsNullOrEmpty(LobbyId) || peer == null || !IsHost) return false;
        var container = ConnectionContainer.Instance;
        var manager = GetComponent<NetworkManager>();
        if (container == null || container.IsTransitioning || container.CurrentRound > 0
            || manager.ConnectedClientsIds.Count >= container.RequiredPlayerCount) return false;
        var copy = new CopyLobbyDetailsHandleOptions { LobbyId = LobbyId, LocalUserId = user };
        if (lobbies.CopyLobbyDetailsHandle(ref copy, out var details) != Result.Success) return false;
        try
        {
            var countOptions = new LobbyDetailsGetMemberCountOptions();
            uint count = details.GetMemberCount(ref countOptions);
            for (uint i = 0; i < count; i++)
            {
                var index = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                if (peer.Equals(details.GetMemberByIndex(ref index))) return true;
            }
            return false;
        }
        finally { details.Release(); }
    }

    private void Update()
    {
        if (!IsHost || string.IsNullOrEmpty(LobbyId) || closingLobby) return;
        var container = ConnectionContainer.Instance;
        if (container == null || (!container.IsTransitioning && container.CurrentRound == 0)) return;
        closingLobby = true;
        _ = CloseSearchAsync();
    }

    private async Task CloseSearchAsync()
    {
        try { await SetPublishedAsync(false); }
        catch (Exception) { Debug.LogWarning("EOSロビーを非公開にできませんでした。途中参加はNGO側で拒否します。"); }
    }

    public void Stop()
    {
        ++generation;
        string id = LobbyId;
        LobbyId = null;
        IsHost = false;
        if (!string.IsNullOrEmpty(id)) ReleaseLobby(id);
    }

    private void ReleaseLobby(string id)
    {
        pendingLeaves++;
        var options = new LeaveLobbyOptions { LobbyId = id, LocalUserId = user };
        lobbies.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo info) =>
        {
            pendingLeaves--;
            if (info.ResultCode != Result.Success && info.ResultCode != Result.NotFound)
                Debug.LogWarning($"EOS部屋の退出に失敗しました ({info.ResultCode})。");
        });
    }

    private void EnsureCurrent(int attempt)
    {
        if (attempt != generation) throw new OperationCanceledException();
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success) throw new InvalidOperationException($"EOS {operation}に失敗しました ({result})。");
    }

    private void HandleClientStopped(bool wasHost)
    {
        // タイトルのNetworkConnectはラウンド移行で破棄されるため、切断後の復帰はここで行う。
        var manager = GetComponent<NetworkManager>();
        if (!quitting && manager.NetworkConfig.NetworkTransport == Transport && SceneManager.GetActiveScene().name != "Title")
            GameManager.Instance?.ReturnToTitle();
    }

    private void OnApplicationQuit() => quitting = true;

    private void OnDestroy()
    {
        var manager = GetComponent<NetworkManager>();
        if (manager != null) manager.OnClientStopped -= HandleClientStopped;
        // アプリ終了時はEOSManagerがPlatformを解放する。解放後のnative handleには触れない。
        if (quitting || EOSManager.Instance.GetEOSPlatformInterface() == null) return;
        Stop();
        if (memberNotification != 0) lobbies?.RemoveNotifyLobbyMemberStatusReceived(memberNotification);
    }
}
