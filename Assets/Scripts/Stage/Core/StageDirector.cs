using UnityEngine;

/// <summary>
/// Stage ギミックの唯一の更新元。
///
/// なぜ 1 箇所にまとめるのか:
/// Unity の Update / FixedUpdate は MonoBehaviour ごとにネイティブ側から呼び出される。
/// 回る床を 200 個置けば、中身が空でも 200 回の呼び出しが毎フレーム発生する。
/// ここで配列を 1 周するだけにすれば、その固定費はオブジェクト数によらず 1 回で済む。
/// 「1 本化」で減るのは呼び出しの固定費であって、ギミック 1 個あたりの仕事そのものではない。
/// そこで、ギミック 1 個あたりのコストにも効くよう、更新対象を<b>実装クラスごとの束</b>に分けて回す
/// （<see cref="StageTypeBuckets{T}"/>）。同じ束の中では呼び先の型が 1 つに固定されるので、
/// インターフェース越しの間接呼び出しの分岐予測が当たりやすく、参照するメモリも近くにまとまる。
/// シーンに置く必要はない。最初に Register が呼ばれた時点で自動生成される。
/// </summary>
[DefaultExecutionOrder(-100)]
[AddComponentMenu("")] // Inspector の Add Component には出さない（手で付ける物ではないため）
public sealed class StageDirector : MonoBehaviour
{
    private static StageDirector instance;
    private static bool isShuttingDown;
    private readonly StageTypeBuckets<IStageTick> tickers = new StageTypeBuckets<IStageTick>();
    private readonly StageTypeBuckets<IStageFixedTick> fixedTickers = new StageTypeBuckets<IStageFixedTick>();
    private readonly StageTimerWheel timers = new StageTimerWheel();

    // NetworkManager は後から生成されることがあるので、捕まえるまで時々試す。
    private const int NetworkHookRetryInterval = 120; // フレーム
    private int hookRetryCountdown;
    /// <summary>デバッグ表示用。今フレーム回している対象数。</summary>
    public static int TickCount => instance != null ? instance.tickers.Count : 0;
    public static int FixedTickCount => instance != null ? instance.fixedTickers.Count : 0;
    public static int TimerCount => instance != null ? instance.timers.ActiveCount : 0;

    // Domain Reload を切っている場合、static はシーン再生をまたいで残るので明示的に初期化する。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        isShuttingDown = false;
    }

    private static StageDirector GetOrCreate()
    {
        if (instance != null) return instance;
        if (isShuttingDown) return null;

        var go = new GameObject("[StageDirector]");
        go.hideFlags = HideFlags.DontSave;
        instance = go.AddComponent<StageDirector>();
        return instance;
    }

    // --- 登録 / 解除 ---------------------------------------------------------
    // Register は生成を伴うが、Unregister は決して生成しない。
    // シーン破棄中に Unregister から Director を作り直してしまうのを防ぐため。

    public static void Register(IStageTick t) => GetOrCreate()?.tickers.Add(t);
    public static void Unregister(IStageTick t) => instance?.tickers.Remove(t);

    public static void Register(IStageFixedTick t) => GetOrCreate()?.fixedTickers.Add(t);
    public static void Unregister(IStageFixedTick t) => instance?.fixedTickers.Remove(t);

    /// <summary>delay 秒後に target.OnStageTimer(token) を呼ぶ。Coroutine の代わり。</summary>
    public static void Schedule(IStageTimerTarget target, float delay, int token = 0)
        => GetOrCreate()?.timers.Schedule(target, delay, token);

    /// <summary>予約済みタイマーの取り消し。OnDisable で必ず呼ぶこと。</summary>
    public static void CancelTimers(IStageTimerTarget target) => instance?.timers.CancelAll(target);

    // --- 更新ループ ----------------------------------------------------------

    private void Update()
    {
        float dt = Time.deltaTime;

        // 実装クラスごとの束を順に回す。
        // Count / Items を毎回読み直すのは、Tick の中で登録・解除されても壊れないようにするため。
        var buckets = this.tickers.Buckets;
        for (int b = 0; b < buckets.Count; b++)
        {
            StageRegistry<IStageTick> bucket = buckets[b];
            for (int i = 0; i < bucket.Count; i++)
            {
                bucket.Items[i].Tick(dt);
            }
        }

        this.timers.Advance(dt);

        // 破棄済み Collider を掴んだままにしないための掃除。1 フレームあたり数件だけ見る。
        StageCacheRegistry.SweepAll();

        // NetworkManager が出来ていたら、セッションの開始 / 終了を購読する。
        if (--this.hookRetryCountdown <= 0)
        {
            this.hookRetryCountdown = NetworkHookRetryInterval;
            StageNetworkCacheHook.Install();
        }
    }

    private void FixedUpdate()
    {
        float fdt = Time.fixedDeltaTime;

        var buckets = this.fixedTickers.Buckets;
        for (int b = 0; b < buckets.Count; b++)
        {
            StageRegistry<IStageFixedTick> bucket = buckets[b];
            for (int i = 0; i < bucket.Count; i++)
            {
                bucket.Items[i].FixedTick(fdt);
            }
        }
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        this.tickers.Clear();
        this.fixedTickers.Clear();
        this.timers.Clear();
        instance = null;
    }

    private void OnApplicationQuit()
    {
        isShuttingDown = true;
    }
}
