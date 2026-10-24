using UnityEngine;
using UnityEngine.UI;

// Font-independent outlined triangle for small selection indicators.
public class SelectionArrowGraphic : MaskableGraphic
{
    public Color OutlineColor { get; set; } = Color.black;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = this.rectTransform.rect;
        Vector2 a = new Vector2(rect.xMin, rect.yMin);
        Vector2 b = new Vector2(rect.xMin, rect.yMax);
        Vector2 c = new Vector2(rect.xMax, rect.center.y);
        Vector2 center = (a + b + c) / 3f;

        AddTriangle(vh, a, b, c, this.OutlineColor);
        AddTriangle(vh, Vector2.Lerp(center, a, 0.65f),
            Vector2.Lerp(center, b, 0.65f), Vector2.Lerp(center, c, 0.65f), this.color);
    }

    private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, color, Vector2.zero);
        vh.AddVert(b, color, Vector2.zero);
        vh.AddVert(c, color, Vector2.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
