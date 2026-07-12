using System.Collections.Generic;

using Meta.XR;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 확정된 탐지 물체의 중심 방향에 반짝이 마커를 표시한다.
///
/// 담당 기능:
/// 1. YOLO 바운딩박스 중심 좌표를 카메라 Ray로 변환
/// 2. 탐지 물체 방향에 ✦ 반짝이 마커 생성
/// 3. 마커가 사용자를 바라보도록 회전
/// 4. 마커가 부드럽게 커졌다 작아지는 애니메이션
/// 5. 이전 스캔의 마커 제거
/// </summary>
public class DetectionMarkerManager : MonoBehaviour
{
    private const float ModelInputWidth = 640f;
    private const float ModelInputHeight = 640f;

    [Header("[ 필수 연결 ]")]
    public PassthroughCameraAccess passthroughCameraAccess;

    [Header("[ 마커 위치 및 크기 ]")]
    [Min(0.1f)]
    public float markerDistance = 1f;

    [Min(0.005f)]
    public float markerScale = 0.12f;

    [Header("[ 반짝이 색상 ]")]
    public Color outerColor =
        new Color(0.65f, 1f, 0.15f, 1f);

    public Color innerColor =
        new Color(1f, 0.9f, 0.2f, 1f);

    [Header("[ 반짝이 모양 ]")]
    [Range(0.05f, 0.4f)]
    public float innerRadius = 0.14f;

    [Range(0.1f, 1f)]
    public float innerSparkleScale = 0.48f;

    [Header("[ 애니메이션 ]")]
    [Range(0f, 0.5f)]
    public float pulseAmount = 0.15f;

    [Min(0.1f)]
    public float pulseSpeed = 3f;

    [Header("[ 좌표 보정 ]")]
    public bool flipX = false;
    public bool flipY = true;

    [Header("[ 표시 설정 ]")]
    public bool clearPreviousMarkers = true;

    private Transform lookTarget;

    private Mesh sparkleMesh;
    private Material outerMaterial;
    private Material innerMaterial;

    private readonly List<MarkerInfo> spawnedMarkers =
        new List<MarkerInfo>();

    private void Awake()
    {
        if (passthroughCameraAccess == null)
        {
            passthroughCameraAccess =
                FindFirstObjectByType<PassthroughCameraAccess>();
        }

        FindLookTarget();

        sparkleMesh =
            CreateSparkleMesh();

        outerMaterial =
            CreateUnlitMaterial(
                outerColor
            );

        innerMaterial =
            CreateUnlitMaterial(
                innerColor
            );
    }

    private void LateUpdate()
    {
        UpdateMarkers();
    }

