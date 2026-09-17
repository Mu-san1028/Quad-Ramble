using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 「どのプレイヤーが、どのセーブポイントまで進んだか」を持つ台帳。サーバー専用。
///
/// 置き場所について:
/// - シーンに Manager オブジェクトを増やしたくなかったので静的クラスにしている。
///   ステージごとに設定するものは何も無く、持つのは実行中の記録だけ。
/// - キーは PlayerController。人数分しか増えないので辞書のコストは問題にならない。
///
/// 記録を捨てる場所は 3 か所。
///   (1) ラウンド開始時: PlayerSpawner.SpawnPlayer が <see cref="Clear"/> を呼ぶ。
///   (2) シーン切り替え時: 別ステージの座標を持ち越さないよう <see cref="ClearAll"/>。
///   (3) Domain Reload を切っている場合の再生開始時（static が前の再生から残るため）。
/// </summary>
public static class SavePointRegistry
{
    private struct Entry
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public int Order;
        public int RespawnCount;
        public bool HasSavePoint;   // セーブポイントを 1 つでも通ったか

        // ラウンド開始地点。セーブポイント未通過のときの復帰先として使う（使うかは呼び出し側が決める）
        public Vector3 StartPosition;
        public Quaternion StartRotation;
        public bool HasStart;
    }

    private static readonly Dictionary<PlayerController, Entry> records = new Dictionary<PlayerController, Entry>(8);

    // Domain Reload を切っていると static が再生をまたいで残る。前回の座標を必ず捨てる。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => records.Clear();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ClearAll();

    /// <summary>
    /// 通過を記録する。記録が更新されたら true。
    /// 既に同じか先のセーブポイントを通っている場合は何もしない（戻る方向に通っても巻き戻らない）。
    /// </summary>
    public static bool Record(PlayerController player, SavePoint point)
    {
        if (point == null) return false;
        return Record(player, point.Order, point.RespawnPosition, point.RespawnRotation);
    }

    /// <summary>
    /// 通過を記録する（値で受け取る版）。クライアントから RPC で届いた通過報告はこちらを使う。
    /// </summary>
    public static bool Record(PlayerController player, int order, Vector3 position, Quaternion rotation)
    {
        if (player == null) return false;

        records.TryGetValue(player, out Entry current);
        if (current.HasSavePoint && current.Order >= order) return false;

        // 開始地点と復帰回数は引き継ぐ
        current.Position = position;
        current.Rotation = rotation;
        current.Order = order;
        current.HasSavePoint = true;
        records[player] = current;
        return true;
    }

    /// <summary>
    /// ラウンドの開始地点を覚える。PlayerSpawner がスポーン時に呼ぶ。
    /// セーブポイントの記録は消さない（呼ぶ前に <see cref="Clear"/> しておくこと）。
    /// </summary>
    public static void RecordStart(PlayerController player, Vector3 position, Quaternion rotation)
    {
        if (player == null) return;

        records.TryGetValue(player, out Entry current);
        current.StartPosition = position;
        current.StartRotation = rotation;
        current.HasStart = true;
        records[player] = current;
    }

    /// <summary>
    /// 復帰地点を 1 回分取り出す。取れたら復帰回数を 1 増やして true。
    /// limit が負なら無制限。limit 回を超えたら false を返すので、呼び出し側は従来どおり脱落させる。
    /// useStart が true なら、セーブポイント未通過でも開始地点へ戻す。
    /// </summary>
    public static bool TryConsumeRespawn(PlayerController player, int limit, bool useStart, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (player == null) return false;
        if (!records.TryGetValue(player, out Entry record)) return false;
        if (limit >= 0 && record.RespawnCount >= limit) return false;

        if (record.HasSavePoint)
        {
            position = record.Position;
            rotation = record.Rotation;
        }
        else if (useStart && record.HasStart)
        {
            position = record.StartPosition;
            rotation = record.StartRotation;
        }
        else
        {
            return false;
        }

        record.RespawnCount++;
        records[player] = record;
        return true;
    }

    /// <summary>そのプレイヤーが復帰した回数。UI などから見る用。</summary>
    public static int GetRespawnCount(PlayerController player)
    {
        if (player == null) return 0;
        return records.TryGetValue(player, out Entry record) ? record.RespawnCount : 0;
    }

    /// <summary>そのプレイヤーの記録だけ捨てる。ラウンド開始時に呼ぶ。</summary>
    public static void Clear(PlayerController player)
    {
        if (player == null) return;
        records.Remove(player);
    }

    public static void ClearAll() => records.Clear();
}
