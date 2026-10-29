using UnityEngine;

/// <summary>Titleのカメラを、画面中央の16:9の領域に合わせる。</summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class TitleCameraViewport : MonoBehaviour
{
    private const float TargetAspect = 16f / 9f;

    private Camera targetCamera;
    private Rect originalRect;

    private void OnEnable()
    {
        this.targetCamera = GetComponent<Camera>();
        this.originalRect = this.targetCamera.rect;
        this.UpdateViewport();
    }

    private void LateUpdate()
    {
        this.UpdateViewport();
    }

    private void UpdateViewport()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
            return;

        float screenAspect = (float)Screen.width / Screen.height;
        Rect viewport;

        if (screenAspect < TargetAspect)
        {
            float height = screenAspect / TargetAspect;
            viewport = new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }
        else
        {
            float width = TargetAspect / screenAspect;
            viewport = new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        if (this.targetCamera.rect != viewport)
            this.targetCamera.rect = viewport;
    }

    private void OnDisable()
    {
        if (this.targetCamera != null)
            this.targetCamera.rect = this.originalRect;
    }
}
