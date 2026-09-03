using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collider から目的のコンポーネントを引くときの「検索結果」キャッシュ。
///
/// なぜ要るのか:
/// OnTriggerEnter / OnTriggerExit / OnCollisionEnter は、プレイヤーが歩き回るだけで何度も呼ばれる。
/// そのたびに GetComponent を叩くと、GameObject のコンポーネント配列を毎回走査することになる。
/// さらに「Player ではない Collider」に対する空振りの GetComponent が一番多い。
/// ここでは "見つからなかった" という結果も覚えるので、空振りのコストが 2 回目以降ほぼゼロになる。
///
/// ◆ ここが覚えてよいもの / 覚えてはいけないもの（マルチプレイでの取り扱い）
/// 覚えるのは「この Collider に T が付いているか」という <b>ローカルプロセス内の参照解決の結果</b> だけ。
/// IsServer / IsOwner / NetworkVariable の値など、<b>権威やネットワークで変わる状態は一切キャッシュしない</b>。
/// 参照解決の結果はサーバーとクライアントで別々に持って構わない値（各プロセスのメモリ配置の話）なので、
/// これがサーバー権威の判定とクライアントの見た目をずらす原因にはならない。
/// 逆に言うと、判定そのものをここに載せてはいけない。
///
/// ◆ 無効化（Despawn / 遅延 Spawn 対策）
/// ネットワークでは、同じ Collider に対する答えが途中で変わる。
///   - Despawn: 付いていたコンポーネントが消える     → 肯定キャッシュが古くなる
///   - 遅延 Spawn: 後から AddComponent / Spawn される → 否定（空振り）キャッシュが古くなる
/// そこで無効化を 3 段構えにしている。
///   (1) 世代印: <see cref="StageCacheRegistry.Generation"/> が上がると、それ以前の記録は全部無視される。
///       シーン切り替え、NetworkManager の開始 / 停止、明示的な <see cref="StageCacheRegistry.Invalidate"/> で上がる。
///   (2) 対象指定の無効化: <see cref="StageCacheRegistry.InvalidateObject"/> でその GameObject 配下の Collider だけ捨てる。
///       Spawn / Despawn の瞬間に呼ぶのが本筋（<see cref="StageCacheInvalidator"/> が自動で呼ぶ）。
///   (3) 空振りキャッシュの寿命: 呼び忘れがあっても最大 <see cref="NegativeEntryLifetimeFrames"/> フレームで自然に消える。
///       取りこぼしても必ず自己修復する、という保険。
///
/// ◆ 破棄済みオブジェクトを掴み続けない
/// キーは Collider の InstanceID だが、破棄検知のため Collider 参照も一緒に持つ。
/// StageDirector が毎フレーム少しずつ <see cref="Sweep"/> を回し、破棄された Collider の項目を捨てる。
/// これで、辞書が死んだ参照を溜め続けることはない。
/// </summary>
public static class StageCache<T> where T : Component
{
    /// <summary>空振り（付いていない）という記録を信用するフレーム数。取りこぼし時の自己修復用。</summary>
    public const int NegativeEntryLifetimeFrames = 120; // 60fps で約 2 秒

    private struct Entry
    {
        public T Component;      // ReferenceEquals(null) = 「調べたが付いていなかった」という記録
        public Collider Source;  // 破棄検知（掃除）のためだけに持つ
        public int Generation;   // 記録した時点の世代
        public int Frame;        // 記録した時点のフレーム（空振りの寿命判定用）
    }

    private static readonly Dictionary<int, Entry> map = new Dictionary<int, Entry>(64);

    // 掃除で順番に見て回るためのキー一覧。map と足並みを揃える必要はない（ズレは掃除側で吸収する）。
    private static readonly List<int> keys = new List<int>(64);
    private static int sweepCursor;

    static StageCache()
    {
        StageCacheRegistry.Register(Clear, Remove, Sweep);
    }

    /// <summary>デバッグ表示用。今持っている項目数。</summary>
    public static int Count => map.Count;

    /// <summary>
    /// col から T を引く（親方向も探す）。付いていなければ null。
    /// </summary>
    public static T Get(Collider col)
    {
        if (col == null) return null;

        int id = col.GetInstanceID();
        int generation = StageCacheRegistry.Generation;

        if (map.TryGetValue(id, out Entry cached) && cached.Generation == generation)
        {
            if (ReferenceEquals(cached.Component, null))
            {
                // 空振りの記録。ただし寿命付き。
                // 遅延 Spawn で後から付く場合に備え、一定フレームを過ぎたら引き直す。
                if (Time.frameCount - cached.Frame < NegativeEntryLifetimeFrames) return null;
            }
            else if ((Component)cached.Component != null)
            {
                // 参照はあるが Unity 的に null = 破棄済み。その場合はここに来ないので引き直しになる。
                return cached.Component;
            }
        }

        T found = col.GetComponentInParent<T>();
        Store(id, found, col, generation);
        return found;
    }

