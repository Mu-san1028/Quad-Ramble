using System.Collections;
using UnityEngine;

/// <summary>
/// アイテムボックスの「アニメーション再生だけ」を担当します。
/// 取得判定・復活タイマー・中身の抽選は、呼び出し側で自由に書いてください。
///
/// 付ける場所：
///   FBXを読み込むと item_box(ルート) → ItemBox → 各メッシュ という階層になります。
///   Animator と一緒に「外側のルート」に付けてください。
///
/// 使い方の例：
///   var box = GetComponent&lt;ItemBoxAnimator&gt;();
///   box.PlayHit();                       // 取られた瞬間
///   yield return new WaitForSeconds(4f); // 復活待ちは呼び出し側の都合で
///   box.PlayRespawnThenIdle();           // 復活 → 終わったらIdleへ
/// </summary>
[RequireComponent(typeof(Animator))]
public class ItemBoxAnimation : MonoBehaviour
{
    [Header("Animator のステート名")]
    [SerializeField] string idleState = "ItemBox_Idle";
    [SerializeField] string hitState = "ItemBox_Hit";
    [SerializeField] string respawnState = "ItemBox_Respawn";

    Animator anim;
    Coroutine running;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    public void PlayIdle()
    {
        this.StopRunning();
        anim.Play(idleState, 0, 0f);
    }

    public void PlayHit()
    {
        this.StopRunning();
        anim.Play(hitState, 0, 0f);
    }

    public void PlayRespawn()
    {
        this.StopRunning();
        anim.Play(respawnState, 0, 0f);
    }

    /// <summary>Respawn を再生し、終わったら Idle に戻します。</summary>
    public Coroutine PlayRespawnThenIdle()
    {
        this.StopRunning();
        this.running = StartCoroutine(this.RespawnRoutine());
        return this.running;
    }

    IEnumerator RespawnRoutine()
    {
        anim.Play(respawnState, 0, 0f);        // StopRunning を経由しない
        yield return new WaitForSeconds(RespawnLength);
        anim.Play(idleState, 0, 0f);
        this.running = null;
    }

    private void StopRunning()
    {
        if (this.running == null) return;
        StopCoroutine(this.running);
        this.running = null;
    }

    public float HitLength => GetClipLength(hitState);
    public float RespawnLength => GetClipLength(respawnState);

    /// <summary>
    /// クリップの長さ（秒）。復活までの待ち時間などを自前で組むときに使ってください。
    /// 注意：Play() が使うのは「ステート名」、こちらが照合するのは「クリップ名」です。
    /// クリップをドラッグしてステートを作った場合は同じ名前になるので、そのまま一致します。
    /// 一致しない場合は 0 が返るので、そのときはインスペクタの名前を揃えてください。
    /// </summary>
    public float GetClipLength(string clipName)
    {
        var rc = anim.runtimeAnimatorController;
        if (rc == null) return 0f;
        foreach (var clip in rc.animationClips)
            if (clip.name == clipName) return clip.length;
        return 0f;
    }
}