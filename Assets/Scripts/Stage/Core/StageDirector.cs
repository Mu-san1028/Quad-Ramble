using UnityEngine;

/// <summary>
/// Stage ギミックの唯一の更新元。
///
/// なぜ 1 箇所にまとめるのか:
/// Unity の Update / FixedUpdate は MonoBehaviour ごとにネイティブ側から呼び出される。
/// 回る床を 200 個置けば、中身が空でも 200 回の呼び出しが毎フレーム発生する。
/// ここで配列を 1 周するだけにすれば、その固定費はオブジェクト数によらず 1 回で済む。
///
/// シーンに置く必要はない。最初に Register が呼ばれた時点で自動生成される。
/// </summary>
[DefaultExecutionOrder(-100)]
[AddComponentMenu("")] // Inspector の Add Component には出さない（手で付ける物ではないため）
public sealed class StageDirector : MonoBehaviour
{
    private static StageDirector instance;
    private static bool isShuttingDown;

    private readonly StageRegistry<IStageTick> tickers = new StageRegistry<IStageTick>();
    private readonly StageRegistry<IStageFixedTick> fixedTickers = new StageRegistry<IStageFixedTick>();
    private readonly StageTimerWheel timers = new StageTimerWheel();

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

        // Count / Items を毎回読み直すのは、Tick の中で登録・解除されても壊れないようにするため。
        for (int i = 0; i < this.tickers.Count; i++)
        {
            this.tickers.Items[i].Tick(dt);
        }

        this.timers.Advance(dt);
    }

    private void FixedUpdate()
    {
        float fdt = Time.fixedDeltaTime;

        for (int i = 0; i < this.fixedTickers.Count; i++)
        {
            this.fixedTickers.Items[i].FixedTick(fdt);
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
