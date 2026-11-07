using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>同期済みの名簿から、タイトル下部の参加者カードだけを更新する。</summary>
public sealed class TitlePlayerCardsUI : MonoBehaviour
{
    [Serializable]
    public sealed class Card
    {
        public RectTransform root;
        public Image icon;
        public Image badge;
        public Image[] frame;
        public TMP_Text playerNumber;
        public TMP_Text characterName;
        public TMP_Text readyState;
        [NonSerialized] public Vector3 scale;
        [NonSerialized] public bool initialized;
        [NonSerialized] public ulong clientId;
        [NonSerialized] public bool ready;
        [NonSerialized] public float bounce;
    }

    [SerializeField] private Card[] cards;
    [SerializeField] private Color[] playerColors = {
        new(1f, .24f, .32f), new(.18f, .57f, 1f),
        new(.2f, .85f, .47f), new(1f, .83f, .2f)
    };
    [SerializeField] private Color readyColor = new(.45f, 1f, .65f);
    [SerializeField] private Color selectingColor = new(.85f, .9f, 1f);
    [SerializeField] private Color unusedColor = new(.42f, .45f, .5f, 1f);
    [SerializeField, Min(.01f)] private float bounceDuration = .3f;
    [SerializeField, Range(0f, .2f)] private float bounceStrength = .07f;

    private void Awake()
    {
        foreach (var card in this.cards)
        {
            if (card.root == null) continue;
            card.scale = card.root.localScale;
            foreach (var text in new[] { card.playerNumber, card.characterName, card.readyState })
            {
                if (text == null) continue;
                text.outlineColor = new Color32(12, 17, 32, 255);
                text.outlineWidth = .16f;
            }
            card.root.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        var network = NetworkManager.Singleton;
        var roster = ConnectionContainer.Instance;
        bool visible = network != null && network.IsConnectedClient && roster != null
            && roster.IsSpawned && roster.GetConnection(network.LocalClientId) != null;

        for (int i = 0; i < this.cards.Length; i++)
        {
            var card = this.cards[i];
            if (card.root == null) continue;
            card.root.gameObject.SetActive(visible);
            if (!visible)
            {
                card.initialized = false;
                card.bounce = 0f;
                card.root.localScale = card.scale;
                continue;
            }

            if (i >= roster.RequiredPlayerCount)
            {
                this.ShowUnusedCard(card);
                continue;
            }

            var connection = roster.GetConnectionByPlayerId((ulong)i);
            var character = connection != null && CharacterDatabase.Instance != null
                ? CharacterDatabase.Instance.Get(connection.CharacterIndex) : null;
            bool ready = connection != null && connection.IsReady;
            if (card.initialized && connection != null && card.clientId == connection.Id
                && !card.ready && ready) card.bounce = this.bounceDuration;
            if (connection == null || (card.initialized && card.clientId != connection.Id))
                card.bounce = 0f;

            card.initialized = connection != null;
            card.clientId = connection != null ? connection.Id : ulong.MaxValue;
            card.ready = ready;
            Color color = i < this.playerColors.Length ? this.playerColors[i] : Color.gray;
            if (card.badge != null) card.badge.color = color;
            foreach (var edge in card.frame) if (edge != null) edge.color = color;
            if (card.playerNumber != null) card.playerNumber.text = $"{i + 1}P";
            if (card.characterName != null) card.characterName.text = connection != null && connection.IsBot
                ? connection.Name : character != null ? character.displayName
                : connection != null ? "キャラを選ぼう" : "参加待ち";
            if (card.readyState != null)
            {
                card.readyState.text = connection == null ? "—" : ready ? "準備完了！" : "選択中…";
                card.readyState.color = ready ? this.readyColor : this.selectingColor;
            }
            if (card.icon != null)
            {
                card.icon.sprite = character?.icon;
                card.icon.enabled = card.icon.sprite != null;
            }

            card.bounce = Mathf.Max(0f, card.bounce - Time.unscaledDeltaTime);
            float t = 1f - card.bounce / Mathf.Max(.01f, this.bounceDuration);
            card.root.localScale = card.scale * (1f + Mathf.Sin(t * Mathf.PI) * this.bounceStrength);
        }
    }

    private void ShowUnusedCard(Card card)
    {
        card.initialized = false;
        card.clientId = ulong.MaxValue;
        card.ready = false;
        card.bounce = 0f;
        card.root.localScale = card.scale;

        if (card.badge != null) card.badge.color = this.unusedColor;
        foreach (var edge in card.frame)
            if (edge != null) edge.color = this.unusedColor;

        if (card.playerNumber != null) card.playerNumber.text = "";
        if (card.characterName != null) card.characterName.text = "";
        if (card.readyState != null) card.readyState.text = "";
        if (card.icon != null)
        {
            card.icon.sprite = null;
            card.icon.enabled = false;
        }
    }
}
