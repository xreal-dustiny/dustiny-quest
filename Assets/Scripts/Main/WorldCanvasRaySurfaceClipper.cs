// VERSION: 1_WORLD_CANVAS_RAY_CLIP_2026-03-21
// WorldCanvas RayInteractable이 쓰는 PlaneSurface는 기본적으로 무한 평면입니다.
// Shop/MyPage에서 빈 공간(더리 쪽)으로도 레이저가 평면에 붙는 문제를
// ClippedPlaneSurface + BoundsClipper로 페이지 Rect 안으로만 제한합니다.

using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class WorldCanvasRaySurfaceClipper : MonoBehaviour
{
    [Tooltip("보통 WorldCanvas입니다. 비우면 이 오브젝트에서 찾습니다.")]
    public Canvas targetCanvas;

    [Tooltip("Shop/MyPage 클립 기준. 비우면 BigNoteRoot를 자동 탐색합니다.")]
    public RectTransform primaryPageBounds;

    [Tooltip("Note 전용 루트. 열려 있으면 이 Rect로 클립합니다.")]
    public RectTransform notePageBounds;

    [Tooltip("Menu 전용 루트. 열려 있으면 이 Rect로 클립합니다.")]
    public RectTransform menuPageBounds;

    [Tooltip("사이드 태그 등을 위해 Rect 바깥으로 살짝 넓힙니다(캔버스 로컬 단위).")]
    public Vector2 boundsPadding = new Vector2(120f, 40f);

    [Tooltip("클립 박스 깊이(캔버스 로컬 단위).")]
    [Min(1f)] public float boundsDepth = 40f;

    [Tooltip("페이지가 모두 닫혀 있을 때 레이 표면을 거의 꺼서 빈 공간에 레이저가 붙지 않게 합니다.")]
    public bool shrinkWhenNoPageOpen = true;

    private PlaneSurface planeSurface;
    private ClippedPlaneSurface clippedPlaneSurface;
    private BoundsClipper boundsClipper;
    private RayInteractable rayInteractable;
    private RectTransform clipperRect;
    private bool wired;

    private void Awake()
    {
        EnsureWired();
    }

    private void OnEnable()
    {
        EnsureWired();
        SyncClipBounds();
    }

    private void LateUpdate()
    {
        if (!wired)
        {
            EnsureWired();
        }

        SyncClipBounds();
    }

    private void EnsureWired()
    {
        if (wired)
        {
            return;
        }

        if (targetCanvas == null)
        {
            targetCanvas = GetComponent<Canvas>();
        }

        if (targetCanvas == null)
        {
            return;
        }

        ResolveBoundsSources();

        planeSurface = targetCanvas.GetComponent<PlaneSurface>();
        rayInteractable = targetCanvas.GetComponent<RayInteractable>();
        if (planeSurface == null || rayInteractable == null)
        {
            Debug.LogWarning(
                "[WorldCanvasRaySurfaceClipper] PlaneSurface 또는 RayInteractable이 없어 클립을 적용하지 않습니다.",
                this
            );
            return;
        }

        Transform clipRoot = targetCanvas.transform.Find("CanvasRayClipSurface");
        GameObject clipObject = clipRoot != null
            ? clipRoot.gameObject
            : new GameObject("CanvasRayClipSurface", typeof(RectTransform));

        if (clipRoot == null)
        {
            clipObject.transform.SetParent(targetCanvas.transform, false);
            clipObject.layer = targetCanvas.gameObject.layer;
        }

        clipperRect = clipObject.GetComponent<RectTransform>();
        clipperRect.anchorMin = new Vector2(0.5f, 0.5f);
        clipperRect.anchorMax = new Vector2(0.5f, 0.5f);
        clipperRect.pivot = new Vector2(0.5f, 0.5f);
        clipperRect.localScale = Vector3.one;
        clipperRect.localRotation = Quaternion.identity;

        boundsClipper = clipObject.GetComponent<BoundsClipper>();
        if (boundsClipper == null)
        {
            boundsClipper = clipObject.AddComponent<BoundsClipper>();
        }

        clippedPlaneSurface = clipObject.GetComponent<ClippedPlaneSurface>();
        if (clippedPlaneSurface == null)
        {
            clippedPlaneSurface = clipObject.AddComponent<ClippedPlaneSurface>();
        }

        clippedPlaneSurface.InjectAllClippedPlaneSurface(
            planeSurface,
            new List<IBoundsClipper> { boundsClipper }
        );

        rayInteractable.InjectSurface(clippedPlaneSurface);

        // Size/Position는 public property로 바로 설정합니다.
        wired = true;
        Debug.Log("[WorldCanvasRaySurfaceClipper] WorldCanvas 레이 표면을 페이지 Rect로 클립했습니다.", this);
    }

    private void ResolveBoundsSources()
    {
        Transform canvasTransform = targetCanvas != null ? targetCanvas.transform : transform;

        if (primaryPageBounds == null)
        {
            Transform found = FindChildExact(canvasTransform, "BigNoteRoot");
            if (found != null)
            {
                primaryPageBounds = found as RectTransform ?? found.GetComponent<RectTransform>();
            }
        }

        if (notePageBounds == null)
        {
            Transform found = FindChildExact(canvasTransform, "NotePageRoot");
            if (found != null)
            {
                notePageBounds = found as RectTransform ?? found.GetComponent<RectTransform>();
            }
        }

        if (menuPageBounds == null)
        {
            Transform found = FindChildExact(canvasTransform, "MenuPageRoot");
            if (found != null)
            {
                menuPageBounds = found as RectTransform ?? found.GetComponent<RectTransform>();
            }
        }
    }

    private void SyncClipBounds()
    {
        if (!wired || boundsClipper == null || clipperRect == null)
        {
            return;
        }

        RectTransform source = ResolveActiveBoundsSource();
        if (source == null)
        {
            if (!shrinkWhenNoPageOpen)
            {
                return;
            }

            clipperRect.anchoredPosition = Vector2.zero;
            clipperRect.sizeDelta = Vector2.one;
            boundsClipper.Position = Vector3.zero;
            boundsClipper.Size = new Vector3(1f, 1f, 1f);
            return;
        }

        // clipper는 WorldCanvas 자식이므로, 활성 페이지 Rect를 캔버스 로컬로 맞춥니다.
        Vector3 worldCenter = source.TransformPoint(source.rect.center);
        Vector3 localCenter = targetCanvas.transform.InverseTransformPoint(worldCenter);
        Vector2 localSize = source.rect.size;
        Vector3 lossy = source.lossyScale;
        Vector3 canvasLossy = targetCanvas.transform.lossyScale;
        float sx = Mathf.Abs(canvasLossy.x) > 0.00001f ? Mathf.Abs(lossy.x / canvasLossy.x) : 1f;
        float sy = Mathf.Abs(canvasLossy.y) > 0.00001f ? Mathf.Abs(lossy.y / canvasLossy.y) : 1f;

        Vector2 paddedSize = new Vector2(
            localSize.x * sx + boundsPadding.x * 2f,
            localSize.y * sy + boundsPadding.y * 2f
        );

        clipperRect.anchoredPosition = new Vector2(localCenter.x, localCenter.y);
        clipperRect.localPosition = new Vector3(localCenter.x, localCenter.y, localCenter.z);
        clipperRect.sizeDelta = paddedSize;

        boundsClipper.Position = Vector3.zero;
        boundsClipper.Size = new Vector3(paddedSize.x, paddedSize.y, boundsDepth);
    }

    private RectTransform ResolveActiveBoundsSource()
    {
        if (notePageBounds != null && notePageBounds.gameObject.activeInHierarchy)
        {
            return notePageBounds;
        }

        if (menuPageBounds != null && menuPageBounds.gameObject.activeInHierarchy)
        {
            return menuPageBounds;
        }

        if (primaryPageBounds != null && primaryPageBounds.gameObject.activeInHierarchy)
        {
            return primaryPageBounds;
        }

        return null;
    }

    private static Transform FindChildExact(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrWhiteSpace(exactName))
        {
            return null;
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            if (child != null && child.name == exactName)
            {
                return child;
            }
        }

        return null;
    }
}
