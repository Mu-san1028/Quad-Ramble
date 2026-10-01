using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ラウンドの得点の決め方。シーンに置かれていればそれが使われ、
/// 無ければ RoundManager が従来どおり順位制で処理する。
/// </summary>
public abstract class RoundScoring : NetworkBehaviour
{
    /// <summary>サーバーのみ。ラウンド終了時に呼ばれる</summary>
    public abstract void AwardPoints();
}
