using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>アセットを生成せず、静止Botの登録・物理・得点・ラウンド継続を検証する。</summary>
[InitializeOnLoad]
public static class BotParticipantValidation
{
    private const string Active = "QuadRamble.BotValidation.Active";
    private const string Restore = "QuadRamble.BotValidation.Restore";
    private const string SetupKey = "QuadRamble.BotValidation.Setup";
    private const string Request = "Temp/BotParticipantValidation.request";
    private const string VariantRequest = "Temp/PlayerVariantsValidation.request";
    private const string ReportPath = "Temp/BotParticipantValidation.txt";
    [Serializable] private class SceneSetupData { public SceneSetup[] scenes; }
    private static readonly List<string> report = new();
    private static int phase;
    private static double started;
    private static double actionAt;
    private static PlayerController bot;
    private static PlayerController human;
    private static ulong botParticipant;
    private static Vector3 beforeHit;
    private static string runtimeError;

    static BotParticipantValidation()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Exception
                || (type == LogType.Warning && message.Contains("NetworkVariable is written to"))))
                runtimeError = message + "\n" + stack;
        };
        started = EditorApplication.timeSinceStartup;
    }

    [MenuItem("Tools/QUAD RAMBLE/Validate Static Bot (Play Mode)")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Playを停止してください。");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("未保存のシーンがあります。保存してから実行してください。");

        ValidatePrefab("Assets/Prefabs/Player/PlayerBase.prefab");
        ValidatePrefab("Assets/Prefabs/Player/Human.prefab");
        ValidatePrefab("Assets/Prefabs/Player/Bot.prefab");
        ValidatePrefab("Assets/Prefabs/ConnectionContainer.prefab");
        ValidatePrefab("Assets/Prefabs/UI/LobbyBotControls.prefab");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SceneSetupData { scenes = EditorSceneManager.GetSceneManagerSetup() }));
        EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
        SessionState.SetBool(Active, true);
        phase = 0;
        started = EditorApplication.timeSinceStartup;
        report.Clear();
        if (File.Exists(ReportPath)) File.Delete(ReportPath);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetBool(Restore, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(Restore, false);
            var setup = JsonUtility.FromJson<SceneSetupData>(SessionState.GetString(SetupKey, ""));
            if (setup?.scenes != null) EditorSceneManager.RestoreSceneManagerSetup(setup.scenes);
        }
        if ((File.Exists(Request) || File.Exists(VariantRequest)) && !SessionState.GetBool(Active, false))
        {
            File.Delete(File.Exists(VariantRequest) ? VariantRequest : Request);
            try { Begin(); }
            catch (Exception error) { File.WriteAllText(ReportPath, "NOT RUN: " + error); }
            return;
        }
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            Require(EditorApplication.timeSinceStartup - started < 240, "検証が240秒以内に完了する");
            Step();
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Step()
    {
        var roster = ConnectionContainer.Instance;
        if (phase == 0)
        {
            var connect = UnityEngine.Object.FindAnyObjectByType<NetworkConnect>();
            if (connect == null || GameManager.Instance == null || SceneManager.GetActiveScene().name != "Title") return;
            // シーン・Prefabを保存せず、テスト中の値だけ変更する。
            var game = new SerializedObject(GameManager.Instance);
            var stages = game.FindProperty("stageNames");
            stages.arraySize = 2;
            stages.GetArrayElementAtIndex(0).stringValue = "SkyArena";
            stages.GetArrayElementAtIndex(1).stringValue = "SkyArena";
            game.FindProperty("stageSelectionMode").intValue = 0;
            game.ApplyModifiedPropertiesWithoutUndo();
            var data = new SerializedObject(connect);
            data.FindProperty("connectionMode").intValue = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
            // NetworkConnect.Startがtransportを取得するのを待つ。
            if (typeof(NetworkConnect).GetField("transport", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(connect) == null) return;
            connect.CreateHost(2);
            phase = 1;
        }
        else if (phase == 1 && roster != null && PlayerStatus.Local != null && roster.GetConnectionsNum() == 1)
        {
            var manager = roster.GetComponent<BotParticipantManager>();
            Require(manager.CanAdd, "人間1人のロビーへBotを追加できる");
            manager.AddBot();
            Require(roster.BotCount == 1 && !manager.CanAdd, "Botは参加枠を1つ使い、満員時は追加不可");
            manager.RemoveBot();
            Require(roster.BotCount == 0 && manager.CanAdd, "Botを削除して枠とキャラを解放できる");
            manager.AddBot();
            human = PlayerStatus.Local.GetComponent<PlayerController>();
            bot = PlayerManager.Instance.AllPlayers.Single(p => p.IsBot);
            // この検証は静止時の被弾・脱落を対象にする。自動収集は別途確認する。
            bot.GetComponent<BotItemCollector>().enabled = false;
            bot.GetComponent<BotBrain>().enabled = false;
            botParticipant = bot.ParticipantId;
            var botEntry = roster.GetConnectionByPlayerId(botParticipant);
            Require(roster.GetConnection(NetworkManager.ServerClientId) != botEntry && botEntry.IsReady,
                "Botはホストと別の名簿で自動準備完了");
            roster.SelectCharacterRpc(botEntry.CharacterIndex);
            Require(roster.GetConnection(NetworkManager.ServerClientId).CharacterIndex < 0,
                "人間がBotのキャラを重複選択できない");
            int character = Enumerable.Range(0, CharacterDatabase.Instance.Count).First(i => roster.GetOwnerOf(i) == null);
            roster.SelectCharacterRpc(character);
            roster.SetReadyRpc(true);
            report.Add("PASS Lobby: add, remove, capacity, unique participant, character reservation, ready.");
            phase = 2;
        }
        else if (phase == 2 && Playing(1))
        {
            Require(PlayerManager.Instance.Players.Count == 2, "Botを含む2人が試合へ出現する");
            Require(PlayerStatus.Local.gameObject == human.gameObject && PlayerItem.Local.gameObject == human.gameObject,
                "BotがホストのLocal参照を上書きしない");
            Require(bot.GetComponent<PlayerInput>() == null && bot.GetComponent<HumanPlayerInput>() == null
                && human.GetComponent<PlayerInput>() != null && human.GetComponent<HumanPlayerInput>() != null
                && human.GetComponent<BotBrain>() == null && human.IsLocalHuman && !bot.IsLocalHuman,
                "人間とBotのVariantはそれぞれ専用の入力コンポーネントだけを持つ");
            Require(bot.IsOwner && bot.IsServer && bot.ParticipantId != human.ParticipantId,
                "Botの物理権限はサーバー、参加者IDは人間と独立");
            var identity = bot.GetComponent<PlayerIdentity>();
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            Require(!(bool)typeof(PlayerIdentity).GetField("pendingBot", fields).GetValue(identity),
                "Spawn後は一時設定から同期変数へ反映済み");
            Require(((NetworkVariable<bool>)typeof(PlayerIdentity).GetField("bot", fields).GetValue(identity)).Value,
                "同期されるBotフラグが有効");
            Require(bot.LastPlacementRound == 1 && bot.GetComponent<PlayerItem>().LastWeaponsResetRound == 1,
                "Botの配置と持ち物リセットが完了する");
            beforeHit = bot.transform.position;
            actionAt = EditorApplication.timeSinceStartup;
            phase = 3;
        }
        else if (phase == 3 && EditorApplication.timeSinceStartup - actionAt > 0.5)
        {
            Vector3 drift = bot.transform.position - beforeHit;
            Require(new Vector2(drift.x, drift.z).magnitude < 0.05f, "Botは自発的に歩かない");
            beforeHit = bot.transform.position;
            var status = bot.GetComponent<PlayerStatus>();
            float previous = status.Fatigue;
            typeof(PlayerController).GetMethod("ApplyKnockback", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(human, new object[] { bot.NetworkObjectId, Vector3.forward, 2f, false });
            Require(status.Fatigue > previous, "既存攻撃経路でBotの疲労が増加する");
            actionAt = EditorApplication.timeSinceStartup;
            phase = 4;
        }
        else if (phase == 4 && EditorApplication.timeSinceStartup - actionAt > 0.3)
        {
            Require(Vector3.Distance(beforeHit, bot.transform.position) > 0.05f, "静止Botも吹き飛ばされる");
            DropOutSensor.Apply(bot, -1, false);
            Require(bot.IsEliminated && RoundManager.Instance.IsRoundOver, "Bot脱落で残り1人となりラウンド終了");
            var results = RoundManager.Instance.RoundResults;
            Require(results.Length == 2 && results.Any(r => r.ParticipantId == botParticipant), "Botの結果を独立集計する");
            Require(roster.GetConnectionByPlayerId(human.ParticipantId).Score > roster.GetConnectionByPlayerId(botParticipant).Score,
                "生存者とBotへ異なる順位点を加算する");
            report.Add("PASS Round 1: ownership, idle, damage, knockback, elimination, ranking, independent scores.");
            phase = 5;
        }
        else if (phase == 5 && Playing(2))
        {
            Require(bot != null && bot.IsSpawned && !bot.IsEliminated && PlayerManager.Instance.Players.Contains(bot),
                "同じBotが次ラウンドで復活する");
            Require(bot.ParticipantId == botParticipant && bot.GetComponent<PlayerItem>().LastWeaponsResetRound == 2,
                "Botの参加者IDを保持して次ラウンドの持ち物をリセットする");
            DropOutSensor.Apply(bot, -1, false);
            report.Add("PASS Round 2: transition barrier completed, same participant revived and reset.");
            phase = 6;
        }
        else if (phase == 6 && SceneManager.GetActiveScene().name == "ResultScene" && !SceneTransitionUI.InputBlocked)
        {
            Require(roster.Result.Count == 2 && roster.Result.Any(r => r.Name.StartsWith("BOT")),
                "最終結果に人間とBotを表示する");
            report.Add("PASS Results: both participants included, transition input released.");
            report.Add("PASS NetworkVariable initialization: no pre-spawn write warnings.");
            Finish(true, null);
        }
    }

    private static bool Playing(int round) => ConnectionContainer.Instance != null
        && ConnectionContainer.Instance.CurrentRound == round && RoundManager.Instance != null
        && RoundManager.Instance.IsReady && !RoundManager.Instance.IsIntro && !RoundManager.Instance.IsRoundOver
        && GameManager.Instance.currentState == GameState.Playing && !SceneTransitionUI.InputBlocked;

    private static void ValidatePrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Require(prefab != null, "Prefabが読み込める: " + path);
        foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                "Missing Scriptがない: " + path + "/" + transform.name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(bool success, string error)
    {
        SessionState.SetBool(Active, false);
        SessionState.SetBool(Restore, true);
        report.Add(success ? "SUCCESS" : "FAIL: " + error);
        File.WriteAllLines(ReportPath, report);
        EditorApplication.ExitPlaymode();
    }
}
