using UnityEngine;
using UnityEngine.UI;

/// <summary>単色の Image の右下を内側へ寄せ、台形のバッジにする。</summary>
[RequireComponent(typeof(Image))]
[AddComponentMenu("UI/Effects/Trapezoid Badge")]
public sealed class TrapezoidBadge : BaseMeshEffect
{
    [SerializeField, Range(0f, 0.45f)]
    private float bottomRightInset = 0.25f;

    public override void ModifyMesh(VertexHelper vertices)
    {
        if (!IsActive()) return;

        Rect rect = graphic.GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;

        UIVertex vertex = new UIVertex();
        for (int i = 0; i < vertices.currentVertCount; i++)
        {
            vertices.PopulateUIVertex(ref vertex, i);
            float x = Mathf.InverseLerp(rect.xMin, rect.xMax, vertex.position.x);
            float y = Mathf.InverseLerp(rect.yMin, rect.yMax, vertex.position.y);
            vertex.position.x -= rect.width * this.bottomRightInset * x * (1f - y);
            vertices.SetUIVertex(vertex, i);
        }
    }
}