    /// <summary>
    /// 확정된 물체들의 중심 방향에 마커를 생성한다.
    /// </summary>
    public void ShowMarkers(
        List<ConfirmedObjectInfo> confirmedObjects
    )
    {
        if (passthroughCameraAccess == null)
        {
            Debug.LogError(
                "[Detection Marker] " +
                "PassthroughCameraAccess가 없습니다."
            );

            return;
        }

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
            $"{spawnedMarkers.Count}개의 반짝이 마커를 표시했습니다."
        );
    }

    private void CreateMarker(
        ConfirmedObjectInfo detectedObject
    )
    {
        Vector2 boxCenter =
            detectedObject.lastRect.center;

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

        Vector3 markerPosition =
            cameraRay.GetPoint(
                markerDistance
            );

        GameObject marker =
            new GameObject(
                $"Marker_" +
                $"{detectedObject.objectId}_" +
                $"{detectedObject.className}"
            );

        marker.transform.position =
            markerPosition;

        marker.transform.localScale =
            Vector3.one *
            markerScale;

        CreateSparkleChild(
            marker.transform,
            "OuterSparkle",
            sparkleMesh,
            outerMaterial,
            1f,
            0f
        );

        CreateSparkleChild(
            marker.transform,
            "InnerSparkle",
            sparkleMesh,
            innerMaterial,
            innerSparkleScale,
            0.002f
        );

        MarkerInfo markerInfo =
            new MarkerInfo
            {
                markerObject = marker,
                baseScale =
                    Vector3.one *
                    markerScale,
                animationOffset =
                    Random.Range(
                        0f,
                        Mathf.PI * 2f
                    )
            };

        spawnedMarkers.Add(
            markerInfo
        );

        LookAtUser(
            marker.transform
        );
    }

    private void CreateSparkleChild(
        Transform parent,
        string objectName,
        Mesh mesh,
        Material material,
        float objectScale,
        float zPosition
    )
    {
        GameObject sparkle =
            new GameObject(
                objectName
            );

        sparkle.transform.SetParent(
            parent,
            false
        );

        sparkle.transform.localPosition =
            new Vector3(
                0f,
                0f,
                zPosition
            );

        sparkle.transform.localRotation =
            Quaternion.identity;

        sparkle.transform.localScale =
            Vector3.one *
            objectScale;

        MeshFilter meshFilter =
            sparkle.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer =
            sparkle.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh =
            mesh;

        meshRenderer.sharedMaterial =
            material;
    }

    private void UpdateMarkers()
    {
        for (
            int i = spawnedMarkers.Count - 1;
            i >= 0;
            i--
        )
        {
            MarkerInfo markerInfo =
                spawnedMarkers[i];

            if (markerInfo.markerObject == null)
            {
                spawnedMarkers.RemoveAt(i);
                continue;
            }

            Transform markerTransform =
                markerInfo.markerObject.transform;

            LookAtUser(
                markerTransform
            );

            float pulse =
                1f +
                Mathf.Sin(
                    Time.time *
                    pulseSpeed +
                    markerInfo.animationOffset
                ) *
                pulseAmount;

            markerTransform.localScale =
                markerInfo.baseScale *
                pulse;
        }
    }

    private void FindLookTarget()
    {
        Camera targetCamera =
            Camera.main;

        if (targetCamera == null)
        {
            targetCamera =
                FindFirstObjectByType<Camera>();
        }

        if (targetCamera != null)
        {
            lookTarget =
                targetCamera.transform;
        }
    }

    private void LookAtUser(
        Transform markerTransform
    )
    {
        if (lookTarget == null)
        {
            FindLookTarget();

            if (lookTarget == null)
            {
                return;
            }
        }

        Vector3 direction =
            lookTarget.position -
            markerTransform.position;

        if (direction.sqrMagnitude <
            0.0001f)
        {
            return;
        }

        markerTransform.rotation =
            Quaternion.LookRotation(
                direction.normalized,
                Vector3.up
            );
    }

    /// <summary>
    /// 4방향 ✦ 모양 Mesh를 생성한다.
    /// </summary>
    private Mesh CreateSparkleMesh()
    {
        const int pointCount = 8;

        Vector3[] vertices =
            new Vector3[
                pointCount + 1
            ];

        int[] triangles =
            new int[
                pointCount * 3
            ];

        vertices[0] =
            Vector3.zero;

        for (
            int i = 0;
            i < pointCount;
            i++
        )
        {
            float angle =
                Mathf.Deg2Rad *
                (
                    90f +
                    i * 45f
                );

            float radius =
                i % 2 == 0
                    ? 0.5f
                    : innerRadius;

            vertices[i + 1] =
                new Vector3(
                    Mathf.Cos(angle) *
                    radius,

                    Mathf.Sin(angle) *
                    radius,

                    0f
                );

            int triangleIndex =
                i * 3;

            triangles[
                triangleIndex
            ] = 0;

            triangles[
                triangleIndex + 1
            ] = i + 1;

            triangles[
                triangleIndex + 2
            ] =
                i == pointCount - 1
                    ? 1
                    : i + 2;
        }

        Mesh mesh =
            new Mesh
            {
                name =
                    "DetectionSparkleMesh",

                vertices =
                    vertices,

                triangles =
                    triangles
            };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private Material CreateUnlitMaterial(
        Color color
    )
    {
        Shader shader =
            Shader.Find(
                "Universal Render Pipeline/Unlit"
            );

        if (shader == null)
        {
            shader =
                Shader.Find(
                    "Unlit/Color"
                );
        }

        if (shader == null)
        {
            shader =
                Shader.Find(
                    "Sprites/Default"
                );
        }

        Material material =
            new Material(
                shader
            );

        if (material.HasProperty(
            "_BaseColor"))
        {
            material.SetColor(
                "_BaseColor",
                color
            );
        }

        if (material.HasProperty(
            "_Color"))
        {
            material.SetColor(
                "_Color",
                color
            );
        }

        if (material.HasProperty(
            "_Cull"))
        {
            material.SetFloat(
                "_Cull",
                (float)CullMode.Off
            );
        }

        return material;
    }

    /// <summary>
    /// 생성된 모든 마커를 제거한다.
    /// </summary>
    public void ClearMarkers()
    {
        foreach (
            MarkerInfo markerInfo
            in spawnedMarkers
        )
        {
            if (markerInfo.markerObject != null)
            {
                Destroy(
                    markerInfo.markerObject
                );
            }
        }

        spawnedMarkers.Clear();
    }

    private void OnDestroy()
    {
        ClearMarkers();

        if (sparkleMesh != null)
        {
            Destroy(
                sparkleMesh
            );
        }

        if (outerMaterial != null)
        {
            Destroy(
                outerMaterial
            );
        }

        if (innerMaterial != null)
        {
            Destroy(
                innerMaterial
            );
        }
    }

    private class MarkerInfo
    {
        public GameObject markerObject;
        public Vector3 baseScale;
        public float animationOffset;
    }
}