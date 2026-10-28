using System.Collections.Generic;
using UnityEngine;

/// <summary>所有者へ届いたダメージ通知を編集可能なUI Prefabへ渡す。</summary>
public class PlayerDamageFeedback : MonoBehaviour
{
    [SerializeField] private DamagePopupUI prefab;
    [SerializeField, Min(1)] private int maxVisible = 16;
    private readonly List<DamagePopupUI> active = new List<DamagePopupUI>();
    private int sequence;

    public void Show(float damage, Vector3 position, int round)
    {
        if (!isActiveAndEnabled || prefab == null || damage <= 0f) return;
        active.RemoveAll(popup => popup == null);
        while (active.Count >= Mathf.Max(1, maxVisible))
        {
            Destroy(active[0].gameObject);
            active.RemoveAt(0);
        }

        var popup = Instantiate(prefab);
        popup.Play(damage, position, round, sequence++);
        active.Add(popup);
    }

    private void OnDisable()
    {
        foreach (var popup in active)
            if (popup != null) Destroy(popup.gameObject);
        active.Clear();
    }
}
