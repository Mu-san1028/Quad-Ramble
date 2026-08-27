using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collider から目的のコンポーネントを引くときの結果キャッシュ。
///
/// なぜ要るのか:
/// OnTriggerEnter / OnTriggerExit / OnCollisionEnter は、プレイヤーが歩き回るだけで何度も呼ばれる。
/// そのたびに GetComponent を叩くと、GameObject のコンポーネント配列を毎回走査することになる。
/// さらに「Player ではない Collider」に対する空振りの GetComponent が一番多い。
/// ここでは "見つからなかった" という結果も覚えるので、空振りのコストが 2 回目以降ゼロになる。
///
/// 注意: Collider の InstanceID をキーにしている。シーンを跨いだら自動で Clear される。
/// </summary>
public static class StageCache<T> where T : Component
{
    private static readonly Dictionary<int, T> map = new Dictionary<int, T>(64);

    static StageCache()
    {
        StageCacheRegistry.RegisterClear(Clear);
    }

    /// <summary>
    /// col から T を引く（親方向も探す）。付いていなければ null。
    /// </summary>
    public static T Get(Collider col)
    {
        if (col == null) return null;

        int id = col.GetInstanceID();

        if (map.TryGetValue(id, out T cached))
        {
            // 参照そのものが null = 「以前調べたが付いていなかった」という記録。空振りを即返す。
            if (ReferenceEquals(cached, null)) return null;

            // 参照はあるが Unity 的に null = 破棄済み。この場合だけ引き直す。
            if ((Component)cached != null) return cached;
        }

        T found = col.GetComponentInParent<T>();
        map[id] = found;
        return found;
    }

    public static void Clear() => map.Clear();
}

/// <summary>
/// StageCache&lt;T&gt; は型ごとに別々の静的クラスになるため、まとめて消す窓口を用意しておく。
/// </summary>
public static class StageCacheRegistry
{
    private static readonly List<Action> clears = new List<Action>(8);

    // Domain Reload を切っていると static が再生をまたいで残る。
    // 登録簿そのものは残して構わない（中身は静的メソッドへの参照なので）が、
    // キャッシュの中身は前回のシーンの InstanceID なので必ず捨てる。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ClearAll();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ClearAll();

    internal static void RegisterClear(Action clear)
    {
        if (clear != null) clears.Add(clear);
    }

    /// <summary>全キャッシュを破棄する。シーン切り替え時に自動で呼ばれる。</summary>
    public static void ClearAll()
    {
        for (int i = 0; i < clears.Count; i++) clears[i]();
    }
}
