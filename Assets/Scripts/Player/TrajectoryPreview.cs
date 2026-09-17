using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 照準の読み取り表示を担当する。
/// 表示は「弧＋着弾マーカー」「画面中央の十字」「何も出さない」の3状態のいずれか。
/// </summary>
[System.Serializable]
public class TrajectoryPreview
{
    [SerializeField] private LineRenderer line;
    [SerializeField] private GameObject blastMarker;
    [SerializeField] private GameObject crosshair;   // まっすぐ飛ぶ武器のときだけ出す画面中央の十字
    [SerializeField] private float markerRadius = 0.5f;

    /// <summary>すべての表示を消す</summary>
    public void Hide()
    {
        if (line != null) line.enabled = false;
        this.ShowMarker(false);
        this.ShowCrosshair(false);
    }

    /// <summary>弧を描く武器の表示。軌道線と着弾マーカーを出す</summary>
    public void Draw(List<Vector3> path, Vector3 landing, bool hitSomething)
    {
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
        if (line != null) line.enabled = false;
        this.ShowMarker(false);
        this.ShowCrosshair(true);
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
