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
/// - 見た目の切り替えを MaterialPropertyBlock に変更した（下記）。
/// - Destroy をやめ、表示と当たり判定を切るだけにした。
///   ラウンド制のゲームなので、次のラウンドで ResetFloor() すれば元に戻せる。
///   従来どおり消滅させたい場合は destroyOnVanish を有効にする。
///
/// ◆ 見た目の切り替え方について
/// 旧実装の <c>Renderer.material</c> は、触るたびにマテリアルの複製を作り、シーンを抜けるまで解放されない。
/// かといって <c>sharedMaterial</c> の<b>中身</b>（色など）を書き換えると、
/// そのマテリアルを共有している床すべてが同時に変わってしまい、1 枚だけ消える演出が成立しない。
/// そこで既定は <see cref="MaterialPropertyBlock"/> による上書きにした。
///   - 複製を作らないのでヒープに残らない（ブロックは使い回すので確保もゼロ）
///   - 効くのはこの Renderer だけなので、他の床には影響しない
/// SwappedMaterial に「元と別のシェーダー」のマテリアルが入っている場合だけ、
/// 色の上書きでは再現できないので、この Renderer のマテリアル参照そのものを差し替える。
/// このときも共有アセットの中身は一切書き換えないので、他の床には影響しない。
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

    [Tooltip("消える予兆の見た目。同じシェーダーなら色だけを借りて上書きする（複製しない）。")]
    [SerializeField] private Material SwappedMaterial;

    [Tooltip("有効にすると旧実装と同じく GameObject ごと破棄する。既定は非表示にするだけ（元に戻せる）。")]
    [SerializeField] private bool destroyOnVanish = false;

    [SerializeField] private bool verboseLog = false;

    // 色の上書き先。URP は _BaseColor、ビルトインは _Color。
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // 全床で 1 個を使い回す。MaterialPropertyBlock は使い回せばヒープ確保が起きない。
    private static MaterialPropertyBlock sharedBlock;

    private Renderer floorRenderer;
    private Collider floorCollider;
    private Material originalMaterial;
    private bool isVanishing;

    // 予兆の見せ方。Awake で 1 度だけ決める。
    private enum SwapMode { None, PropertyBlock, MaterialReference }
    private SwapMode swapMode = SwapMode.None;
    private Color swappedColor;
    private int swappedColorId;

    private const int VanishToken = 1;

    /// <summary>消える処理が始まっているか。</summary>
    public bool IsVanishing => this.isVanishing;

    private void Awake()
    {
        this.floorRenderer = this.GetComponent<Renderer>();
        this.floorCollider = this.GetComponent<Collider>();

        // 差し替え前のマテリアルを覚えておく（共有アセットなので複製は発生しない）
        if (this.floorRenderer != null) this.originalMaterial = this.floorRenderer.sharedMaterial;

        this.DecideSwapMode();
    }

    /// <summary>予兆の見せ方を決める。色だけで済むなら PropertyBlock、無理なら参照の差し替え。</summary>
    private void DecideSwapMode()
    {
        if (this.floorRenderer == null || this.SwappedMaterial == null)
        {
            this.swapMode = SwapMode.None;
            return;
        }

        // シェーダーが違うと色の上書きでは再現できない（見た目が別物になる）。
        if (this.originalMaterial == null || this.originalMaterial.shader != this.SwappedMaterial.shader)
        {
            this.swapMode = SwapMode.MaterialReference;
            return;
        }

        if (this.SwappedMaterial.HasProperty(BaseColorId))
        {
            this.swappedColorId = BaseColorId;
        }
        else if (this.SwappedMaterial.HasProperty(ColorId))
        {
            this.swappedColorId = ColorId;
        }
        else
        {
            // 色の持ち方が分からないものは、無理に上書きせず参照差し替えに任せる。
            this.swapMode = SwapMode.MaterialReference;
            return;
        }

        this.swappedColor = this.SwappedMaterial.GetColor(this.swappedColorId);
        this.swapMode = SwapMode.PropertyBlock;
    }

    /// <summary>
    /// 予兆マテリアルを実行中に差し替える（手続き的にステージを組む場合用）。
    /// 見せ方（色の上書きか参照の差し替えか）もここで決め直す。
    /// </summary>
    public void SetSwappedMaterial(Material material)
    {
        this.SwappedMaterial = material;

        if (this.floorRenderer == null)
        {
            this.floorRenderer = this.GetComponent<Renderer>();
            if (this.floorRenderer != null && this.originalMaterial == null)
            {
                this.originalMaterial = this.floorRenderer.sharedMaterial;
            }
        }

        this.DecideSwapMode();
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

        this.ApplyVanishingLook();

        StageDirector.Schedule(this, this.waitingTime, VanishToken);
    }

    /// <summary>消える予兆の見た目にする。この Renderer だけに効く。</summary>
    private void ApplyVanishingLook()
    {
        switch (this.swapMode)
        {
            case SwapMode.PropertyBlock:
                if (sharedBlock == null) sharedBlock = new MaterialPropertyBlock();

                // 既に付いている上書き内容を読んでから足す（他の仕組みが色以外を入れていても消さない）
                this.floorRenderer.GetPropertyBlock(sharedBlock);
                sharedBlock.SetColor(this.swappedColorId, this.swappedColor);
                this.floorRenderer.SetPropertyBlock(sharedBlock);
                break;

            case SwapMode.MaterialReference:
                // この Renderer が指すマテリアルを差し替えるだけ。共有アセットの中身は書き換えない。
                this.floorRenderer.sharedMaterial = this.SwappedMaterial;
                break;
        }
    }

    /// <summary>予兆の見た目を元に戻す。</summary>
    private void ClearVanishingLook()
    {
        switch (this.swapMode)
        {
            case SwapMode.PropertyBlock:
                if (sharedBlock == null) sharedBlock = new MaterialPropertyBlock();

                this.floorRenderer.GetPropertyBlock(sharedBlock);
                sharedBlock.SetColor(this.swappedColorId, this.originalMaterial.GetColor(this.swappedColorId));
                this.floorRenderer.SetPropertyBlock(sharedBlock);
                break;

            case SwapMode.MaterialReference:
                if (this.originalMaterial != null) this.floorRenderer.sharedMaterial = this.originalMaterial;
                break;
        }
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
            this.ClearVanishingLook();
        }
        if (this.floorCollider != null) this.floorCollider.enabled = true;
    }
}
