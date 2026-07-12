using UnityEngine;
using Meta.XR;

public class PassthroughDetectionCanvas : MonoBehaviour
{
    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;
    public RectTransform canvasRect;

    [Header("[ 위치 보정 ]")]
    public float verticalOffset = 0.2f;

    [Header("[ 표시 설정 ]")]
    [Min(0.1f)]
    public float canvasDistance = 1f;

    [Min(1)]
    public int referenceWidth = 640;

    [Min(1)]
    public int referenceHeight = 640;

    private Canvas worldCanvas;

    private void Awake()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess =
                FindFirstObjectByType<PassthroughCameraAccess>();
        }

        if (canvasRect == null)
        {
            canvasRect =
                GetComponent<RectTransform>();
        }

        worldCanvas =
            GetComponent<Canvas>();
    }

    private void Start()
    {
        if (worldCanvas != null)
        {
            worldCanvas.renderMode =
                RenderMode.WorldSpace;

            worldCanvas.worldCamera =
                Camera.main;

            worldCanvas.sortingOrder = 100;
        }

        if (canvasRect != null)
        {
            canvasRect.sizeDelta =
                new Vector2(
                    referenceWidth,
                    referenceHeight
                );
        }
    }

    public void AlignToCurrentCamera()
    {
        if (passthroughCameraAccess == null ||
            !passthroughCameraAccess.isActiveAndEnabled ||
            canvasRect == null)
        {
            return;
        }

        Pose cameraPose =
            passthroughCameraAccess.GetCameraPose();

        transform.position =
            cameraPose.position +
            cameraPose.rotation *
            Vector3.forward *
            canvasDistance;

        transform.rotation =
            cameraPose.rotation;

        Ray leftRay =
            passthroughCameraAccess.ViewportPointToRay(
                new Vector2(0f, 0.5f)
            );

        Ray rightRay =
            passthroughCameraAccess.ViewportPointToRay(
                new Vector2(1f, 0.5f)
            );

        Ray bottomRay =
            passthroughCameraAccess.ViewportPointToRay(
                new Vector2(0.5f, 0f)
            );

        Ray topRay =
            passthroughCameraAccess.ViewportPointToRay(
                new Vector2(0.5f, 1f)
            );

        float horizontalFov =
            Vector3.Angle(
                leftRay.direction,
                rightRay.direction
            );

        float verticalFov =
            Vector3.Angle(
                bottomRay.direction,
                topRay.direction
            );

        float worldWidth =
            2f *
            canvasDistance *
            Mathf.Tan(
                horizontalFov *
                0.5f *
                Mathf.Deg2Rad
            );

        float worldHeight =
            2f *
            canvasDistance *
            Mathf.Tan(
                verticalFov *
                0.5f *
                Mathf.Deg2Rad
            );

        transform.localScale =
            new Vector3(
                worldWidth / referenceWidth,
                worldHeight / referenceHeight,
                1f
            );
    }
}