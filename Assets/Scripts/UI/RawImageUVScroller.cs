using UnityEngine;
using UnityEngine.UI;

/// <summary>RawImageの柄を、時間停止の影響を受けずに繰り返しスクロールする。</summary>
[RequireComponent(typeof(RawImage))]
[DisallowMultipleComponent]
public class RawImageUVScroller : MonoBehaviour
{
    [Header("柄の移動速度（UV / 秒）")]
    [Tooltip("Xが正なら右、Yが正なら上へ流れます。画像のWrap ModeはRepeatにしてください。")]
    [SerializeField] private Vector2 scrollSpeed = new Vector2(0.02f, 0.01f);

    private RawImage background;

    private void Awake()
    {
        this.background = GetComponent<RawImage>();
    }

    private void Update()
    {
        if (this.scrollSpeed == Vector2.zero)
            return;

        Rect uv = this.background.uvRect;
        float dt = Time.unscaledDeltaTime;

        // 表示範囲を逆向きにずらすことで、柄を指定方向へ流す。
        // 幅・高さは維持し、座標だけを0〜1の範囲で循環させる。
        uv.x = Mathf.Repeat(uv.x - this.scrollSpeed.x * dt, 1f);
        uv.y = Mathf.Repeat(uv.y - this.scrollSpeed.y * dt, 1f);

        this.background.uvRect = uv;
    }
}
