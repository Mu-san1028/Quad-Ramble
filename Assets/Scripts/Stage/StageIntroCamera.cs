using Unity.Cinemachine;
using UnityEngine;

/// <summary>ステージ紹介中だけ実カメラを指定地点へ動かし、終了時に追従制御へ戻す。</summary>
[DisallowMultipleComponent]
public sealed class StageIntroCamera : MonoBehaviour
{
    [Header("紹介に使うカメラ")]
    [Tooltip("未設定ならMainCameraタグのカメラを使用")]
    [SerializeField] private Camera targetCamera;

    [Header("通過地点")]
    [SerializeField] private Transform[] points;
    [Tooltip("Pointsと同じ番号に注視対象を設定。未設定の地点ではPointのRotationを使用")]
    [SerializeField] private Transform[] lookTargets;
    [SerializeField, Min(0.01f)] private float secondsPerSegment = 2f;

    private CinemachineBrain brain;
    private bool originalBrainEnabled;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool controlling;
    private bool introStarted;
    private bool finished;
    private float elapsed;

    private void OnEnable()
    {
        finished = false;
        introStarted = false;
        elapsed = 0f;
    }

    private void Update()
    {
        if (finished) return;

        var round = RoundManager.Instance;

        // 同期前の初期値を、紹介終了と取り違えない。
        if (round == null || !round.IsSpawned)
        {
            if (introStarted)
                Finish();

            return;
        }

        if (!round.IsReady)
            return;

        if (!round.IsIntro)
        {
            Finish();
            return;
        }

        if (!controlling && !Begin())
        {
            finished = true;
            return;
        }

        introStarted = true;

        // 幕が開くまでは最初の地点で待機する。
        if (SceneTransitionUI.Instance != null && SceneTransitionUI.Instance.IsCovering)
            return;

        elapsed += Time.deltaTime;
        ApplyPose();
    }

    private bool Begin()
    {
        if (!targetCamera)
            targetCamera = Camera.main;

        if (!targetCamera || points == null || points.Length == 0)
        {
            Debug.LogWarning("StageIntroCamera: カメラと通過地点を設定してください。", this);
            return false;
        }

        foreach (Transform point in points)
        {
            if (!point || point == targetCamera.transform || point.IsChildOf(targetCamera.transform))
            {
                Debug.LogWarning("StageIntroCamera: 通過地点をすべて設定し、動かすカメラの外側に配置してください。", this);
                return false;
            }
        }

        originalPosition = targetCamera.transform.position;
        originalRotation = targetCamera.transform.rotation;
        brain = targetCamera.GetComponent<CinemachineBrain>();
        originalBrainEnabled = brain != null && brain.enabled;

        if (brain)
            brain.enabled = false;

        controlling = true;
        ApplyPose();
        return true;
    }

    private void ApplyPose()
    {
        if (!targetCamera)
        {
            Finish();
            return;
        }

        float routePosition = Mathf.Min(
            elapsed / Mathf.Max(0.01f, secondsPerSegment), points.Length - 1);
        int index = Mathf.FloorToInt(routePosition);
        int nextIndex = Mathf.Min(index + 1, points.Length - 1);
        Transform from = points[index];
        Transform to = points[nextIndex];

        if (!from || !to)
        {
            Finish();
            return;
        }

        float t = routePosition - index;
        Vector3 position = Vector3.Lerp(from.position, to.position, t);
        Transform fromTarget = GetLookTarget(index);
        Transform toTarget = GetLookTarget(nextIndex);
        Quaternion rotation;

        if (fromTarget && toTarget)
        {
            // 同じ対象なら移動中も見続け、別の対象なら注視位置を徐々に移す。
            Vector3 focus = Vector3.Lerp(fromTarget.position, toTarget.position, t);
            rotation = LookRotation(position, focus, targetCamera.transform.rotation);
        }
        else
        {
            Quaternion fromRotation = fromTarget
                ? LookRotation(from.position, fromTarget.position, from.rotation)
                : from.rotation;
            Quaternion toRotation = toTarget
                ? LookRotation(to.position, toTarget.position, to.rotation)
                : to.rotation;
            rotation = Quaternion.Slerp(fromRotation, toRotation, t);
        }

        targetCamera.transform.SetPositionAndRotation(position, rotation);
    }

    private Transform GetLookTarget(int index)
    {
        return lookTargets != null && index < lookTargets.Length ? lookTargets[index] : null;
    }

    private static Quaternion LookRotation(Vector3 position, Vector3 focus, Quaternion fallback)
    {
        Vector3 direction = focus - position;
        if (direction.sqrMagnitude < 0.0001f)
            return fallback;

        direction.Normalize();
        Vector3 up = Vector3.up;

        // 真上・真下を見る場合も、有効な上方向を確保する。
        if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.999f)
        {
            up = fallback * Vector3.up;
            if (Mathf.Abs(Vector3.Dot(direction, up)) > 0.999f)
                up = Vector3.forward;
        }

        return Quaternion.LookRotation(direction, up);
    }

    private void Finish()
    {
        finished = true;

        if (!controlling) return;

        if (targetCamera)
            targetCamera.transform.SetPositionAndRotation(originalPosition, originalRotation);

        if (brain)
            brain.enabled = originalBrainEnabled;

        controlling = false;
    }

    private void OnDisable()
    {
        Finish();
    }
}