    private static void Store(int id, T found, Collider source, int generation)
    {
        if (!map.ContainsKey(id)) keys.Add(id);

        map[id] = new Entry
        {
            Component = found,
            Source = source,
            Generation = generation,
            Frame = Time.frameCount,
        };
    }

    /// <summary>指定 Collider の記録だけを捨てる。</summary>
    public static void Remove(int colliderInstanceId) => map.Remove(colliderInstanceId);

    /// <summary>
    /// 破棄済み Collider の項目を少しずつ捨てる。StageDirector から毎フレーム呼ばれる。
    /// 一度に全部見ないのは、項目数に比例したコストを 1 フレームに集中させないため。
    /// </summary>
    public static void Sweep(int budget)
    {
        if (keys.Count == 0) return;

        for (int n = 0; n < budget && keys.Count > 0; n++)
        {
            if (sweepCursor >= keys.Count) sweepCursor = 0;

            int key = keys[sweepCursor];

            // 辞書側に無い / 記録元の Collider が破棄されている / 世代が古い項目は、
            // 掴んだままにせずここで捨てる。
            bool dead = !map.TryGetValue(key, out Entry e)
                        || e.Source == null
                        || e.Generation != StageCacheRegistry.Generation;

            if (!dead)
            {
                sweepCursor++;
                continue;
            }

            map.Remove(key);

            // 末尾を空いた穴へ詰める（順序は要らないので O(1)）。
            // 詰めた結果、同じ位置に別のキーが来るので cursor は進めない。
            int last = keys.Count - 1;
            keys[sweepCursor] = keys[last];
            keys.RemoveAt(last);
        }
    }

    public static void Clear()
    {
        map.Clear();
        keys.Clear();
        sweepCursor = 0;
    }
}

/// <summary>
/// StageCache&lt;T&gt; は型ごとに別々の静的クラスになるため、まとめて操作する窓口を用意しておく。
/// </summary>
public static class StageCacheRegistry
{
    private static readonly List<Action> clears = new List<Action>(8);
    private static readonly List<Action<int>> removers = new List<Action<int>>(8);
    private static readonly List<Action<int>> sweepers = new List<Action<int>>(8);

    // InvalidateObject で子の Collider を集めるときの受け皿。毎回 new しないよう使い回す。
    private static readonly List<Collider> colliderBuffer = new List<Collider>(8);

    /// <summary>
    /// 記録の世代。上げると、それ以前に記録した内容は（肯定・否定とも）すべて無視される。
    /// </summary>
    public static int Generation { get; private set; } = 1;

    /// <summary>1 フレームあたりに掃除する項目数。</summary>
    public static int SweepBudgetPerFrame = 4;

    // Domain Reload を切っていると static が再生をまたいで残る。
    // 登録簿そのものは残して構わない（中身は静的メソッドへの参照なので）が、
    // キャッシュの中身は前回のシーンの InstanceID なので必ず捨てる。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Generation = 1;
        ClearAll();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;       
        StageNetworkCacheHook.Install();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Invalidate();

    internal static void Register(Action clear, Action<int> remove, Action<int> sweep)
    {
        if (clear != null) clears.Add(clear);
        if (remove != null) removers.Add(remove);
        if (sweep != null) sweepers.Add(sweep);
    }

    /// <summary>
    /// 記録を全部無効にする（世代を進める）。
    /// シーン切り替え、ネットワークセッションの開始 / 終了で自動的に呼ばれる。
    /// </summary>
    public static void Invalidate()
    {
        Generation++;
        if (Generation == int.MaxValue) Generation = 1;
    }

    /// <summary>
    /// 指定 GameObject 配下の Collider についての記録だけを捨てる。
    /// Spawn / Despawn / AddComponent の直後に呼ぶ。全体を捨てないので効きが落ちない。
    /// </summary>
    public static void InvalidateObject(GameObject go)
    {
        if (go == null) return;

        colliderBuffer.Clear();
        go.GetComponentsInChildren(true, colliderBuffer);

        for (int i = 0; i < colliderBuffer.Count; i++)
        {
            int id = colliderBuffer[i].GetInstanceID();
            for (int c = 0; c < removers.Count; c++) removers[c](id);
        }

        colliderBuffer.Clear();
    }

    /// <summary>全キャッシュを空にする。</summary>
    public static void ClearAll()
    {
        for (int i = 0; i < clears.Count; i++) clears[i]();
    }

    /// <summary>破棄済み参照の掃除。StageDirector が毎フレーム少しずつ呼ぶ。</summary>
    public static void SweepAll()
    {
        int budget = SweepBudgetPerFrame;
        for (int i = 0; i < sweepers.Count; i++) sweepers[i](budget);
    }
}
