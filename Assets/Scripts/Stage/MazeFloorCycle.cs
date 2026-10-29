using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Mazeの床を1枚ずつ予告・落下させ、一定時間後に元の位置へ戻します。
/// 落下のタイミングと床番号はサーバーが決め、全クライアントで同じ動きを再生します。
/// </summary>
public sealed class MazeFloorCycle : NetworkBehaviour
{
    [Header("対象の床")]
    [Tooltip("空の場合、MazeシーンにあるFloorCube_large_prefabで始まる名前の床を起動時に一度だけ取得します。")]
    [SerializeField] private Transform[] floors;
    [SerializeField] private string autoFindNamePrefix = "FloorCube_large_prefab";

    [Header("床が落ちるタイミング")]
    [Tooltip("ラウンド開始から最初の床が揺れ始めるまでの秒数")]
    [SerializeField, Min(0f)] private float firstDropDelay = 8f;
    [Tooltip("次の床が揺れ始めるまでの待ち時間")]
    [SerializeField, Min(0f)] private float delayBetweenFloors = 2f;
    [Tooltip("床が揺れて、落下するまでの予告時間")]
    [SerializeField, Min(0.1f)] private float warningDuration = 2f;
    [Tooltip("床がゆっくり落ちる時間")]
    [SerializeField, Min(0.1f)] private float fallDuration = 2f;
    [Tooltip("落ちた床が見えなくなっている時間")]
    [SerializeField, Min(0f)] private float disappearDuration = 3f;
    [Tooltip("予告中に床が揺れる幅（度）")]
    [SerializeField, Min(0f)] private float shakeAngle = 3f;
    [Tooltip("床が落ちる距離")]
    [SerializeField, Min(0.1f)] private float fallDistance = 12f;

    // サーバーが選んだ床番号と開始時刻を全クライアントに同期します。
    private readonly NetworkVariable<int> activeFloorIndex = new(-1);
    private readonly NetworkVariable<double> activeFloorStartTime = new(-1d);
    private readonly NetworkVariable<double> nextDropTime = new(-1d);

    // 各床の初期状態を保存し、再出現時に元通りに戻せるようにします。
    private Vector3[] startingPositions;
    private Quaternion[] startingRotations;
    private Collider[][] floorColliders;
    private bool[][] colliderWasEnabled;
    private Renderer[][] floorRenderers;
    private bool[][] rendererWasEnabled;
    private bool initialized;
    private int previousFloorIndex = -1;

    public override void OnNetworkSpawn()
    {
        this.FindAndCacheFloors();

        if (IsServer)
        {
            this.activeFloorIndex.Value = -1;
            this.activeFloorStartTime.Value = -1d;
            this.nextDropTime.Value = -1d;
            this.previousFloorIndex = -1;
        }
    }

    private void Update()
    {
        if (!this.initialized || RoundManager.Instance == null || !RoundManager.Instance.IsSpawned)
            return;

        RoundManager round = RoundManager.Instance;
        if (round.IsRoundOver)
        {
            // 結果発表に入ったとき、落下途中の床もすぐに元の位置へ戻します。
            this.RestoreAllFloors();
            return;
        }

        if (round.IsIntro || round.CountdownRemaining > RoundManager.StartDisplayDuration)
            return;

        double now = NetworkManager.ServerTime.Time;
        if (IsServer)
            this.UpdateServerSchedule(now);

        this.UpdateFloorAnimation(now);
    }

