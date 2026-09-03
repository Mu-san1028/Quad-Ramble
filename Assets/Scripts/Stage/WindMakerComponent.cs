using UnityEngine;

/// <summary>
/// 【非推奨】旧・風ギミック。役割は WindArea + WindReceiver に統合された。
///
/// なぜ残してあるか:
/// シーンやプレハブがこのコンポーネントを参照している可能性があるため、
/// ファイルごと消すと参照が壊れる（Missing Script になる）。
/// そこで、起動時に Inspector の設定値を WindArea へ移し替えて自分を無効化する、
/// 移行専用の中身に置き換えてある。
///
/// 旧実装は次の状態だった:
/// - FixedUpdate のループの中身が空で、風の力が実際には一切加わっていなかった。
/// - 同じ機能を WindReceiver 側でも実装していて、設計が二重化していた。
/// - OnTriggerEnter / OnTriggerExit のたびに GetComponent&lt;PlayerController&gt;() を呼んでいた。
///
/// 移行が済んだら、シーン上からこのコンポーネントを外してよい。
/// </summary>
[System.Obsolete("WindArea と WindReceiver を使ってください。このコンポーネントは起動時に自動で WindArea へ移行します。")]
[AddComponentMenu("")]
public class WindMakerComponent : MonoBehaviour
{
    // Inspector に入っている設定値を拾うため、名前を旧実装のまま（綴りの typo も含めて）残してある。
    [SerializeField] private Vector3 accelration;

    private void Awake()
    {
        WindArea area = this.GetComponent<WindArea>();

        if (area == null)
        {
            area = this.gameObject.AddComponent<WindArea>();

            float magnitude = this.accelration.magnitude;
            area.windDirection = magnitude > 0.000001f ? this.accelration / magnitude : Vector3.forward;
            area.force = magnitude;

            Debug.LogWarning(
                $"[WindMakerComponent] '{this.name}' の設定を WindArea へ移行しました。" +
                "シーンを保存して、このコンポーネントは外してください。", this);
        }

        // 二重に風が吹かないよう、自分は止める。
        this.enabled = false;
    }
}
