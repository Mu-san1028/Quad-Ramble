using UnityEngine;
using UnityEngine.UI;

/// <summary>チャージ表示の見た目。背景・バーのサイズと配置はPrefabで編集する。</summary>
public class WeaponChargeUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform fill;
    [SerializeField] private Image fillImage;
    [SerializeField] private Color emptyColor = new Color(0.2f, 0.75f, 1f);
    [SerializeField] private Color fullColor = new Color(1f, 0.85f, 0.2f);

    public void SetProgress(bool visible, float progress)
    {
        group.alpha = visible ? 1f : 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        progress = Mathf.Clamp01(progress);
        Vector2 max = fill.anchorMax;
        max.x = progress;
        fill.anchorMax = max;
        fillImage.color = Color.Lerp(emptyColor, fullColor, progress);
    }
}