    /// <summary>床を起動時に一度だけ探し、動かすための初期情報を保存します。</summary>
    private void FindAndCacheFloors()
    {
        // Inspectorで登録されていない場合だけ、Mazeシーンの対象床をまとめて取得します。
        if (this.floors == null || this.floors.Length == 0)
        {
            Transform[] sceneTransforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var foundFloors = new List<Transform>();
            foreach (Transform candidate in sceneTransforms)
            {
                if (candidate != null
                    && candidate.gameObject.scene == gameObject.scene
                    && candidate.name.StartsWith(this.autoFindNamePrefix, StringComparison.Ordinal))
                {
                    foundFloors.Add(candidate);
                }
            }

            // 全クライアントで床番号が一致するよう、名前順に並べます。
            foundFloors.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            this.floors = foundFloors.ToArray();
        }

        int count = this.floors.Length;
        this.startingPositions = new Vector3[count];
        this.startingRotations = new Quaternion[count];
        this.floorColliders = new Collider[count][];
        this.colliderWasEnabled = new bool[count][];
        this.floorRenderers = new Renderer[count][];
        this.rendererWasEnabled = new bool[count][];

        for (int i = 0; i < count; i++)
        {
            Transform floor = this.floors[i];
            if (floor == null)
                continue;

            this.startingPositions[i] = floor.localPosition;
            this.startingRotations[i] = floor.localRotation;

            // 子オブジェクトに見た目や当たり判定がある床Prefabにも対応します。
            this.floorColliders[i] = floor.GetComponentsInChildren<Collider>(true);
            this.colliderWasEnabled[i] = new bool[this.floorColliders[i].Length];
            for (int j = 0; j < this.floorColliders[i].Length; j++)
                this.colliderWasEnabled[i][j] = this.floorColliders[i][j].enabled;

            this.floorRenderers[i] = floor.GetComponentsInChildren<Renderer>(true);
            this.rendererWasEnabled[i] = new bool[this.floorRenderers[i].Length];
            for (int j = 0; j < this.floorRenderers[i].Length; j++)
                this.rendererWasEnabled[i][j] = this.floorRenderers[i][j].enabled;
        }

        if (count == 0)
            Debug.LogError("[MazeFloorCycle] 対象の床が見つかりません。床の名前かInspector設定を確認してください。", this);
        else if (count != 16)
            Debug.LogWarning($"[MazeFloorCycle] 対象の床が{count}枚見つかりました（想定は16枚）。", this);

        this.initialized = true;
    }

    /// <summary>次の床を落とす時刻になったら、サーバーがランダムに1枚を選びます。</summary>
    private void UpdateServerSchedule(double now)
    {
        if (this.floors.Length == 0)
            return;

        if (this.nextDropTime.Value < 0d)
        {
            this.nextDropTime.Value = now + this.firstDropDelay;
            return;
        }

        if (this.activeFloorIndex.Value >= 0)
        {
            float totalDuration = this.warningDuration + this.fallDuration + this.disappearDuration;
            if (now - this.activeFloorStartTime.Value >= totalDuration)
            {
                this.activeFloorIndex.Value = -1;
                this.activeFloorStartTime.Value = -1d;
                this.nextDropTime.Value = now + this.delayBetweenFloors;
            }

            return;
        }

        if (now < this.nextDropTime.Value)
            return;

        // 直前と同じ床が続かないように、可能なら別の床を選びます。
        int selectedIndex = UnityEngine.Random.Range(0, this.floors.Length);
        if (this.floors.Length > 1 && selectedIndex == this.previousFloorIndex)
            selectedIndex = (selectedIndex + 1) % this.floors.Length;

        this.previousFloorIndex = selectedIndex;
        this.activeFloorStartTime.Value = now;
        this.activeFloorIndex.Value = selectedIndex;
    }

