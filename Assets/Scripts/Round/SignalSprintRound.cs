using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 青で進み、黄で減速し、赤で止まる周回競争。判定と加点はサーバーだけが行う。
/// 赤で動くと最後のチェックポイントへ戻る。ゴール後は再びスタートから走れる。
/// </summary>
public sealed class SignalSprintRound : ArenaRoundController
{
    public enum SignalState : byte { Green, Amber, Red }

    [Header("コース（ワールド +Z 方向）")]
    [SerializeField] private Transform[] startPoints;
    [SerializeField] private Transform[] checkpoints;
    [SerializeField] private Transform finishLine;
    [SerializeField, Min(1f)] private float courseHalfWidth = 10f;
    [SerializeField, Min(1)] private int pointsPerLap = 10;

    [Header("信号")]
    [SerializeField, Min(1f)] private float greenDuration = 4f;
    [SerializeField, Min(.5f)] private float amberDuration = 1.2f;
    [SerializeField, Min(1f)] private float redDuration = 2.8f;
    [Tooltip("赤への切り替え直後は通信遅延と停止操作のため判定を猶予する")]
    [SerializeField, Min(.1f)] private float stopGrace = .4f;
    [SerializeField, Min(.1f)] private float movementTolerance = .8f;
    [Tooltip("青、黄、赤の順。複数の信号機なら同じ順で繰り返す")]
    [SerializeField] private Renderer[] signalLamps;

    private readonly NetworkVariable<SignalState> signal = new(SignalState.Green);
    private readonly NetworkVariable<double> phaseStartedAt = new();
    private readonly NetworkVariable<double> phaseEndsAt = new();
    private NetworkList<SignalRunnerSnapshot> runnerStates;
    private readonly Dictionary<ulong, Runner> runners = new();
    private MaterialPropertyBlock lampProperties;
    private int cycle;

    private sealed class Runner
    {
        public int Lane;
        public int StateIndex;
        public Vector3 StopPosition;
        public bool HasStopPosition;
        public double IgnoreUntil;
        public bool AwaitingTeleport;
        public Vector3 TeleportTarget;
        public double RetryTeleportAt;
    }

    public SignalState CurrentSignal => this.signal.Value;
    public override string Objective => $"青で進む・赤で止まる！ ゴール1回で{Mathf.Max(1, pointsPerLap)}点。赤で動くとチェックポイントへ戻る。";
    public override Color Accent => ColorFor(this.signal.Value);
    public override string PhaseLabel
    {
        get
        {
            if (!IsPlaying || NetworkManager == null) return "信号に注目！";
            int seconds = Mathf.CeilToInt((float)Math.Max(0d, this.phaseEndsAt.Value - NetworkManager.ServerTime.Time));
            string label = this.signal.Value switch
            {
                SignalState.Green => "青：進め！",
                SignalState.Amber => "黄：止まる準備！",
                _ => "赤：動くな！"
            };
            return $"{label}  {seconds}";
        }
    }

    private void Awake()
    {
        // NativeContainerを使うNetworkListはフィールド初期化時に確保しない。
        this.runnerStates = new NetworkList<SignalRunnerSnapshot>();
        this.lampProperties = new MaterialPropertyBlock();
    }

    public override void OnNetworkSpawn()
    {
        this.runners.Clear();
        this.cycle = 0;
        if (IsServer)
        {
            this.runnerStates.Clear();
            this.signal.Value = SignalState.Green;
            this.phaseStartedAt.Value = 0d;
            this.phaseEndsAt.Value = 0d;
        }
        base.OnNetworkSpawn();
    }

    public override void OnNetworkDespawn()
    {
        this.runners.Clear();
        base.OnNetworkDespawn();
    }

    protected override void ServerStarted()
    {
        this.SetSignal(SignalState.Green, NetworkManager.ServerTime.Time);
        if (PlayerManager.Instance == null) return;
        foreach (PlayerController player in PlayerManager.Instance.Players)
            if (player != null && player.IsSpawned && !player.IsEliminated)
                this.GetRunner(player);
    }

