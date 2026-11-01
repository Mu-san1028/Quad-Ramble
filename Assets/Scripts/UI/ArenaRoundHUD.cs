using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>追加ラウンドの目的・状態・全員の現在得点を表示する。</summary>
public sealed class ArenaRoundHUD : MonoBehaviour
{
    [SerializeField] private ArenaRoundController round;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI objectiveText;
    [SerializeField] private TextMeshProUGUI phaseText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI scoresText;
    [SerializeField] private Image accentBar;
    private float nextRefresh;
    private readonly StringBuilder scores = new();

    private void Update()
    {
        var manager = RoundManager.Instance;
        bool visible = round != null && round.IsSpawned && manager != null && manager.IsReady
            && !manager.IsIntro && !manager.IsRoundOver && !SceneTransitionUI.InputBlocked;
        if (group != null)
        {
            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
        }
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.1f;
        objectiveText.text = round.Objective;
        phaseText.text = round.PhaseLabel;
        phaseText.color = round.Accent;
        accentBar.color = round.Accent;
        statusText.text = round.GetStatus(NetworkManager.Singleton.LocalClientId);
        scores.Clear();
        // この端末で同期済みのプレイヤーだけを一覧に表示する。
        foreach (var networkObject in NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
        {
            if (!networkObject.TryGetComponent<PlayerStatus>(out var status)) continue;
            var connection = ConnectionContainer.Instance?.GetConnection(networkObject.OwnerClientId);
            string name = connection != null ? connection.Name : $"Player {networkObject.OwnerClientId + 1}";
            // TMPタグをプレイヤー名に含めさせない。
            name = name.Replace("<", "＜").Replace(">", "＞");
            if (name.Length > 16) name = name.Substring(0, 16);
            scores.Append(name).Append("  ").Append(status.RoundScore).AppendLine();
        }
        scoresText.text = scores.ToString();
    }
}
