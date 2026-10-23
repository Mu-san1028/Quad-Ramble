using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>Titleの既存プレイヤーを固定表示し、シーン終了時に表示と操作を戻す。</summary>
[DefaultExecutionOrder(-100)]
public class TitlePlayerPresentation : MonoBehaviour
{
    [Header("自分のキャラの立ち位置・向き")]
    [SerializeField] private Transform displayPoint;

    private sealed class PlayerState
    {
        public PlayerController player;
        public PlayerAppearance appearance;
        public CharacterController controller;
        public bool previousTitleMode;
        public bool placed;

        public readonly Dictionary<Renderer, bool> renderers = new();
        public readonly Dictionary<Canvas, bool> canvases = new();
    }

    private readonly Dictionary<PlayerController, PlayerState> players = new();
    private readonly List<PlayerController> removedPlayers = new();
    private readonly List<Renderer> rendererBuffer = new();
    private readonly List<Canvas> canvasBuffer = new();

    private PlayerState localPlayer;
    private NetworkSceneManager subscribedSceneManager;
    private bool leavingTitle;

    private void OnEnable()
    {
        this.leavingTitle = false;

        if (this.displayPoint != null)
            return;

        Debug.LogError("TitlePlayerPresentation: Display Pointに台の立ち位置を設定してください。", this);
        this.enabled = false;
    }

    private void Update()
    {
        this.TrackSceneManager();
        this.RemoveDepartedPlayers();

        PlayerManager manager = PlayerManager.Instance;
        if (manager == null)
            return;

        this.localPlayer = null;

        foreach (PlayerController player in manager.AllPlayers)
        {
            if (player == null || !player.IsSpawned)
                continue;

            if (!this.players.TryGetValue(player, out PlayerState state))
            {
                state = new PlayerState
                {
                    player = player,
                    appearance = player.GetComponentInChildren<PlayerAppearance>(true),
                    controller = player.GetComponent<CharacterController>(),
                    previousTitleMode = player.IsTitleMode
                };

                this.players.Add(player, state);
                state.appearance?.ResetTitleModelRotation();
            }

            player.SetTitleMode(true);

            if (player.IsOwner)
            {
                this.localPlayer = state;
                this.RestoreVisuals(state);
                this.PlaceLocalPlayer(state);
            }
            else
            {
                state.placed = false;
            }
        }
    }

    private void LateUpdate()
    {
        foreach (PlayerState state in this.players.Values)
        {
            if (state.player == null || !state.player.IsSpawned)
                continue;

            if (state.player.IsOwner)
            {
                this.PlaceLocalPlayer(state);
            }
            else
            {
                // モデルや名前表示が後から生成されても、描画前に非表示へ反映する。
                this.HideVisuals(state);
            }
        }
    }

    private void TrackSceneManager()
    {
        NetworkSceneManager sceneManager = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.SceneManager : null;

        if (sceneManager == this.subscribedSceneManager)
            return;

        this.UnsubscribeSceneManager();
        this.subscribedSceneManager = sceneManager;

        if (sceneManager != null)
            sceneManager.OnSceneEvent += this.HandleSceneEvent;
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType != SceneEventType.Load
            || sceneEvent.LoadSceneMode != UnityEngine.SceneManagement.LoadSceneMode.Single)
            return;

        // ロード開始後はUpdate/LateUpdateで展示位置へ戻さない。
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer
            && PlayerManager.Instance != null)
        {
            foreach (PlayerController player in PlayerManager.Instance.AllPlayers)
            {
                if (player != null && player.IsSpawned)
                    player.StopOrMovePlayer(false);
            }
        }

        this.leavingTitle = true;
        this.enabled = false;
    }

    private void UnsubscribeSceneManager()
    {
        if (this.subscribedSceneManager == null)
            return;

        this.subscribedSceneManager.OnSceneEvent -= this.HandleSceneEvent;
        this.subscribedSceneManager = null;
    }

    /// <summary>Title側の入力処理から、フレームごとの回転角度を渡す。</summary>
    public void RotateLocalModel(float degrees)
    {
        if (!this.isActiveAndEnabled || this.localPlayer == null)
            return;

        PlayerController player = this.localPlayer.player;
        if (player == null || !player.IsSpawned || !player.IsOwner || !player.IsTitleMode)
            return;

        this.localPlayer.appearance?.RotateTitleModel(degrees);
    }

    private void PlaceLocalPlayer(PlayerState state)
    {
        if (this.displayPoint == null)
            return;

        Transform target = state.player.transform;
        Vector3 position = this.displayPoint.position;
        Quaternion rotation = this.displayPoint.rotation;

        if (state.placed && target.position == position && target.rotation == rotation)
            return;

        bool controllerEnabled = state.controller != null && state.controller.enabled;
        if (controllerEnabled)
            state.controller.enabled = false;

        target.SetPositionAndRotation(position, rotation);

        // 初回だけ所有者から瞬間移動を同期する。他人の座標は変更しない。
        if (!state.placed)
        {
            NetworkTransform networkTransform = state.player.GetComponent<NetworkTransform>();
            if (networkTransform != null)
                networkTransform.Teleport(position, rotation, target.localScale);
        }

        if (controllerEnabled)
            state.controller.enabled = true;

        state.placed = true;
    }

    private void HideVisuals(PlayerState state)
    {
        state.player.GetComponentsInChildren<Renderer>(true, this.rendererBuffer);

        foreach (Renderer renderer in this.rendererBuffer)
        {
            if (!state.renderers.ContainsKey(renderer))
                state.renderers.Add(renderer, renderer.forceRenderingOff);

            renderer.forceRenderingOff = true;
        }

        state.player.GetComponentsInChildren<Canvas>(true, this.canvasBuffer);

        foreach (Canvas canvas in this.canvasBuffer)
        {
            if (!state.canvases.ContainsKey(canvas))
                state.canvases.Add(canvas, canvas.enabled);

            canvas.enabled = false;
        }
    }

    private void RestoreVisuals(PlayerState state)
    {
        foreach (var entry in state.renderers)
        {
            if (entry.Key != null)
                entry.Key.forceRenderingOff = entry.Value;
        }

        foreach (var entry in state.canvases)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }

        state.renderers.Clear();
        state.canvases.Clear();
    }

    private void RestorePlayer(PlayerState state)
    {
        this.RestoreVisuals(state);
        state.appearance?.ResetTitleModelRotation();

        // シーン移行中はTitleのPlayerInputがまだデバイスを保持している。
        // 操作の復帰はPlayerController.RefreshMainCameraに任せる。
        if (state.player != null && !this.leavingTitle)
            state.player.SetTitleMode(state.previousTitleMode);
    }

    private void RemoveDepartedPlayers()
    {
        this.removedPlayers.Clear();

        foreach (var entry in this.players)
        {
            if (entry.Key == null || !entry.Key.IsSpawned)
            {
                this.RestorePlayer(entry.Value);
                this.removedPlayers.Add(entry.Key);
            }
        }

        foreach (PlayerController player in this.removedPlayers)
        {
            this.players.Remove(player);
        }
    }

    private void OnDisable()
    {
        this.UnsubscribeSceneManager();

        // 操作復帰に問題があっても、全員の非表示解除は先に完了させる。
        foreach (PlayerState state in this.players.Values)
        {
            this.RestoreVisuals(state);
        }

        foreach (PlayerState state in this.players.Values)
        {
            this.RestorePlayer(state);
        }

        this.players.Clear();
        this.localPlayer = null;
    }
}