    protected override void ServerTick(float deltaTime)
    {
        if (PlayerManager.Instance == null || this.finishLine == null) return;

        double now = NetworkManager.ServerTime.Time;
        if (now >= this.phaseEndsAt.Value)
        {
            SignalState next = this.signal.Value switch
            {
                SignalState.Green => SignalState.Amber,
                SignalState.Amber => SignalState.Red,
                _ => SignalState.Green
            };
            if (next == SignalState.Green) this.cycle++;
            this.SetSignal(next, now);
        }

        bool mayAdvance = CanAdvance(this.signal.Value, this.phaseStartedAt.Value, now, this.stopGrace);
        foreach (PlayerController player in PlayerManager.Instance.Players)
        {
            if (player == null || !player.IsSpawned || player.IsEliminated) continue;
            Runner runner = this.GetRunner(player);
            Vector3 position = player.transform.position;

            // Teleportは所有者へのRPC。サーバーの古い座標で二重加点・再違反しない。
            if (runner.AwaitingTeleport)
            {
                if ((position - runner.TeleportTarget).sqrMagnitude <= 9f)
                    runner.AwaitingTeleport = false;
                else if (now >= runner.RetryTeleportAt)
                {
                    player.Teleport(runner.TeleportTarget, Quaternion.identity);
                    runner.RetryTeleportAt = now + 1d;
                }
                runner.HasStopPosition = false;
                continue;
            }
            if (now < runner.IgnoreUntil)
            {
                runner.HasStopPosition = false;
                continue;
            }

            if (!mayAdvance)
            {
                if (!runner.HasStopPosition)
                {
                    runner.StopPosition = position;
                    runner.HasStopPosition = true;
                }
                else if (MovedWhileStopped(runner.StopPosition, position, this.movementTolerance))
                {
                    SignalRunnerSnapshot state = this.runnerStates[runner.StateIndex];
                    state.Penalties++;
                    state.Notice = 1;
                    state.NoticeUntil = now + 2d;
                    this.runnerStates[runner.StateIndex] = state;
                    this.SendTo(player, runner, this.RespawnPosition(runner, state.Checkpoint), now);
                }
                // 赤の間は、許容誤差の内側を少しずつ動いてもCPやゴールを獲得できない。
                continue;
            }

            runner.HasStopPosition = false;
            if (Mathf.Abs(position.x - this.finishLine.position.x) > this.courseHalfWidth
                || position.y < this.finishLine.position.y - .5f
                || position.y > this.finishLine.position.y + 8f)
                continue;

            SignalRunnerSnapshot progress = this.runnerStates[runner.StateIndex];
            int previousCheckpoint = progress.Checkpoint;
            int checkpointCount = this.checkpoints != null ? this.checkpoints.Length : 0;
            while (progress.Checkpoint < checkpointCount)
            {
                Transform checkpoint = this.checkpoints[progress.Checkpoint];
                if (checkpoint == null || position.z < checkpoint.position.z) break;
                progress.Checkpoint++;
            }

            if (position.z >= this.finishLine.position.z && progress.Checkpoint == checkpointCount)
            {
                if (!player.TryGetComponent<PlayerStatus>(out var status) || !status.IsSpawned) continue;
                status.AddRoundScore(Mathf.Max(1, this.pointsPerLap));
                progress.Laps++;
                progress.Checkpoint = 0;
                progress.Notice = 2;
                progress.NoticeUntil = now + 2d;
                this.runnerStates[runner.StateIndex] = progress;
                this.SendTo(player, runner, this.RespawnPosition(runner, 0), now);
            }
            else if (progress.Checkpoint != previousCheckpoint)
            {
                this.runnerStates[runner.StateIndex] = progress;
            }
        }
    }

    private void SetSignal(SignalState next, double now)
    {
        this.signal.Value = next;
        this.phaseStartedAt.Value = now;
        // 一定周期の暗記だけにならない、全端末共通の緩やかな長さの変化。
        float variation = (this.cycle % 3 - 1) * .4f;
        float duration = next switch
        {
            SignalState.Green => Mathf.Max(1f, this.greenDuration + variation),
            SignalState.Amber => Mathf.Max(.5f, this.amberDuration),
            _ => Mathf.Max(this.stopGrace + .5f, this.redDuration - variation)
        };
        this.phaseEndsAt.Value = now + duration;
        foreach (Runner runner in this.runners.Values) runner.HasStopPosition = false;
    }

    private Runner GetRunner(PlayerController player)
    {
        if (this.runners.TryGetValue(player.ParticipantId, out Runner runner)) return runner;
        int lane = this.runners.Count;
        runner = new Runner { Lane = lane, StateIndex = this.runnerStates.Count };
        this.runnerStates.Add(new SignalRunnerSnapshot { ClientId = player.ParticipantId });
        this.runners.Add(player.ParticipantId, runner);
        return runner;
    }

    private Vector3 RespawnPosition(Runner runner, int checkpoint)
    {
        Transform start = this.startPoints != null && this.startPoints.Length > 0
            ? this.startPoints[runner.Lane % this.startPoints.Length] : null;
        Vector3 position = start != null ? start.position : new Vector3(0f, 1f, -20f);
        if (checkpoint > 0 && this.checkpoints != null && checkpoint <= this.checkpoints.Length
            && this.checkpoints[checkpoint - 1] != null)
        {
            // 取得したラインの少し手前へ。横座標は元のレーンに揃える。
            position.z = this.checkpoints[checkpoint - 1].position.z - .8f;
        }
        return position;
    }

