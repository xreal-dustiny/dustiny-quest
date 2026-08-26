using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DollController : MonoBehaviour
{
    [Header("Clean Textures (L0 = Dirty, L1, L2, L3 = Brightest)")]
    public List<Texture2D> cleanTextures = new List<Texture2D>();

    [Header("Bubble Meshes (1 ~ 23)")]
    public GameObject[] bubbleMeshes;

    [Header("Manager Reference")]
    public MiniGameManager miniGameManager;

    [Header("Left Hand Rotation")]
    public Transform leftHandAnchor;
    public float touchDistanceThreshold = 0.20f;
    public float rotationSensitivity = 3.0f;

    [Header("Doll Visuals")]
    private Renderer dollRenderer;
    private int currentCleanlinessLevel = 1;

    [Header("Interaction Tracking")]
    private bool isTouchingSponge = false;
    private bool isTouchingShower = false;
    private bool isTouchingLeftHand = false;

    private bool isLeftHandHolding = false;
    private Vector3 lastLeftHandPos;
    private Transform centerEyeTransform;

private void Awake()
    {
        dollRenderer = GetComponent<Renderer>();
        if (dollRenderer == null)
        {
            dollRenderer = GetComponentInChildren<Renderer>(true);
        }

        AutoFindBubbleMeshes();
        AutoFindTextures();
    }


    private void Start()
    {
        AutoFindReferences();
        FacePlayerInitially();
        SetCleanlinessVisual(1);
        SetBubbleProgress(0f, true);
    }

    private void Update()
    {
        HandleLeftHandRotation();
        CheckProximityFallback();
    }

    public void AutoFindBubbleMeshes()
    {
        Transform bubbleParent = transform.Find("Bubble");
        if (bubbleParent != null && (bubbleMeshes == null || bubbleMeshes.Length == 0))
        {
            List<GameObject> list = new List<GameObject>();
            for (int i = 1; i <= 23; i++)
            {
                Transform t = bubbleParent.Find("Bubble_mesh" + i);
                if (t != null)
                {
                    list.Add(t.gameObject);
                }
            }
            if (list.Count > 0)
            {
                bubbleMeshes = list.ToArray();
            }
        }
    }

    public void AutoFindTextures()
    {
        if (cleanTextures == null || cleanTextures.Count == 0)
        {
            cleanTextures = new List<Texture2D>();
#if UNITY_EDITOR
            string[] paths = new[]
            {
                "Assets/Dustiny/Mini game/Durry_Doll/Texture L0/Durry_Doll_Durry_Doll_c_BaseMap.1001.png",
                "Assets/Dustiny/Mini game/Durry_Doll/Texture L1/Durry_Doll_Durry_Doll_c_BaseMap.1001L1.png",
                "Assets/Dustiny/Mini game/Texture L2/Durry_Doll_Durry_Doll_c_BaseMap.1001.png",
                "Assets/Dustiny/Mini game/Durry_Doll/Texture L3/Durry_Doll_Durry_Doll_c_BaseMap.1001.png"
            };
            foreach (var p in paths)
            {
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex != null) cleanTextures.Add(tex);
            }
#endif
        }
    }

    private void AutoFindReferences()
    {
        if (miniGameManager == null)
        {
            miniGameManager = FindFirstObjectByType<MiniGameManager>();
        }

        if (leftHandAnchor == null)
        {
            var leftGo = GameObject.Find("LeftHandAnchor");
            if (leftGo != null) leftHandAnchor = leftGo.transform;
        }

        var eyeGo = GameObject.Find("CenterEyeAnchor");
        if (eyeGo != null)
        {
            centerEyeTransform = eyeGo.transform;
        }
        else if (Camera.main != null)
        {
            centerEyeTransform = Camera.main.transform;
        }
    }

    public void FacePlayerInitially()
    {
        if (centerEyeTransform == null) AutoFindReferences();
        if (centerEyeTransform != null)
        {
            Vector3 lookDir = centerEyeTransform.position - transform.position;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                // Align to face player
                transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
            }
        }
    }

    private void HandleLeftHandRotation()
    {
        if (leftHandAnchor == null) return;

        float dist = Vector3.Distance(leftHandAnchor.position, transform.position);
        bool canRotate = isTouchingLeftHand || (dist <= touchDistanceThreshold);

        if (canRotate)
        {
            if (!isLeftHandHolding)
            {
                isLeftHandHolding = true;
                lastLeftHandPos = leftHandAnchor.position;
            }
            else
            {
                Vector3 delta = leftHandAnchor.position - lastLeftHandPos;
                if (delta.sqrMagnitude > 0.00001f)
                {
                    float rotAngle = -delta.x * rotationSensitivity * 360f;
                    transform.Rotate(Vector3.up, rotAngle, Space.World);
                }
                lastLeftHandPos = leftHandAnchor.position;
            }
        }
        else
        {
            isLeftHandHolding = false;
        }
    }

    private void CheckProximityFallback()
    {
        if (miniGameManager == null) return;

        bool activeSponge = false;
        bool activeShower = false;

        if (miniGameManager.SpongeObject != null && miniGameManager.SpongeObject.activeInHierarchy)
        {
            float dist = Vector3.Distance(miniGameManager.SpongeObject.transform.position, transform.position);
            if (dist < 0.28f || isTouchingSponge)
            {
                activeSponge = true;
            }
        }

        if (miniGameManager.ShowerObject != null && miniGameManager.ShowerObject.activeInHierarchy)
        {
            float dist = Vector3.Distance(miniGameManager.ShowerObject.transform.position, transform.position);
            if (dist < 0.35f || isTouchingShower)
            {
                activeShower = true;
            }
        }

        miniGameManager.SetTouchState(activeSponge, activeShower);
    }

    public void SetBubbleProgress(float progress, bool isFoaming)
    {
        if (bubbleMeshes == null || bubbleMeshes.Length == 0)
        {
            AutoFindBubbleMeshes();
        }

        if (bubbleMeshes == null || bubbleMeshes.Length == 0) return;

        int total = bubbleMeshes.Length;
        int activeCount = 0;

        if (isFoaming)
        {
            activeCount = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(progress) * total), 0, total);
            if (progress >= 0.999f) activeCount = total;
        }
        else
        {
            activeCount = Mathf.Clamp(Mathf.CeilToInt((1f - Mathf.Clamp01(progress)) * total), 0, total);
            if (progress >= 0.999f) activeCount = 0;
        }

        for (int i = 0; i < total; i++)
        {
            if (bubbleMeshes[i] != null)
            {
                bubbleMeshes[i].SetActive(i < activeCount);
            }
        }
    }

