using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 軌道線と着弾マーカーの表示を担当する。
/// </summary>
[System.Serializable]
public class TrajectoryPreview
{
    [SerializeField] private LineRenderer line;
    [SerializeField] private GameObject blastMarker;
    [SerializeField] private float markerRadius = 0.5f;

    public void Show(bool visible)
    {
        if (line != null) line.enabled = visible;
        this.ShowMarker(visible);
    }

    public void Draw(List<Vector3> path, Vector3 landing, bool hitSomething)
    {
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

    private void ShowMarker(bool visible)
    {
        if (blastMarker != null && blastMarker.activeSelf != visible) blastMarker.SetActive(visible);
    }
}