    /// <summary>共有された床番号と時刻から、揺れ・落下・再出現を描画します。</summary>
    private void UpdateFloorAnimation(double now)
    {
        int selectedIndex = this.activeFloorIndex.Value;
        if (selectedIndex < 0 || selectedIndex >= this.floors.Length)
        {
            this.RestoreAllFloors();
            return;
        }

        float age = (float)(now - this.activeFloorStartTime.Value);
        for (int i = 0; i < this.floors.Length; i++)
        {
            Transform floor = this.floors[i];
            if (floor == null)
                continue;

            if (i != selectedIndex)
            {
                this.RestoreFloor(i);
                continue;
            }

            if (age < this.warningDuration)
            {
                // 予告中は床の中心を保ったまま、左右に小さく揺らします。
                float progress = Mathf.Clamp01(age / this.warningDuration);
                float shake = Mathf.Sin(age * 2f * Mathf.PI * 4f) * this.shakeAngle * progress;
                floor.localPosition = this.startingPositions[i];
                floor.localRotation = this.startingRotations[i] * Quaternion.Euler(shake, 0f, -shake);
                this.SetCollidersEnabled(i, true);
                this.SetRenderersEnabled(i, true);
            }
            else if (age < this.warningDuration + this.fallDuration)
            {
                // 落下は加速をつけてゆっくり始まり、最後に速くなるよう補間します。
                float progress = (age - this.warningDuration) / this.fallDuration;
                float easedProgress = progress * progress;
                floor.localPosition = this.startingPositions[i] + Vector3.down * (this.fallDistance * easedProgress);
                floor.localRotation = this.startingRotations[i];
                this.SetCollidersEnabled(i, false);
                this.SetRenderersEnabled(i, true);
            }
            else if (age < this.warningDuration + this.fallDuration + this.disappearDuration)
            {
                floor.localPosition = this.startingPositions[i] + Vector3.down * this.fallDistance;
                floor.localRotation = this.startingRotations[i];
                this.SetCollidersEnabled(i, false);
                this.SetRenderersEnabled(i, false);
            }
            else
            {
                // 指定時間が過ぎたら、床の表示・当たり判定・位置を初期状態へ戻します。
                this.RestoreFloor(i);
            }
        }

        // 移動したTransformの新しい位置を、このフレームの物理判定にも反映します。
        Physics.SyncTransforms();
    }

    /// <summary>床1枚の位置、向き、表示、当たり判定を保存状態に戻します。</summary>
    private void RestoreFloor(int index)
    {
        Transform floor = this.floors[index];
        if (floor == null)
            return;

        floor.localPosition = this.startingPositions[index];
        floor.localRotation = this.startingRotations[index];
        for (int j = 0; j < this.floorColliders[index].Length; j++)
            if (this.floorColliders[index][j] != null)
                this.floorColliders[index][j].enabled = this.colliderWasEnabled[index][j];
        for (int j = 0; j < this.floorRenderers[index].Length; j++)
            if (this.floorRenderers[index][j] != null)
                this.floorRenderers[index][j].enabled = this.rendererWasEnabled[index][j];
    }

    /// <summary>すべての床を結果表示用に元の状態へ戻します。</summary>
    private void RestoreAllFloors()
    {
        if (!this.initialized)
            return;

        for (int i = 0; i < this.floors.Length; i++)
        {
            Transform floor = this.floors[i];
            if (floor == null)
                continue;

            floor.localPosition = this.startingPositions[i];
            floor.localRotation = this.startingRotations[i];
            for (int j = 0; j < this.floorColliders[i].Length; j++)
                if (this.floorColliders[i][j] != null)
                    this.floorColliders[i][j].enabled = this.colliderWasEnabled[i][j];
            for (int j = 0; j < this.floorRenderers[i].Length; j++)
                if (this.floorRenderers[i][j] != null)
                    this.floorRenderers[i][j].enabled = this.rendererWasEnabled[i][j];
        }
    }

    private void SetCollidersEnabled(int index, bool enabled)
    {
        for (int j = 0; j < this.floorColliders[index].Length; j++)
            if (this.floorColliders[index][j] != null)
                this.floorColliders[index][j].enabled = enabled && this.colliderWasEnabled[index][j];
    }

    private void SetRenderersEnabled(int index, bool enabled)
    {
        for (int j = 0; j < this.floorRenderers[index].Length; j++)
            if (this.floorRenderers[index][j] != null)
                this.floorRenderers[index][j].enabled = enabled && this.rendererWasEnabled[index][j];
    }
}
