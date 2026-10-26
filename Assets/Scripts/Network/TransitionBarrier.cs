using System.Collections.Generic;

/// <summary>遷移開始時の参加者のみを対象に、各段階の完了通知を待つ。</summary>
public sealed class TransitionBarrier
{
    private readonly HashSet<ulong> pending = new();
    public int Token { get; private set; }
    public byte Step { get; private set; }
    public bool Complete => this.pending.Count == 0;

    public void Begin(int token, byte step, IEnumerable<ulong> participants)
    {
        this.Token = token;
        this.Step = step;
        this.pending.Clear();
        foreach (ulong id in participants) this.pending.Add(id);
    }

    public void Acknowledge(int token, byte step, ulong sender)
    {
        if (token == this.Token && step == this.Step) this.pending.Remove(sender);
    }

    public void Remove(ulong disconnected) => this.pending.Remove(disconnected);

    public ulong[] Pending()
    {
        var result = new ulong[this.pending.Count];
        this.pending.CopyTo(result);
        return result;
    }
}
