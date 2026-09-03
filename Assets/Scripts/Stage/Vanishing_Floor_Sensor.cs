using UnityEngine;

/// <summary>
/// 消える床の踏み判定。子にある Vanishing_Floor に消滅を伝える。
///
/// 旧実装からの変更点:
/// - コメントが文字化けしていたので書き直した（Shift-JIS のまま保存されていた）。
/// - Debug.Log がタグ判定より前にあり、プレイヤー以外の Collider が触れるたびに
///   ログが出ていた（毎回文字列を作るのでゴミにもなる）。判定の後ろへ移した。
/// - Coroutine + WaitForSeconds をやめ、StageDirector のタイマーに寄せた。
/// - Destroy をやめ、当たり判定を切るだけにした。床側と同じく ResetSensor() で戻せる。
///   旧実装は床を Destroy した後もセンサーが床への参照を持っており、
///   ラウンドをまたぐと破棄済みオブジェクトを触りにいく状態だった。
/// </summary>
public class Vanishing_Floor_Sensor : MonoBehaviour, IStageTimerTarget
{
    private Vanishing_Floor VFScript;
    private Collider sensorCollider;
    private bool isSwitchedOn;

    [Tooltip("床が消えてから、さらにこの秒数だけ待ってセンサーを止める（多重発火の防止）")]
    [SerializeField] private float extraDisableDelay = 1f;

    [SerializeField] private bool verboseLog = false;

    private const int DisableToken = 2;

    private void Awake()
    {
        this.VFScript = this.GetComponentInChildren<Vanishing_Floor>();
        this.sensorCollider = this.GetComponent<Collider>();

        if (this.VFScript == null)
        {
            Debug.LogError($"[Vanishing_Floor_Sensor] '{this.name}' の子に Vanishing_Floor がありません。", this);
        }
    }

    private void OnDisable() => StageDirector.CancelTimers(this);

    private void OnTriggerEnter(Collider collider)
    {
        if (this.isSwitchedOn) return;
        if (!collider.CompareTag("Player")) return;
        if (this.VFScript == null) return;

        if (this.verboseLog) Debug.Log("<color=yellow>消える床の判定にプレイヤーが接触しました。</color>", this);

        this.isSwitchedOn = true;
        this.VFScript.FloorVanish();

        // 床が消えたあとに再度発火しないよう、少し遅らせてセンサーを止める。
        StageDirector.Schedule(this, this.VFScript.WaitingTime + this.extraDisableDelay, DisableToken);
    }

    public void OnStageTimer(int token)
    {
        if (token != DisableToken) return;

        if (this.verboseLog) Debug.Log($"[Vanishing_Floor_Sensor] センサー停止: {Time.time}", this);

        if (this.sensorCollider != null) this.sensorCollider.enabled = false;
    }

    /// <summary>次のラウンド用にセンサーと床をまとめて元に戻す。</summary>
    public void ResetSensor()
    {
        StageDirector.CancelTimers(this);
        this.isSwitchedOn = false;

        if (this.sensorCollider != null) this.sensorCollider.enabled = true;
        if (this.VFScript != null) this.VFScript.ResetFloor();
    }
}
