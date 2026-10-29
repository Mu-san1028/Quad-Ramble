using UnityEngine;
using UnityEngine.UI;

/// <summary>解像度に依存しない斜めの縁と上下グラデーション。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class TransitionPanelGraphic : Image
{
    public float Slant = 180f;
    public Color BottomTint = Color.white;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        float skew = this.Slant * 0.5f;
        Color bottom = this.color * this.BottomTint;
        vh.AddVert(new Vector3(r.xMin - skew, r.yMin), bottom, Vector2.zero);
        vh.AddVert(new Vector3(r.xMin + skew, r.yMax), this.color, Vector2.up);
        vh.AddVert(new Vector3(r.xMax + skew, r.yMax), this.color, Vector2.one);
        vh.AddVert(new Vector3(r.xMax - skew, r.yMin), bottom, Vector2.right);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }
}
