using System.Collections;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;

/// <summary>固定地形のNavMeshをホストで読み込み時に一度だけ生成する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshSurface))]
public sealed class BotNavigationSurface : MonoBehaviour
{
    [Tooltip("ベイク済みデータがない場合、ホストで一度だけ生成する。動的地形の更新は行わない。")]
    [SerializeField] private bool buildOnServer = false;
    private Coroutine buildRoutine;

    private void OnEnable()
    {
        if (buildOnServer) buildRoutine = StartCoroutine(BuildWhenServerStarts());
    }

    private IEnumerator BuildWhenServerStarts()
    {
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            yield return null;

        // シーンのStart処理とCollider初期化が済んでから作成する。
        yield return null;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            var surface = GetComponent<NavMeshSurface>();
            if (surface.isActiveAndEnabled && surface.navMeshData == null)
            {
                Physics.SyncTransforms();
                surface.BuildNavMesh();
                if (surface.navMeshData == null)
                    Debug.LogWarning("[BotNavigation] NavMeshを作成できませんでした。地形ColliderとSurfaceの設定を確認してください。", this);
            }
        }
        buildRoutine = null;
    }

    private void OnDisable()
    {
        if (buildRoutine != null) StopCoroutine(buildRoutine);
        buildRoutine = null;
    }
}