    private void SendTo(PlayerController player, Runner runner, Vector3 target, double now)
    {
        runner.HasStopPosition = false;
        runner.AwaitingTeleport = true;
        runner.TeleportTarget = target;
        runner.IgnoreUntil = now + .45d;
        runner.RetryTeleportAt = now + 1d;
        player.Teleport(target, Quaternion.identity);
    }

    protected override void PlayerRespawned(PlayerController player)
    {
        Runner runner = this.GetRunner(player);
        SignalRunnerSnapshot progress = this.runnerStates[runner.StateIndex];
        progress.Checkpoint = 0;
        progress.Notice = 3;
        progress.NoticeUntil = NetworkManager.ServerTime.Time + 2d;
        this.runnerStates[runner.StateIndex] = progress;
        // 共通復帰処理の直後も、所有者の旧位置を判定から除外する。
        this.SendTo(player, runner, this.RespawnPosition(runner, 0), NetworkManager.ServerTime.Time);
    }

    protected override void VisualTick(float deltaTime)
    {
        if (this.signalLamps == null) return;
        this.lampProperties ??= new MaterialPropertyBlock();
        for (int i = 0; i < this.signalLamps.Length; i++)
        {
            Renderer lamp = this.signalLamps[i];
            if (lamp == null) continue;
            SignalState lampState = (SignalState)(i % 3);
            bool lit = IsPlaying && lampState == this.signal.Value;
            Color color = ColorFor(lampState) * (lit ? 1f : .14f);
            color.a = 1f;
            lamp.GetPropertyBlock(this.lampProperties);
            this.lampProperties.SetColor("_BaseColor", color);
            this.lampProperties.SetColor("_Color", color);
            this.lampProperties.SetColor("_EmissionColor", lit ? color * 1.4f : Color.black);
            lamp.SetPropertyBlock(this.lampProperties);
        }
    }

    public override string GetStatus(ulong clientId)
    {
        if (this.runnerStates == null) return "青信号でスタート！";
        for (int i = 0; i < this.runnerStates.Count; i++)
        {
            SignalRunnerSnapshot state = this.runnerStates[i];
            if (state.ClientId != clientId) continue;
            if (NetworkManager != null && NetworkManager.ServerTime.Time < state.NoticeUntil)
            {
                if (state.Notice == 1) return "動いた！ チェックポイントへ戻ります";
                if (state.Notice == 2) return $"ゴール！ +{this.pointsPerLap}点　もう1周！";
                if (state.Notice == 3) return "コースアウト！ スタートから再挑戦";
            }
            int total = this.checkpoints != null ? this.checkpoints.Length : 0;
            return $"{state.Laps}周 / {state.Laps * this.pointsPerLap}点　CP {state.Checkpoint}/{total}　違反 {state.Penalties}回";
        }
        return "青で進み、赤で止まろう";
    }

    /// <summary>赤ではアンカーからの累積移動を判定し、垂直方向の落下・着地は無視する。</summary>
    public static bool MovedWhileStopped(Vector3 anchor, Vector3 position, float tolerance)
    {
        float dx = position.x - anchor.x;
        float dz = position.z - anchor.z;
        float allowed = Mathf.Max(.1f, tolerance);
        return dx * dx + dz * dz > allowed * allowed;
    }

    public static bool CanAdvance(SignalState state, double phaseStart, double now, float grace)
        => state != SignalState.Red || now < phaseStart + Mathf.Max(0f, grace);

    private static Color ColorFor(SignalState state) => state switch
    {
        SignalState.Green => new Color(.16f, 1f, .55f),
        SignalState.Amber => new Color(1f, .7f, .12f),
        _ => new Color(1f, .2f, .28f)
    };
}

public struct SignalRunnerSnapshot : INetworkSerializable, IEquatable<SignalRunnerSnapshot>
{
    public ulong ClientId;
    public int Checkpoint;
    public int Laps;
    public int Penalties;
    public byte Notice;
    public double NoticeUntil;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref this.ClientId);
        serializer.SerializeValue(ref this.Checkpoint);
        serializer.SerializeValue(ref this.Laps);
        serializer.SerializeValue(ref this.Penalties);
        serializer.SerializeValue(ref this.Notice);
        serializer.SerializeValue(ref this.NoticeUntil);
    }

    public bool Equals(SignalRunnerSnapshot other) => this.ClientId == other.ClientId
        && this.Checkpoint == other.Checkpoint && this.Laps == other.Laps && this.Penalties == other.Penalties
        && this.Notice == other.Notice && this.NoticeUntil.Equals(other.NoticeUntil);
}
