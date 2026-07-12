using System.Collections.Generic;

using Meta.XR;
using UnityEngine;

/// <summary>
/// YOLO로 확정된 물체의 바운딩박스 중심 좌표를
/// 패스스루 카메라의 3D Ray로 변환하고 마커를 표시
/// </summary>
public class DetectionMarkerManager : MonoBehaviour
{
    private const float ModelInputWidth = 640f;
    private const float ModelInputHeight = 640f;

    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;

    [Header("[ 마커 설정 ]")]
    [Tooltip("비워두면 기본 Sphere를 생성합니다.")]
    public GameObject markerPrefab;

    [Min(0.1f)]
    public float markerDistance = 1.5f;

    [Min(0.005f)]
    public float markerScale = 0.04f;

    [Header("[ 좌표 보정 ]")]
    public bool flipX = false;
    public bool flipY = true;

    [Header("[ 표시 설정 ]")]
    public bool clearPreviousMarkers = true;

    private readonly List<GameObject> spawnedMarkers =
        new List<GameObject>();

    private void Awake()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess =
                FindFirstObjectByType<PassthroughCameraAccess>();
        }
    }

    /// <summary>
    /// 확정된 물체들의 중심 위치에 마커를 생성
    /// </summary>
    public void ShowMarkers(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        if (confirmedObjects == null)
        {
            return;
        }

        if (clearPreviousMarkers)
        {
            ClearMarkers();
        }

        foreach (
            ConfirmedObjectInfo detectedObject
            in confirmedObjects
        )
        {
            CreateMarker(
                detectedObject
            );
        }

        Debug.Log(
            $"[Detection Marker] " +
            $"{spawnedMarkers.Count}개의 마커를 표시했습니다."
        );
    }

    private void CreateMarker(
        ConfirmedObjectInfo detectedObject
    )
    {
        Vector2 boxCenter =
            detectedObject.lastRect.center;

        // YOLO의 640×640 좌표를
        // 카메라 Viewport의 0~1 좌표로 변환
        float viewportX =
            Mathf.Clamp01(
                boxCenter.x /
                ModelInputWidth
            );

        float viewportY =
            Mathf.Clamp01(
                boxCenter.y /
                ModelInputHeight
            );

        if (flipX)
        {
            viewportX =
                1f - viewportX;
        }

        if (flipY)
        {
            viewportY =
                1f - viewportY;
        }

        Ray cameraRay =
            passthroughCameraAccess.ViewportPointToRay(
                new Vector2(
                    viewportX,
                    viewportY
                )
            );

        // 현재는 깊이를 알 수 없으므로
        // Ray의 일정 거리 앞에 임시 배치
        Vector3 markerPosition =
            cameraRay.GetPoint(
                markerDistance
            );

        GameObject marker;

        if (markerPrefab != null)
        {
            marker =
                Instantiate(
                    markerPrefab,
                    markerPosition,
                    Quaternion.identity
                );
        }
        else
        {
            marker =
                GameObject.CreatePrimitive(
                    PrimitiveType.Sphere
                );

            marker.transform.position =
                markerPosition;

            Collider markerCollider =
                marker.GetComponent<Collider>();

            if (markerCollider != null)
            {
                Destroy(
                    markerCollider
                );
            }
        }

        marker.name =
            $"Marker_" +
            $"{detectedObject.objectId}_" +
            $"{detectedObject.className}";

        marker.transform.localScale =
            Vector3.one *
            markerScale;

        spawnedMarkers.Add(
            marker
        );
    }

    /// <summary>
    /// 기존에 생성된 마커를 모두 제거한다.
    /// </summary>
    public void ClearMarkers()
    {
        foreach (
            GameObject marker
            in spawnedMarkers
        )
        {
            if (marker != null)
            {
                Destroy(
                    marker
                );
            }
        }

        spawnedMarkers.Clear();
    }
}