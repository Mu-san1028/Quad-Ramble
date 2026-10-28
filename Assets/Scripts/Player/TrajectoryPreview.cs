using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 照準の読み取り表示を担当する。
/// 弾道・レティクル・近接範囲のローカル表示を切り替える。
/// </summary>
[System.Serializable]
public class TrajectoryPreview
{
    [SerializeField] private LineRenderer line;
    [SerializeField] private GameObject blastMarker;
    [SerializeField] private GameObject crosshair;   // まっすぐ飛ぶ武器のときだけ出す画面中央の十字
    [SerializeField] private float markerRadius = 0.5f;

    [Header("近接武器の範囲表示")]
    [SerializeField] private Color meleeColor = new Color(1f, 0.8f, 0.2f, 0.85f);
    [SerializeField, Min(0.001f)] private float meleeLineWidth = 0.045f;
    [Tooltip("扇形・円の線が床と重ならないための表示用高さ。判定には影響しません。")]
    [SerializeField, Min(0f)] private float meleeHeightOffset = 0.06f;
    [SerializeField, Range(12, 64)] private int meleeArcSegments = 32;

    private bool meleeStyleActive;
    private Gradient trajectoryGradient;
    private AnimationCurve trajectoryWidth;
    private float trajectoryWidthMultiplier;
    private bool trajectoryWorldSpace;
    private bool trajectoryLoop;
    private readonly Vector3[] boxCorners = new Vector3[8];
    private static readonly int[] BoxEdges = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4 };

    /// <summary>すべての表示を消す</summary>
    public void Hide()
    {
        RestoreTrajectoryStyle();
        if (line != null) line.enabled = false;
        this.ShowMarker(false);
        this.ShowCrosshair(false);
    }

    /// <summary>弧を描く武器の表示。軌道線と着弾マーカーを出す</summary>
    public void Draw(List<Vector3> path, Vector3 landing, bool hitSomething)
    {
        RestoreTrajectoryStyle();
        this.ShowCrosshair(false);

        if (line != null)
        {
            line.enabled = true;
            line.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++) line.SetPosition(i, path[i]);
        }

        this.ShowMarker(hitSomething);
        if (!hitSomething || blastMarker == null) return;

        blastMarker.transform.position = landing;
        blastMarker.transform.localScale = Vector3.one * markerRadius * 2f;
    }

    /// <summary>まっすぐ飛ぶ武器の表示。着弾点はレティクルそのものなので線は引かない</summary>
    public void DrawCrosshair()
    {
        RestoreTrajectoryStyle();
        if (line != null) line.enabled = false;
        this.ShowMarker(false);
        this.ShowCrosshair(true);
    }

    /// <summary>扇形・球は水平断面、箱は立体の輪郭を表示する。命中判定は変更しない。</summary>
    public void DrawMelee(AttackRangeData range, Vector3 origin, Quaternion rotation, float rangeScale)
    {
        ShowMarker(false);
        ShowCrosshair(false);
        if (line == null) return;

        if (!(range is FanRangeData) && !(range is BoxRangeData) && !(range is SphereRangeData))
        {
            Hide();
            return;
        }

        if (!meleeStyleActive)
        {
            trajectoryGradient = line.colorGradient;
            trajectoryWidth = line.widthCurve;
            trajectoryWidthMultiplier = line.widthMultiplier;
            trajectoryWorldSpace = line.useWorldSpace;
            trajectoryLoop = line.loop;
            meleeStyleActive = true;
            line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        }

        line.useWorldSpace = true;
        line.loop = false;
        line.startColor = line.endColor = meleeColor;
        line.widthMultiplier = Mathf.Max(0.001f, meleeLineWidth);
        line.enabled = true;

        if (range is BoxRangeData box)
        {
            float scale = Mathf.Max(0.01f, rangeScale);
            Vector3 center = new Vector3(box.center.x * scale, box.center.y, box.center.z * scale);
            Vector3 half = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z)) * (0.5f * scale);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i == 0 || i == 3 || i == 4 || i == 7) ? -half.x : half.x,
                    i < 4 ? -half.y : half.y, (i % 4 < 2) ? -half.z : half.z);
                boxCorners[i] = origin + rotation * (center + corner);
            }

            line.positionCount = BoxEdges.Length;
            for (int i = 0; i < BoxEdges.Length; i++) line.SetPosition(i, boxCorners[BoxEdges[i]]);
            return;
        }

        bool isFan = range is FanRangeData;
        float radius = (isFan ? ((FanRangeData)range).radius : ((SphereRangeData)range).radius) * rangeScale;
        float angle = isFan ? ((FanRangeData)range).angle : 360f;
        int segments = Mathf.Clamp(meleeArcSegments, 12, 64);
        Vector3 displayOrigin = origin + Vector3.up * meleeHeightOffset;
        int start = isFan ? 1 : 0;
        line.positionCount = segments + 1 + (isFan ? 2 : 0);
        if (isFan) line.SetPosition(0, displayOrigin);
        for (int i = 0; i <= segments; i++)
        {
            float radians = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, i / (float)segments) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            line.SetPosition(start + i, displayOrigin + rotation * direction * radius);
        }

        if (isFan) line.SetPosition(line.positionCount - 1, displayOrigin);
    }

    private void RestoreTrajectoryStyle()
    {
        if (!meleeStyleActive || line == null) return;

        line.colorGradient = trajectoryGradient;
        line.widthCurve = trajectoryWidth;
        line.widthMultiplier = trajectoryWidthMultiplier;
        line.useWorldSpace = trajectoryWorldSpace;
        line.loop = trajectoryLoop;
        meleeStyleActive = false;
    }

    private void ShowMarker(bool visible)
    {
        if (blastMarker != null && blastMarker.activeSelf != visible) blastMarker.SetActive(visible);
    }

    private void ShowCrosshair(bool visible)
    {
        if (crosshair != null && crosshair.activeSelf != visible) crosshair.SetActive(visible);
    }
}
