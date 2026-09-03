using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ネットワークの節目で StageCache の記録を無効化するための接続部。
///
/// なぜ要るのか:
/// StageCache が覚えているのは「この Collider に T が付いているか」という参照解決の結果だけだが、
/// ネットワークでは同じ Collider に対する答えが途中で変わる。
///   - セッション開始 / 終了: シーン上のオブジェクトが総入れ替えになる
///   - Spawn / Despawn      : 個々のオブジェクトの有無が変わる
/// 前者をここで、後者を <see cref="StageCacheInvalidator"/> で受け持つ。
/// </summary>
public static class StageNetworkCacheHook
{
    private static NetworkManager bound;

    /// <summary>NetworkManager が居れば購読する。まだ居なければ何もしない（後で再試行される）。</summary>
    public static void Install()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm == bound) return;

        Unbind();

        nm.OnServerStarted += OnSessionChanged;
        nm.OnClientStarted += OnSessionChanged;
        nm.OnServerStopped += OnSessionStopped;
        nm.OnClientStopped += OnSessionStopped;

        bound = nm;
    }

    private static void Unbind()
    {
        if (bound == null) { bound = null; return; }

        bound.OnServerStarted -= OnSessionChanged;
        bound.OnClientStarted -= OnSessionChanged;
        bound.OnServerStopped -= OnSessionStopped;
        bound.OnClientStopped -= OnSessionStopped;
        bound = null;
    }

    private static void OnSessionChanged() => StageCacheRegistry.Invalidate();
    private static void OnSessionStopped(bool _) => StageCacheRegistry.Invalidate();
}

/// <summary>
/// Spawn / Despawn の瞬間に、そのオブジェクト配下の Collider についての記録だけを捨てる。
///
/// 付ける場所:
/// 走る途中で Spawn / Despawn されうる Stage 側の NetworkObject（動的に湧く床や風など）。
/// 静的に置きっぱなしのギミックには要らない。
///
/// 全体を無効化しないのは、1 個の Despawn でシーン全部のキャッシュを捨てると効きがなくなるため。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageCacheInvalidator : NetworkBehaviour
{
    public override void OnNetworkSpawn() => StageCacheRegistry.InvalidateObject(this.gameObject);

    public override void OnNetworkDespawn() => StageCacheRegistry.InvalidateObject(this.gameObject);

    /// <summary>実行中に Collider やギミックを付け外ししたときは、これを呼ぶ。</summary>
    public void NotifyComponentsChanged() => StageCacheRegistry.InvalidateObject(this.gameObject);
}
