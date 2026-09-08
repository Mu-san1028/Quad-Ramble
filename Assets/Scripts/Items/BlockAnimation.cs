using UnityEngine;

[RequireComponent(typeof(Animator))]
public class BlockAnimation : MonoBehaviour
{
    [Header("Animator のステート名")]
    [SerializeField] string idleState = "Block_Idle";
    [SerializeField] string hitState = "Block_Hit";
    [SerializeField] string breakState = "Block_Break";

    Animator anim;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    // 第3引数の 0f で毎回頭出しする。連続ヒットでも途中から再生されない
    public void PlayIdle()  => anim.Play(idleState,  0, 0f);
    public void PlayHit()   => anim.Play(hitState,   0, 0f);
    public void PlayBreak() => anim.Play(breakState, 0, 0f);

    public float HitLength   => GetClipLength(hitState);
    public float BreakLength => GetClipLength(breakState);

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