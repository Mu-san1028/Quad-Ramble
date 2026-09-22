using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BuffRowUI : MonoBehaviour
{
    [Header("UI参照")]
    [SerializeField] private Image baseSegment;
    [SerializeField] private RectTransform segmentContainer;
    [SerializeField] private Image segmentPrefab;
    [SerializeField] private TextMeshProUGUI label;

    [Header("表示")]
    [SerializeField] private Color rowColor = Color.white;

    [SerializeField, Range(0f, 1f)]
    private float normalAlpha = 0.65f;

    private readonly List<Image> segments = new();

    /// <summary>
    /// ベースの台形に加え、count個の平行四辺形を表示する。
    /// </summary>
    public void SetCount(int count)
    {
        count = Mathf.Max(0, count);

        // 足りない区画だけ追加し、一度作ったものは使い回す。
        while (this.segments.Count < count)
        {
            Image segment = Instantiate(
                this.segmentPrefab,
                this.segmentContainer
            );

            segment.raycastTarget = false;
            this.segments.Add(segment);
        }

        Color displayColor = this.rowColor;
        displayColor.a = this.normalAlpha;

        this.baseSegment.color = displayColor;

        for (int i = 0; i < this.segments.Count; i++)
        {
            Image segment = this.segments[i];

            segment.gameObject.SetActive(i < count);
            segment.color = displayColor;
        }
    }

    public void Setup(string displayName, Color color, int count)
    {
        this.label.text = displayName;
        this.rowColor = color;

        this.SetCount(count);
    }

    [ContextMenu("表示確認/Power・1段")]
    private void PreviewPower()
    {
        if (!Application.isPlaying)
            return;

        this.Setup(
            "Power",
            new Color(1f, 0.25f, 0.15f),
            1
        );
    }

}