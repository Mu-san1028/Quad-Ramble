using UnityEngine;

/// <summary>
/// クイズなど、特定の床を消すギミックに使用するスクリプト。
/// CheckAnswer に正解の ID を渡すと、ID が一致しない床が消える。
/// 逆に特定の ID の床だけを消したい場合は DeleteFloorByID を使う。
///
/// 旧実装からの変更点:
/// - Renderer / Collider を Awake で 1 度だけ取得するようにした。
///   旧実装は消える／戻るたびに GetComponent を計 4 回叩いていた。
/// - Coroutine + WaitForSeconds をやめ、StageDirector のタイマーに寄せた。
/// - 復活までの秒数がベタ書きだったので Inspector から変えられるようにした。
/// - QuizManager に自分から登録するようにした（QuizManager 側の全件検索をなくすため）。
/// </summary>
public class QuizFloor : MonoBehaviour, IStageTimerTarget
{
    [SerializeField] private int floorID; // インスペクターで各床にユニークな ID を設定

    [Tooltip("消えてから元に戻るまでの秒数")]
    [SerializeField] private float restoreSeconds = 5f;

    [SerializeField] private bool verboseLog = false;

    private Renderer floorRenderer;
    private Collider floorCollider;
    private bool isDissolved;

    private const int RestoreToken = 3;

    public int FloorID => this.floorID;
    public bool IsDissolved => this.isDissolved;

    private void Awake()
    {
        this.floorRenderer = this.GetComponent<Renderer>();
        this.floorCollider = this.GetComponent<Collider>();
    }

    private void OnEnable() => QuizManager.Instance?.RegisterFloor(this);

    private void OnDisable()
    {
        StageDirector.CancelTimers(this);
        QuizManager.UnregisterFloor(this);
    }

    /// <summary>正解 ID を受け取り、自分の ID と違えば消える。</summary>
    public void CheckAnswer(int correctAnswerID)
    {
        if (this.floorID == correctAnswerID)
        {
            if (this.verboseLog) Debug.Log("正解！床はそのままです。", this);
            return;
        }

        this.Dissolve();
    }

    /// <summary>指定 ID と一致する床だけを消す。</summary>
    public void DeleteFloorByID(int targetID)
    {
        if (this.floorID != targetID) return;

        if (this.verboseLog) Debug.Log("ID が一致しました。床を消します。", this);
        this.Dissolve();
    }

    /// <summary>床を消す。restoreSeconds 後に自動で戻る。</summary>
    public void Dissolve()
    {
        if (this.isDissolved) return;
        this.isDissolved = true;

        // GameObject 自体は有効のままにして、見た目と当たり判定だけ切る。
        // こうしないと、無効化した GameObject 上ではタイマーの解除も復帰も動かせない。
        if (this.floorRenderer != null) this.floorRenderer.enabled = false;
        if (this.floorCollider != null) this.floorCollider.enabled = false;

        StageDirector.Schedule(this, this.restoreSeconds, RestoreToken);
    }

    public void OnStageTimer(int token)
    {
        if (token != RestoreToken) return;
        this.Restore();
    }

    /// <summary>床を即座に元に戻す。</summary>
    public void Restore()
    {
        StageDirector.CancelTimers(this);

        if (this.floorRenderer != null) this.floorRenderer.enabled = true;
        if (this.floorCollider != null) this.floorCollider.enabled = true;

        this.isDissolved = false;
    }
}
