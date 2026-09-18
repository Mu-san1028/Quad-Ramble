using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// クイズ床の取りまとめ役。
///
/// 旧実装からの変更点:
/// - Start での FindObjectsByType&lt;QuizFloor&gt;() をやめた。
///   シーン中の全 GameObject を走査するうえ、結果の配列も確保される。
///   さらに、後から生成した床は拾えず、破棄された床は List に null として残り続けていた。
///   床が自分で登録／解除する形にして、この 3 つをまとめて解消した。
/// - ID 引きの Dictionary を持たせ、特定 ID の床だけを消す操作を全件走査なしでできるようにした。
/// </summary>
public class QuizManager : MonoBehaviour
{
    private static QuizManager instance;

    // 全床。NotifyAnswer のように全件へ配る操作はこちらを使う。
    private readonly StageRegistry<QuizFloor> floors = new StageRegistry<QuizFloor>();

    // ID 引き。DeleteFloorsByID を全件走査なしで済ませるため。
    private readonly Dictionary<int, List<QuizFloor>> floorsById = new Dictionary<int, List<QuizFloor>>(16);

    public int FloorCount => this.floors.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>
    /// シーンに置かれていればそれを、なければ自動生成する。
    /// 床の OnEnable が Awake より先に走っても登録できるようにするため。
    /// </summary>
    public static QuizManager Instance
    {
        get
        {
            if (instance != null) return instance;

            // 探索はここでの 1 回だけ。毎フレームは走らない。
            instance = FindFirstObjectByType<QuizManager>();
            if (instance != null) return instance;

            var go = new GameObject("[QuizManager]");
            go.hideFlags = HideFlags.DontSave;
            instance = go.AddComponent<QuizManager>();
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning($"[QuizManager] '{this.name}' は 2 個目なので無効化します。", this);
            this.enabled = false;
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // --- 床の登録 -----------------------------------------------------------

    public void RegisterFloor(QuizFloor floor)
    {
        if (floor == null) return;

        this.floors.Add(floor);

        if (!this.floorsById.TryGetValue(floor.FloorID, out List<QuizFloor> list))
        {
            list = new List<QuizFloor>(4);
            this.floorsById[floor.FloorID] = list;
        }
        if (!list.Contains(floor)) list.Add(floor);
    }

    /// <summary>床側の OnDisable から呼ばれる。Manager が既に消えていても安全なように static。</summary>
    public static void UnregisterFloor(QuizFloor floor)
    {
        if (instance == null || floor == null) return;

        instance.floors.Remove(floor);

        if (instance.floorsById.TryGetValue(floor.FloorID, out List<QuizFloor> list))
        {
            list.Remove(floor);
        }
    }

    // --- 操作 ---------------------------------------------------------------

    /// <summary>
    /// 正解 ID を全床へ配る。ID が違う床が消える。
    /// </summary>
    public void NotifyAnswer(int correctID)
    {
        // 配列をそのまま前から回す。null チェックが不要なのは、
        // 床が OnDisable で自分を外すので、ここに死んだ参照が残らないため。
        QuizFloor[] items = this.floors.Items;
        for (int i = 0; i < this.floors.Count; i++)
        {
            items[i].CheckAnswer(correctID);
        }
    }

    /// <summary>指定 ID の床だけを消す。全件走査しない。</summary>
    public void DeleteFloorsByID(int targetID)
    {
        if (!this.floorsById.TryGetValue(targetID, out List<QuizFloor> list)) return;

        for (int i = 0; i < list.Count; i++)
        {
            list[i].Dissolve();
        }
    }

    /// <summary>次の問題用に全床を戻す。</summary>
    public void RestoreAll()
    {
        QuizFloor[] items = this.floors.Items;
        for (int i = 0; i < this.floors.Count; i++)
        {
            items[i].Restore();
        }
    }
}