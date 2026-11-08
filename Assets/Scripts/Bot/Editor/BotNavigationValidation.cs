using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>シーンを保存せず、実際のNavMeshで穴の迂回とFactoryの生成を検証する。</summary>
[InitializeOnLoad]
public static class BotNavigationValidation
{
    private const string Request = "Temp/BotNavigationValidation.request";
    private const string BakeRequest = "Temp/BotNavigationBake.request";
    private const string EdgeRequest = "Temp/BotEdgeAvoidanceValidation.request";
    private const string Report = "Temp/BotNavigationValidation.txt";
    private static bool saveBakedAsset;

    static BotNavigationValidation()
    {
        EditorApplication.update += CheckRequest;
    }

    private static void CheckRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || (!File.Exists(Request) && !File.Exists(BakeRequest) && !File.Exists(EdgeRequest))) return;
        saveBakedAsset = File.Exists(BakeRequest);
        File.Delete(saveBakedAsset ? BakeRequest : File.Exists(EdgeRequest) ? EdgeRequest : Request);
        try { Run(); }
        finally { saveBakedAsset = false; }
    }

    [MenuItem("Tools/QUAD RAMBLE/Validate Bot Navigation (No Scene Save)")]
    public static void Run()
    {
        var lines = new List<string>();
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Playを停止してから実行してください。");
            ValidateSyntheticHole(lines);
            ValidateFactory(lines);
            lines.Add("SUCCESS (NavMesh build and path queries only; gameplay and networking not tested)");
        }
        catch (Exception error)
        {
            lines.Add("FAILED: " + error);
        }
        File.WriteAllLines(Report, lines);
        Debug.Log(string.Join("\n", lines));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ValidateSyntheticHole(List<string> lines)
    {
        Vector3 origin = new(1000f, 1000f, 1000f);
        var sources = new List<NavMeshBuildSource>();
        void Floor(float x, float z, float width, float depth)
        {
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(origin + new Vector3(x, -0.5f, z), Quaternion.identity, Vector3.one),
                size = new Vector3(width, 1f, depth), area = 0
            });
        }
        Floor(-4f, 0f, 4f, 12f);
        Floor(4f, 0f, 4f, 12f);
        Floor(0f, -4f, 4f, 4f);
        Floor(0f, 4f, 4f, 4f);
        Floor(20f, 0f, 4f, 4f);
        var settings = NavMesh.GetSettingsByID(0);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources,
            new Bounds(origin + new Vector3(8f, 0, 0), new Vector3(32, 4, 16)), Vector3.zero, Quaternion.identity);
        Require(data != null, "Synthetic NavMesh failed to build");
        var instance = NavMesh.AddNavMeshData(data);
        var temporary = new GameObject("BotNavigationValidation") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var avoidance = temporary.AddComponent<BotEdgeAvoidance>();
            var navigator = temporary.AddComponent<BotPathNavigator>();
            var calculate = typeof(BotPathNavigator).GetMethod("TryCalculate", BindingFlags.Instance | BindingFlags.NonPublic);
            bool Query(Vector3 start, Vector3 goal, out Vector3[] points)
            {
                object[] args = { start, goal, new NavMeshPath(), null };
                bool result = (bool)calculate.Invoke(navigator, args);
                points = args[3] as Vector3[];
                return result;
            }
            Require(NavMesh.SamplePosition(origin + new Vector3(-4, 0, 0), out var start, 0.5f, 1), "Start missing");
            bool detour = Query(start.position, origin + new Vector3(4, 0, 0), out var corners);
            if (!detour)
            {
                var diagnostic = new NavMeshPath();
                bool calculated = NavMesh.CalculatePath(start.position, origin + new Vector3(4, 0, 0), 1, diagnostic);
                lines.Add($"Diagnostic: calculated={calculated}, status={diagnostic.status}, corners={diagnostic.corners.Length}");
                for (int i = 1; i < diagnostic.corners.Length; i++)
                {
                    bool blocked = NavMesh.Raycast(diagnostic.corners[i - 1], diagnostic.corners[i], out var edge, 1);
                    lines.Add($"Segment {i}: {diagnostic.corners[i - 1]:F4} -> {diagnostic.corners[i]:F4}, blocked={blocked}, hit={edge.position:F4}");
                }
            }
            Require(detour, "Hole detour missing");
            Require(corners.Length > 2, "Path did not detour around the hole");
            Require(!Query(start.position, origin, out _), "Hole center must not project across the gap");
            Require(!Query(start.position, origin + new Vector3(20, 0, 0), out _), "Disconnected island must be rejected");
            Require(Query(start.position, origin + new Vector3(4, 1, 0), out _), "Floating item target must project to floor");
            ValidateEdgeAvoidance(avoidance, start.position, origin, lines);
            temporary.transform.position = origin + new Vector3(-4, -2, 0);
            var sample = typeof(BotPathNavigator).GetMethod("TrySampleStart", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(!(bool)sample.Invoke(navigator, new object[] { Vector3.zero }), "Falling Bot must not snap to the floor");
            lines.Add("PASS Synthetic: hole detour, hole rejection, disconnected island, item projection, fallen start rejection");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temporary);
            instance.Remove();
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static void ValidateEdgeAvoidance(BotEdgeAvoidance avoidance, Vector3 safePosition,
        Vector3 hole, List<string> lines)
    {
        var choose = typeof(BotEdgeAvoidance).GetMethod("ChooseInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Vector3 Choose(Vector3 position, Vector3 input)
            => (Vector3)choose.Invoke(avoidance, new object[] { position, input, 0.9f });

        Require(avoidance.IsDestinationSafe(safePosition), "Interior destination was rejected");
        Require(NavMesh.FindClosestEdge(safePosition, out var edge, 1), "Hole boundary missing");
        Vector3 inward = safePosition - edge.position;
        inward.y = 0f;
        inward.Normalize();
        Vector3 nearEdge = edge.position + inward * 0.12f;
        Require(!avoidance.IsDestinationSafe(nearEdge), "Edge destination was accepted");
        Require(Vector3.Dot(Choose(nearEdge, -inward), inward) > 0.1f,
            "Movement towards hole did not retreat inward");
        Require(Vector3.Dot(Choose(nearEdge, Vector3.zero), inward) > 0.1f,
            "Idle Bot near edge did not retreat inward");
        Require(Vector3.Dot(Choose(safePosition, Vector3.forward), Vector3.forward) > 0.9f,
            "Safe interior movement changed unexpectedly");
        Require(Choose(safePosition, Vector3.zero) == Vector3.zero, "Safe idle Bot moved");
        Require(Choose(hole, Vector3.forward) == Vector3.zero, "Hole center was treated as a recovery start");
        Require(Choose(safePosition + Vector3.down * 2f, Vector3.forward) == Vector3.zero,
            "Lower floor snapped to upper NavMesh");
        var groundProbe = typeof(BotEdgeAvoidance).GetMethod("HasGroundAlong", BindingFlags.Instance | BindingFlags.NonPublic);
        var floor = new GameObject("EdgeAvoidanceTestFloor") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            floor.transform.position = safePosition + Vector3.down * 0.5f;
            floor.AddComponent<BoxCollider>().size = new Vector3(4f, 1f, 12f);
            Physics.SyncTransforms();
            Require((bool)groundProbe.Invoke(avoidance, new object[] { safePosition, safePosition + Vector3.forward }),
                "Continuous floor was rejected");
            Require(!(bool)groundProbe.Invoke(avoidance, new object[] { safePosition, hole }),
                "Recovery would walk across a physical floor gap");
            Require(!(bool)groundProbe.Invoke(avoidance, new object[] { safePosition + Vector3.down * 2f, safePosition }),
                "Recovery would step onto a floor above its height limit");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(floor);
        }
        lines.Add("PASS Edge avoidance: boundary target rejection, inward retreat (moving/idle), safe movement, hole/lower floor rejection");
        lines.Add("PASS Ground probes: continuous floor, physical gap rejection, upper floor rejection");
    }

    private static void ValidateFactory(List<string> lines)
    {
        var activeScene = SceneManager.GetActiveScene();
        var preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Rounds/Factory.unity");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        NavMeshSurface surface = null;
        NavMeshData builtData = null;
        try
        {
            // SurfaceのEditor用収集APIはPreviewSceneを対象にしない。
            // 作業中のFactoryは触らず、未保存の一時シーンへ複製して検査する。
            foreach (var root in preview.GetRootGameObjects())
            {
                var copy = UnityEngine.Object.Instantiate(root);
                SceneManager.MoveGameObjectToScene(copy, scene);
            }
            Physics.SyncTransforms();
            surface = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NavMeshSurface>()).Single();
            // 保存済みデータの複製を外してから生成分の三角形数を数える。
            surface.RemoveData();
            surface.navMeshData = null;
            int before = NavMesh.CalculateTriangulation().indices.Length;
            surface.BuildNavMesh();
            builtData = surface.navMeshData;
            Require(builtData != null, "Factory NavMesh missing");
            int added = NavMesh.CalculateTriangulation().indices.Length - before;
            Require(added > 0, "Factory NavMesh has no triangles");
            var boxes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ItemBox>()).ToArray();
            int projected = 0;
            var positions = new List<Vector3>();
            foreach (var box in boxes)
            {
                var collider = box.GetComponent<Collider>();
                Vector3 position = collider != null ? collider.bounds.center : box.transform.position;
                if (NavMesh.SamplePosition(position, out var hit, 2.5f, 1))
                {
                    Vector3 shift = hit.position - position;
                    shift.y = 0f;
                    if (shift.magnitude <= 0.6f)
                    {
                        projected++;
                        positions.Add(hit.position);
                        continue;
                    }
                }
                lines.Add($"Factory excluded box: {box.name} at {position:F2}");
            }
            Require(projected > 0, "No Factory item boxes can project to NavMesh");
            int largestGroup = 0;
            foreach (var start in positions)
            {
                int reachable = 0;
                foreach (var goal in positions)
                {
                    var route = new NavMeshPath();
                    if (NavMesh.CalculatePath(start, goal, 1, route) && route.status == NavMeshPathStatus.PathComplete)
                        reachable++;
                }
                largestGroup = Math.Max(largestGroup, reachable);
            }
            Require(largestGroup >= 2, "Factory box routes are disconnected");
            lines.Add($"PASS Factory: {added / 3} triangles, {projected}/{boxes.Length} box positions projected, largest connected box group={largestGroup}; excluded boxes need manual review");
            if (saveBakedAsset)
            {
                const string assetPath = "Assets/Scenes/Rounds/Factory_BotNavMesh.asset";
                Require(!File.Exists(assetPath), "Baked asset already exists; use the Surface Inspector to rebake.");
                AssetDatabase.CreateAsset(builtData, assetPath);
                AssetDatabase.SaveAssetIfDirty(builtData);
                lines.Add("BAKED: " + assetPath + " GUID=" + AssetDatabase.AssetPathToGUID(assetPath));
            }
        }
        finally
        {
            if (surface != null) surface.RemoveData();
            EditorSceneManager.CloseScene(scene, true);
            EditorSceneManager.ClosePreviewScene(preview);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
            if (builtData != null && !AssetDatabase.Contains(builtData)) UnityEngine.Object.DestroyImmediate(builtData);
        }
    }
}
