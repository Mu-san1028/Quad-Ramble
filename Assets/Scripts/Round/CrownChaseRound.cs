using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>王冠を持っている時間を競う。取得・横取り・加点はサーバーだけで判定する。</summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class CrownChaseRound : ArenaRoundController
{
    public const ulong NoHolder = ulong.MaxValue;

    [Header("王冠")]
    [SerializeField] private Transform crownHome;
    [SerializeField] private Transform crownVisual;
    [SerializeField, Min(0.1f)] private float pickupRadius = 1.65f;
    [SerializeField, Min(0.1f)] private float stealRadius = 1.45f;
    [SerializeField, Min(0f)] private float protectionSeconds = 2f;
    [SerializeField, Min(0.1f)] private float crownHeight = 2.5f;

    private readonly NetworkVariable<ulong> holderClientId = new(NoHolder);
    private readonly NetworkVariable<double> protectedUntil = new(0d);
    // 0.6秒ずつ2回持った場合も1秒として加点する。横取りで端数を捨てない。
    private readonly Dictionary<ulong, double> scoreFractions = new();
    private Vector3 visualScale = Vector3.one;
    private Quaternion visualRotation = Quaternion.identity;

    public ulong HolderClientId => this.holderClientId.Value;
    public override Color Accent => new(1f, 0.72f, 0.16f);
    public override string Objective => "王冠を拾って逃げよう！ 保持1秒で1点。近づいて横取り／取得後2秒は保護。";

    public override string PhaseLabel
    {
        get
        {
            if (this.HolderClientId == NoHolder)
                return "中央の王冠を手に入れよう！";

            string name = ConnectionContainer.Instance?.GetConnectionByPlayerId(this.HolderClientId)?.Name;
            if (string.IsNullOrEmpty(name)) name = "プレイヤー";
            return $"{name} が王冠を保持中";
        }
    }

    public override void OnNetworkSpawn()
    {
        if (this.crownVisual != null)
        {
            this.visualScale = this.crownVisual.localScale;
            this.visualRotation = this.crownVisual.rotation;
        }

        if (IsServer)
        {
            this.holderClientId.Value = NoHolder;
            this.protectedUntil.Value = 0d;
            this.scoreFractions.Clear();
        }

        base.OnNetworkSpawn();
    }

    protected override void ServerTick(float deltaTime)
    {
        PlayerController holder = this.FindPlayer(this.HolderClientId);
        if (this.HolderClientId != NoHolder && holder == null)
            this.ReturnCrown();

        if (holder == null)
        {
            if (this.crownHome == null) return;
            PlayerController finder = this.FindNearest(this.crownHome.position, this.pickupRadius,
                2.75f, NoHolder);
            if (finder != null) this.TransferTo(finder);
            return;
        }

        // 直前のフレームから現在までの保持時間を、横取りされる側に先に加算する。
        if (deltaTime > 0f && float.IsFinite(deltaTime))
        {
            this.scoreFractions.TryGetValue(holder.ParticipantId, out double carried);
            carried += deltaTime;
            int points = (int)System.Math.Floor(carried);
            if (points > 0)
            {
                holder.GetComponent<PlayerStatus>()?.AddRoundScore(points);
                carried -= points;
            }
            this.scoreFractions[holder.ParticipantId] = carried;
        }

        if (NetworkManager.ServerTime.Time < this.protectedUntil.Value) return;

        PlayerController thief = this.FindNearest(holder.transform.position, this.stealRadius,
            1.8f, holder.ParticipantId);
        if (thief != null) this.TransferTo(thief);
    }

    protected override void PlayerRespawned(PlayerController player)
    {
        if (player != null && player.ParticipantId == this.HolderClientId)
            this.ReturnCrown();
    }

    private void TransferTo(PlayerController player)
    {
        this.holderClientId.Value = player.ParticipantId;
        this.protectedUntil.Value = NetworkManager.ServerTime.Time + this.protectionSeconds;

        // 取得直後だけ少し減速する。保護が終わる前に解除し、横取り後へ持ち越さない。
        // 継続ApplySlowは通知を連発し、武器による減速も上書きするため行わない。
        PlayerStatus status = player.GetComponent<PlayerStatus>();
        if (status != null && status.SlowSpeedMultiplier >= 0.85f && this.protectionSeconds > 0f)
            status.ApplySlow(0.85f, Mathf.Min(1f, this.protectionSeconds));
    }

    private void ReturnCrown()
    {
        this.holderClientId.Value = NoHolder;
        this.protectedUntil.Value = 0d;
    }

    private PlayerController FindPlayer(ulong clientId)
    {
        if (clientId == NoHolder || PlayerManager.Instance == null) return null;

        foreach (PlayerController player in PlayerManager.Instance.AllPlayers)
        {
            if (player != null && player.IsSpawned && !player.IsEliminated
                && player.ParticipantId == clientId)
                return player;
        }
        return null;
    }

    private PlayerController FindNearest(Vector3 position, float radius, float height, ulong excluded)
    {
        if (PlayerManager.Instance == null) return null;

        PlayerController nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (PlayerController player in PlayerManager.Instance.AllPlayers)
        {
            if (player == null || !player.IsSpawned || player.IsEliminated
                || player.ParticipantId == excluded
                || !CanTouch(player.transform.position, position, radius, height))
                continue;

            Vector3 delta = player.transform.position - position;
            float distance = delta.x * delta.x + delta.z * delta.z;
            if (nearest == null || distance < nearestDistance
                || (distance == nearestDistance
                    && player.ParticipantId < nearest.ParticipantId))
            {
                nearest = player;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>ジャンプで高さが大きく離れた相手や、王冠の真下の落下者を除く。</summary>
    public static bool CanTouch(Vector3 player, Vector3 target, float radius, float height)
    {
        Vector3 delta = player - target;
        return radius > 0f && height >= 0f && Mathf.Abs(delta.y) <= height
            && delta.x * delta.x + delta.z * delta.z <= radius * radius;
    }

    public override string GetStatus(ulong clientId)
    {
        PlayerController player = this.FindPlayer(clientId);
        int score = player != null ? player.GetComponent<PlayerStatus>()?.RoundScore ?? 0 : 0;
        if (clientId != this.HolderClientId)
            return $"保持得点  {score} pt  ／ 王冠を追いかけよう";

        float protection = IsSpawned
            ? Mathf.Max(0f, (float)(this.protectedUntil.Value - NetworkManager.ServerTime.Time)) : 0f;
        return protection > 0f
            ? $"保持得点  {score} pt  ／ 保護中 {protection:0.0}秒"
            : $"保持得点  {score} pt  ／ 王冠を守って逃げよう！";
    }

    protected override void VisualTick(float deltaTime)
    {
        if (this.crownVisual == null || this.crownHome == null) return;

        PlayerController holder = this.FindPlayer(this.HolderClientId);
        float time = IsSpawned ? (float)NetworkManager.ServerTime.Time : Time.time;
        Vector3 position = holder != null
            ? holder.transform.position + Vector3.up * this.crownHeight
            : this.crownHome.position;
        position.y += Mathf.Sin(time * 3f) * 0.12f;
        this.crownVisual.SetPositionAndRotation(position,
            Quaternion.Euler(0f, time * 70f, 0f) * this.visualRotation);

        bool protectedHolder = holder != null && IsSpawned
            && NetworkManager.ServerTime.Time < this.protectedUntil.Value;
        this.crownVisual.localScale = this.visualScale
            * (protectedHolder ? 1.1f + Mathf.Sin(time * 9f) * 0.08f : 1f);
    }
}
