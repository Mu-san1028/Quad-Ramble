using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 追従カメラの制御を担当する。
/// </summary>
[System.Serializable]
public class PlayerCameraRig
{
    [SerializeField] private float aimFovMultiplier = 0.8f;       // 構えたときの視野の絞り
    [SerializeField] private float aimDistanceMultiplier = 0.75f; // 構えたときにカメラを寄せる量
    [SerializeField] private float aimLerpSpeed = 8f;

    [Header("上を向いたときの引き寄せ")]
    [SerializeField] private float pitchZoomStart = 5f;    // この角度から縮み始める
    [SerializeField] private float pitchZoomEnd = 50f;     // ここで最小になる
    [SerializeField, Range(0.15f, 1f)] private float pitchDistanceMultiplier = 0.4f;

    private CinemachineCamera followCam;
    private CinemachineThirdPersonFollow thirdPerson;
    private float defaultFov;
    private float defaultCameraDistance;

    /// <summary>画面中央のレイを飛ばす実カメラ</summary>
    public Camera Main { get; private set; }

    /// <summary>シーンが変わるたびに、そのシーンのカメラを掴み直す</summary>
    public void Rebind(Transform followTarget)
    {
        Main = Camera.main;

        var followCamObj = GameObject.FindWithTag("FollowCamera");
        if (followCamObj == null) return;

        followCam = followCamObj.GetComponent<CinemachineCamera>();
        if (followCam == null) return;

        followCam.Follow = followTarget;
        followCam.LookAt = null;

        defaultFov = followCam.Lens.FieldOfView;

        thirdPerson = followCam.GetComponent<CinemachineThirdPersonFollow>();
        if (thirdPerson != null) defaultCameraDistance = thirdPerson.CameraDistance;
    }

    public void UpdateAim(bool isAiming, float aimPitch)
    {
        float t = aimLerpSpeed * Time.deltaTime;

        // 構えると視野が絞られる
        if (followCam != null)
        {
            float targetFov = isAiming ? defaultFov * aimFovMultiplier : defaultFov;
            followCam.Lens.FieldOfView = Mathf.Lerp(followCam.Lens.FieldOfView, targetFov, t);
        }

        // 同時に少しだけ寄る。差が小さいので切り替えが滑らかに見える
        if (thirdPerson != null)
        {
            float rate = isAiming ? aimDistanceMultiplier : 1f;

            // 上を向くほど腕を縮める。ブームが下がって地面に潜り、
            // AvoidObstacles がカメラをキャラの内部へ引き寄せるのを防ぐ
            float pitch01 = Mathf.InverseLerp(pitchZoomStart, pitchZoomEnd, aimPitch);
            rate *= Mathf.Lerp(1f, pitchDistanceMultiplier, pitch01);

            float targetDistance = defaultCameraDistance * rate;
            thirdPerson.CameraDistance = Mathf.Lerp(thirdPerson.CameraDistance, targetDistance, t);
        }
    }
}
