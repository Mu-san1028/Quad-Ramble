using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーに付ける。今入っている WindArea をまとめて 1 本の加速度にして PlayerController に渡す。
///
/// 旧実装からの変更点:
/// - コメントが文字化けしていたので書き直した（Shift-JIS のまま保存されていた）。
/// - 自前の FixedUpdate をやめ、StageDirector の一括ループから呼ばれるようにした。
/// - HashSet&lt;WindArea&gt; を List に変えた。同時に入る風は多くても数個で、
///   その規模では List の線形探索の方がハッシュ計算より速く、走査も空バケットを踏まない。
/// - OnTrigger のたびの GetComponent&lt;WindArea&gt;() を StageCache 経由にした。
///   風以外の Collider に触れたときの空振りが一番多く、そこが丸ごと消える。
/// - 風の数だけ VelocityChange を呼んでいたのを、合算して 1 回にした。
/// </summary>
public class WindReceiver : MonoBehaviour, IStageFixedTick
{
    private PlayerController playerController;

    // 同時に入る風の数はたかが知れているので List で十分。走査が配列そのままなのが効く。
    private readonly List<WindArea> activeWindAreas = new List<WindArea>(4);

    private void Awake()
    {
        this.playerController = this.GetComponent<PlayerController>();
    }

    private void OnEnable() => StageDirector.Register(this);

    private void OnDisable()
    {
        StageDirector.Unregister(this);
        this.activeWindAreas.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        WindArea area = StageCache<WindArea>.Get(other);
        if (area == null) return;
        if (this.activeWindAreas.Contains(area)) return; // 子 Collider が複数ある場合の二重登録防止

        this.activeWindAreas.Add(area);
    }

    private void OnTriggerExit(Collider other)
    {
        WindArea area = StageCache<WindArea>.Get(other);
        if (area == null) return;

        this.activeWindAreas.Remove(area);
    }

    public void FixedTick(float fixedDeltaTime)
    {
        int count = this.activeWindAreas.Count;
        if (count == 0) return;
        if (this.playerController == null) return;

        // 風ごとに VelocityChange を呼ばず、合算してから 1 回だけ渡す。
        Vector3 total = Vector3.zero;

        // 後ろから回しているのは、破棄済みの風をその場で取り除いても添字がずれないため。
        // 風の側が消えた場合 OnTriggerExit は飛んでこないので、ここで掃除しないと残り続ける。
        for (int i = count - 1; i >= 0; i--)
        {
            WindArea area = this.activeWindAreas[i];
            if (area == null)
            {
                this.activeWindAreas.RemoveAt(i);
                continue;
            }
            total += area.Acceleration;
        }

        if (total != Vector3.zero)
        {
            this.playerController.VelocityChange(total * fixedDeltaTime);
        }
    }
}
