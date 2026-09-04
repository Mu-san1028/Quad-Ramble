using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 自分が持っているラウンド中恒久バフの段数を表示する
/// </summary>
public class BuffUI : MonoBehaviour
{
    [System.Serializable]
    private class BuffRow
    {
        public UpgradeKind kind;
        public Image[] pips;
        public Color color = Color.white;
    }

    [Header("バフの種類ごとの行")]
    [SerializeField] private BuffRow[] rows = new BuffRow[4];

    [Header("消灯時の濃さ")]
    [Range(0f, 1f)]
    [SerializeField] private float emptyAlpha = 0.2f;


    private Canvas canvas;

    void Awake()
    {
        this.canvas = GetComponent<Canvas>();
    }

    void Update()
    {
        PlayerStatus status = PlayerStatus.Local;

        bool visible = status != null;
        if (this.canvas.enabled != visible) this.canvas.enabled = visible;
        if (!visible) return;

        foreach (var row in this.rows)
        {
            int count = status.GetStacks(row.kind);

            Color unlit = row.color;
            unlit.a *= this.emptyAlpha;   // 元のαを尊重して薄くする

            for (int i = 0; i < row.pips.Length; i++)
            {
                if (row.pips[i] == null) continue;
                row.pips[i].color = i < count ? row.color : unlit;
            }
        }
    }
}
