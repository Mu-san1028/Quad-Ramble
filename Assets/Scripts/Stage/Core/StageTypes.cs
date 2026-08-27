using System;
using System.Collections.Generic;

/// <summary>
/// Stage ギミック共通の型定義。
///
/// 設計方針:
/// - ギミックは自前の Update / FixedUpdate を持たない。StageDirector が 1 本のループから呼ぶ。
///   (Unity の Update は MonoBehaviour ごとにネイティブ→マネージドの呼び出しが発生するため、
///    オブジェクト数に比例して固定費がかさむ)
/// - 遅延処理に Coroutine を使わない。StageTimerWheel が構造体の配列で管理する。
///   (StartCoroutine + WaitForSeconds は呼ぶたびにヒープ確保が発生する)
/// </summary>

/// <summary>毎フレーム更新されるギミック。</summary>
public interface IStageTick
{
    void Tick(float deltaTime);
}

/// <summary>物理フレームごとに更新されるギミック。</summary>
public interface IStageFixedTick
{
    void FixedTick(float fixedDeltaTime);
}

/// <summary>
/// StageTimerWheel から時間切れを通知される対象。
/// コールバックをラムダではなくインターフェースにしているのは、クロージャの確保を避けるため。
/// token は 1 つのオブジェクトが複数のタイマーを使い分けるための識別子。
/// </summary>
public interface IStageTimerTarget
{
    void OnStageTimer(int token);
}

/// <summary>
/// 追加 O(1) / 削除 O(1) / 走査は配列そのままの登録簿。
/// 走査中に要素が増減しても落ちないよう、StageDirector 側は毎回 Count と Items を読み直す。
/// </summary>
public sealed class StageRegistry<T> where T : class
{
    private T[] items = new T[16];
    private int count;
    private readonly Dictionary<T, int> indexOf = new Dictionary<T, int>(16);

    public T[] Items => this.items;
    public int Count => this.count;

    public void Add(T item)
    {
        if (item == null) return;
        if (this.indexOf.ContainsKey(item)) return; // 二重登録を防ぐ

        if (this.count == this.items.Length) Array.Resize(ref this.items, this.count * 2);

        this.items[this.count] = item;
        this.indexOf[item] = this.count;
        this.count++;
    }

    public void Remove(T item)
    {
        if (item == null) return;
        if (!this.indexOf.TryGetValue(item, out int i)) return;

        this.indexOf.Remove(item);
        this.count--;

        // 末尾を空いた穴へ詰める。順序は保証しないが、削除が O(1) になる。
        if (i != this.count)
        {
            T moved = this.items[this.count];
            this.items[i] = moved;
            this.indexOf[moved] = i;
        }
        this.items[this.count] = null;
    }

    public void Clear()
    {
        Array.Clear(this.items, 0, this.count);
        this.count = 0;
        this.indexOf.Clear();
    }
}

/// <summary>
/// 「n 秒後に呼び返す」を Coroutine なしで実現するタイマー。
/// 実体は構造体の配列で、1 回の予約あたりのヒープ確保はゼロ。
/// </summary>
public sealed class StageTimerWheel
{
    private struct Entry
    {
        public IStageTimerTarget Target;
        public float DueTime;
        public int Token;
    }

    private Entry[] entries = new Entry[32];
    private int count;
    private float now;

    public int ActiveCount => this.count;

    /// <summary>delay 秒後に target.OnStageTimer(token) を呼ぶ。</summary>
    public void Schedule(IStageTimerTarget target, float delay, int token = 0)
    {
        if (target == null) return;

        if (this.count == this.entries.Length) Array.Resize(ref this.entries, this.count * 2);

        this.entries[this.count].Target = target;
        this.entries[this.count].DueTime = this.now + (delay > 0f ? delay : 0f);
        this.entries[this.count].Token = token;
        this.count++;
    }

    /// <summary>対象が予約したタイマーをすべて取り消す。オブジェクト破棄時に必ず呼ぶこと。</summary>
    public void CancelAll(IStageTimerTarget target)
    {
        if (target == null) return;

        for (int i = 0; i < this.count; )
        {
            if (ReferenceEquals(this.entries[i].Target, target))
            {
                this.entries[i] = this.entries[--this.count];
                this.entries[this.count] = default;
            }
            else i++;
        }
    }

    /// <summary>時間を進め、期限の来たものを発火させる。</summary>
    public void Advance(float deltaTime)
    {
        this.now += deltaTime;

        for (int i = 0; i < this.count; )
        {
            if (this.entries[i].DueTime > this.now) { i++; continue; }

            // 発火前にリストから外す。コールバックの中で再予約されても壊れない。
            Entry fired = this.entries[i];
            this.entries[i] = this.entries[--this.count];
            this.entries[this.count] = default;

            fired.Target?.OnStageTimer(fired.Token);
        }
    }

    public void Clear()
    {
        Array.Clear(this.entries, 0, this.count);
        this.count = 0;
        this.now = 0f;
    }
}
