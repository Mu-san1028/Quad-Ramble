using UnityEngine;

/// <summary>
/// 踏むと少し遅れて消える床。
///
/// 旧実装からの変更点:
/// - コメントが文字化けしていたので書き直した（Shift-JIS のまま保存されていた）。
/// - 中身が空の Start / Update を削除した。
/// - Coroutine + WaitForSeconds をやめ、StageDirector のタイマーに寄せた。
///   WaitForSeconds は消えるたびに new されるため、床の数だけゴミが出ていた。
/// - Renderer / Collider を Awake で 1 度だけ取得するようにした。
/// - Renderer.material への代入をやめ、sharedMaterial の差し替えにした。
///   .material は触るたびにマテリアルの複製を作り、シーンを抜けるまで解放されない。
/// - Destroy をやめ、表示と当たり判定を切るだけにした。
///   ラウンド制のゲームなので、次のラウンドで ResetFloor() すれば元に戻せる。
///   従来どおり消滅させたい場合は destroyOnVanish を有効にする。
/// </summary>
public class Vanishing_Floor : MonoBehaviour, IStageTimerTarget
{
    [Tooltip("踏まれてから消えるまでの秒数")]
    [SerializeField] private float waitingTime = 3f;

    /// <summary>外部から消えるまでの時間を変えられる。</summary>
    public float WaitingTime
    {
        get { return this.waitingTime; }
        set { this.waitingTime = value; }
    }

    [Tooltip("消える予兆として差し替えるマテリアル")]
    [SerializeField] private Material SwappedMaterial;

    [Tooltip("有効にすると旧実装と同じく GameObject ごと破棄する。既定は非表示にするだけ（元に戻せる）。")]
    [SerializeField] private bool destroyOnVanish = false;

    [SerializeField] private bool verboseLog = false;

    private Renderer floorRenderer;
    private Collider floorCollider;
    private Material originalMaterial;
    private bool isVanishing;

    private const int VanishToken = 1;

    /// <summary>消える処理が始まっているか。</summary>
    public bool IsVanishing => this.isVanishing;

    private void Awake()
    {
        this.floorRenderer = this.GetComponent<Renderer>();
        this.floorCollider = this.GetComponent<Collider>();

        // 差し替え前のマテリアルを覚えておく（共有アセットなので複製は発生しない）
        if (this.floorRenderer != null) this.originalMaterial = this.floorRenderer.sharedMaterial;
    }

    private void OnDisable()
    {
        // 破棄・無効化のときに予約が残らないようにする
        StageDirector.CancelTimers(this);
    }

    /// <summary>消滅処理を開始する。二重に呼ばれても 1 回しか進まない。</summary>
    public void FloorVanish()
    {
        if (this.isVanishing) return;
        this.isVanishing = true;

        if (this.verboseLog) Debug.Log($"[Vanishing_Floor] 消滅処理開始: {Time.time}", this);

        // 予兆としてマテリアルを差し替える
        if (this.floorRenderer != null && this.SwappedMaterial != null)
        {
            this.floorRenderer.sharedMaterial = this.SwappedMaterial;
        }

        StageDirector.Schedule(this, this.waitingTime, VanishToken);
    }

    public void OnStageTimer(int token)
    {
        if (token != VanishToken) return;

        if (this.verboseLog) Debug.Log($"[Vanishing_Floor] 消滅処理完了: {Time.time}", this);

        if (this.destroyOnVanish)
        {
            Destroy(this.gameObject);
            return;
        }

        // 表示と当たり判定だけ切る。GameObject は生かしておくので元に戻せる。
        if (this.floorRenderer != null) this.floorRenderer.enabled = false;
        if (this.floorCollider != null) this.floorCollider.enabled = false;
    }

    /// <summary>次のラウンド用に床を元に戻す。</summary>
    public void ResetFloor()
    {
        StageDirector.CancelTimers(this);
        this.isVanishing = false;

        if (this.floorRenderer != null)
        {
            this.floorRenderer.enabled = true;
            if (this.originalMaterial != null) this.floorRenderer.sharedMaterial = this.originalMaterial;
        }
        if (this.floorCollider != null) this.floorCollider.enabled = true;
    }
}