public void SetCleanlinessVisual(int level)
    {
        currentCleanlinessLevel = Mathf.Clamp(level, 0, 3);
        if (dollRenderer == null)
        {
            dollRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>(true);
        }

        if (dollRenderer == null || cleanTextures == null || cleanTextures.Count == 0)
        {
            return;
        }

        int texIndex = Mathf.Clamp(currentCleanlinessLevel, 0, cleanTextures.Count - 1);
        Texture2D texture = cleanTextures[texIndex];
        if (texture == null)
        {
            return;
        }

        Material material = dollRenderer.material;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Sponge") || other.name.Contains("Sponge"))
        {
            isTouchingSponge = true;
        }
        else if (other.CompareTag("Shower") || other.name.Contains("Shower"))
        {
            isTouchingShower = true;
        }
        else if (other.name.Contains("LeftHand") || other.name.Contains("Hand_L") || (leftHandAnchor != null && other.transform.IsChildOf(leftHandAnchor)))
        {
            isTouchingLeftHand = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Sponge") || other.name.Contains("Sponge"))
        {
            isTouchingSponge = false;
        }
        else if (other.CompareTag("Shower") || other.name.Contains("Shower"))
        {
            isTouchingShower = false;
        }
        else if (other.name.Contains("LeftHand") || other.name.Contains("Hand_L") || (leftHandAnchor != null && other.transform.IsChildOf(leftHandAnchor)))
        {
            isTouchingLeftHand = false;
        }
    }
}
