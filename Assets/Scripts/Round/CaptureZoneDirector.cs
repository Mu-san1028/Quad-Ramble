using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>ラウンドの時計で占領エリアを切り替える。各段階は有効エリアの完全な一覧。</summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class CaptureZoneDirector : NetworkBehaviour
{
    [Serializable]
    public class Activation
    {
        public CaptureZone zone;
        [Min(1f)] public float multiplier = 1f;
    }

    [Serializable]
    public class Phase
    {
        [Tooltip("先頭は開始時なので無視。2段階目以降は残り秒数の大きい順")]
        [Min(0f)] public float remainingSeconds;
        public string announcement = "占領エリアが切り替わります！";
        public Activation[] activeZones = Array.Empty<Activation>();
    }

    [SerializeField] private Phase[] phases = {
        new Phase(),
        new Phase { remainingSeconds = 50f },
        new Phase { remainingSeconds = 20f, announcement = "高得点エリアが開放されます！" }
    };

    private readonly NetworkVariable<PhaseState> current = new(new PhaseState { index = -1 });
    private readonly List<CaptureZone> zones = new();
    private bool valid;
    private float[] thresholds;

    public IReadOnlyList<CaptureZone> Zones => zones;
    public bool IsRunning => isActiveAndEnabled && IsSpawned && valid && current.Value.index >= 0;
    public double ChangedAt => current.Value.changedAt;

    private void Awake()
    {
        valid = RegisterZones();

        if (!GetComponent<CaptureZoneNoticeUI>())
            gameObject.AddComponent<CaptureZoneNoticeUI>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            current.Value = new PhaseState { index = -1 };
    }

    private bool RegisterZones()
    {
        bool success = phases != null && phases.Length > 0;

        if (phases == null)
            return false;

        thresholds = new float[phases.Length];

        for (int i = 0; i < phases.Length; i++)
        {
            Phase phase = phases[i];
            if (phase == null || phase.activeZones == null)
            {
                success = false;
                continue;
            }

            if (i > 0 && (phase.remainingSeconds <= 0 || !float.IsFinite(phase.remainingSeconds)
                || (i > 1 && (phases[i - 1] == null || phase.remainingSeconds >= phases[i - 1].remainingSeconds))))
                success = false;

            thresholds[i] = phase.remainingSeconds;

            var unique = new HashSet<CaptureZone>();

            foreach (Activation entry in phase.activeZones)
            {
                if (entry == null || !entry.zone)
                {
                    success = false;
                    continue;
                }

                success &= unique.Add(entry.zone) && float.IsFinite(entry.multiplier) && entry.multiplier >= 1;

                if (!zones.Contains(entry.zone))
                {
                    zones.Add(entry.zone);
                    success &= entry.zone.RegisterDirector(this);
                }
            }
        }

        if (!success)
            Debug.LogError("CaptureZoneDirector: PhasesのZone参照・倍率（1以上）・残り秒数の降順を確認してください。同じZoneの重複や複数Directorへの登録はできません。設定を直して再生し直してください。", this);

        return success;
    }

    private void Update()
    {
        RefreshServerSchedule();
    }

    // Zoneからも先に呼ぶことで、切替フレームに旧エリアが加点するのを防ぐ。
    public void RefreshServerSchedule()
    {
        if (!IsSpawned || !IsServer)
            return;

        var round = RoundManager.Instance;
        bool playing = valid && isActiveAndEnabled && round && round.IsSpawned
            && GameManager.Instance && GameManager.Instance.currentState == GameState.Playing
            && round.RemainingTime > 0;

        int next = -1;

        if (playing)
        {
            next = CaptureZoneScheduleRules.PhaseAt(round.RemainingTime, thresholds);
        }

        if (next != current.Value.index)
        {
            current.Value = new PhaseState {
                index = next,
                previousIndex = current.Value.index,
                changedAt = NetworkManager.ServerTime.Time
            };
        }
    }

    public float MultiplierFor(CaptureZone zone)
    {
        return IsRunning ? FindMultiplier(current.Value.index, zone) : 0f;
    }

    private float FindMultiplier(int phaseIndex, CaptureZone zone)
    {
        if (phaseIndex < 0 || phaseIndex >= phases.Length)
            return 0f;

        foreach (Activation entry in phases[phaseIndex].activeZones)
        {
            if (entry.zone == zone)
                return entry.multiplier;
        }

        return 0f;
    }

    public bool ShowArrowFor(CaptureZone zone)
    {
        if (!IsRunning || !CaptureZoneScheduleRules.ArrowVisible(NetworkManager.ServerTime.Time - ChangedAt))
            return false;

        float multiplier = MultiplierFor(zone);
        return multiplier > 0 && multiplier != FindMultiplier(current.Value.previousIndex, zone);
    }

    public string PendingAnnouncement()
    {
        if (!IsRunning || !RoundManager.Instance)
            return null;

        int next = current.Value.index + 1;
        if (next >= phases.Length)
            return null;

        float until = RoundManager.Instance.RemainingTime - phases[next].remainingSeconds;
        return CaptureZoneScheduleRules.ShouldAnnounce(until) ? phases[next].announcement : null;
    }

    private void OnDisable()
    {
        RefreshServerSchedule();
    }

    public override void OnDestroy()
    {
        foreach (CaptureZone zone in zones)
        {
            if (zone)
                zone.UnregisterDirector(this);
        }

        base.OnDestroy();
    }

    public struct PhaseState : INetworkSerializable, IEquatable<PhaseState>
    {
        public int index;
        public int previousIndex;
        public double changedAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref index);
            serializer.SerializeValue(ref previousIndex);
            serializer.SerializeValue(ref changedAt);
        }

        public bool Equals(PhaseState other)
        {
            return index == other.index && previousIndex == other.previousIndex && changedAt == other.changedAt;
        }
    }
}
