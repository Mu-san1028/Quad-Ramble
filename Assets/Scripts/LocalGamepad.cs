using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// この「プロセス」が使うゲームパッドを1台だけ決める。
/// 1PCで4ウィンドウ起動するブース構成のために席番号でパッドを選び分ける。
/// オンライン（1PCに1プロセス）では席番号 0 のまま、自分の唯一のパッドに当たる。
/// </summary>
public static class LocalGamepad
{
    private static bool resolved;
    private static Gamepad cached;

    public static int Seat { get; private set; }

    /// <summary>割り当てられたパッド。無ければ null（＝ペアリングしない）</summary>
    public static Gamepad Current
    {
        get
        {
            // 起動後に挿された場合に備え、null のうちは引き直す
            if (!resolved || cached == null) Resolve();
            return cached;
        }
    }

    private static void Resolve()
    {
        Seat = ReadSeat();
        var pads = Gamepad.all;
        cached = (Seat >= 0 && Seat < pads.Count) ? pads[Seat] : null;
        resolved = true;

        Debug.Log($"[Input] seat={Seat} pads={pads.Count} -> " +
                  $"{(cached != null ? cached.displayName : "なし")}");
    }

    private static int ReadSeat()
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-seat" && int.TryParse(args[i + 1], out int n)) return n;

        return 0;
    }
}
