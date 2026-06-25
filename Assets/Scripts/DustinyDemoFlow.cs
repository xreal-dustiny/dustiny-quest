using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DustinyDemoFlow : MonoBehaviour
{
    [Header("XR Reference")]
    public Transform centerEyeAnchor;

    [Header("Objects")]
    public GameObject durryObject;       // DurryRoot 넣기. 카메라 밑에 두지 말고 월드 오브젝트로 둔다.
    public Transform durryVisual;        // Durry Visual 넣기
    public GameObject scanZoneObject;
    public Canvas worldCanvas;

    [Header("View Locked UI")]
    public Transform uiRoot;             // CenterEyeAnchor 아래의 UIRoot 넣기. 비워두면 WorldCanvas의 부모를 자동 사용.
    public bool lockUIToUserView = true;
    public bool forceUIRootUnderCenterEye = true;

    // UIRoot가 CenterEyeAnchor의 자식일 때의 로컬 위치.
    // z: 눈앞 거리, y: 시야 안에서 위/아래 위치. y를 더 음수로 하면 더 아래로 내려감.
    public Vector3 uiRootLocalPosition = new Vector3(0f, -0.45f, 1.15f);
    public Vector3 uiRootLocalEuler = Vector3.zero;
    public float uiRootLocalScale = 1f;

    [Header("UI")]
    public TMP_Text questText;
    public TMP_Text bosongText;

    [Header("Start / Summon")]
    public bool summonDurryOnStart = false;
    public bool summonDurryOnMissionStart = false;
    public bool recenterOnTrigger = true;
    public bool detachDurryAndScanZoneFromParent = true;

    [Header("Input")]
    public bool bButtonCompletesMission = true;
    public bool triggerAlsoCompletesMission = false;

    [Header("Durry Fixed World Start")]
    public bool useCurrentSceneDurryPositionOnStart = true;
    public Vector3 durryStartWorldPosition = new Vector3(0f, -0.35f, 1.2f);
    public Vector3 durryStartWorldEuler = new Vector3(0f, 180f, 0f);
    public bool faceDurryToUserOnStart = true;

    [Header("Durry Summon Placement")]
    public float durryDistance = 0.85f;
    public float durrySideOffset = 0f;
    public float durryHeightOffset = -0.25f;

    // 더리가 뒤를 보면 180, 정면을 보면 0으로 조절.
    public float durryYawOffset = 180f;

    [Header("Durry Size")]
    public float durryRootScale = 1.0f;
    public float durryVisualScale = 0.9f;

    [Header("Text Layout")]
    public bool applyTextLayout = true;
    public float questTextY = 120f;
    public float bosongTextY = -110f;
    public float textWidth = 1300f;
    public float questTextHeight = 180f;
    public float bosongTextHeight = 120f;
    public float questFontSize = 42f;
    public float bosongFontSize = 34f;

    [Header("Visual Cleanup")]
    public bool removeTextShadow = true;
    public bool makeCanvasBackgroundTransparent = true;
    public bool disableUIShadowComponents = true;
    public bool disableDurryShadows = true;

    [Header("Scan Zone Placement")]
    public bool moveScanZoneWhenDurrySummoned = true;
    public float scanZoneDistance = 1.25f;
    public float scanZoneHeightOffset = -0.65f;
    public float scanZoneSize = 0.8f;

    [Header("Reward")]
    public int rewardBosongPower = 15;

    private int bosongPower = 0;
    private int step = 0;

    private bool wasTriggerPressed = false;

    private Renderer[] durryRenderers;
    private Renderer scanZoneRenderer;
    private Material[] durryRuntimeMaterials;
    private Transform resolvedUIRoot;

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start - World Durry / View Locked UI");

        ResolveUIRoot();
        SetupDurry();
        SetupScanZone();
        SetupWorldCanvas();

        PlaceDurryAtStartWorldPosition();

        if (summonDurryOnStart)
        {
            SummonDurryToUser();
        }

        UpdateViewLockedUI(true);

        UpdateUI(
            "A 버튼을 눌러 더리의 숨은 구역을 찾아보자!\nTrigger를 누르면 더리가 내 앞으로 와요.",
            "보송력 0"
        );
    }

    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        // UI만 매 프레임 사용자 시야 아래쪽에 고정한다.
        // 더리와 스캔존은 여기서 따라오지 않는다.
        UpdateViewLockedUI(false);

        // Quest 오른손 A 버튼
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            OnPressA();
        }

        // Quest 오른손 B 버튼: 데모용 미션 완료
        if (bButtonCompletesMission && OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            CompleteMission();
        }

        // Quest 오른손 검지 Trigger: 더리를 현재 시야 앞으로 소환한다.
        if (GetRightTriggerDown())
        {
            OnPressTrigger();
        }

        // UI가 카메라 자식이 아닐 때만 월드 캔버스를 사용자를 향해 돌린다.
        if (!lockUIToUserView)
        {
            FaceCanvasToUser();
        }
    }

    private void ResolveUIRoot()
    {
        if (worldCanvas == null)
        {
            resolvedUIRoot = uiRoot;
            return;
        }

        if (uiRoot != null)
        {
            resolvedUIRoot = uiRoot;
            return;
        }

        // WorldCanvas가 UIRoot 아래에 있으면 UIRoot를 움직이는 것이 안전하다.
        if (worldCanvas.transform.parent != null && worldCanvas.transform.parent != centerEyeAnchor)
        {
            resolvedUIRoot = worldCanvas.transform.parent;
        }
        else
        {
            resolvedUIRoot = worldCanvas.transform;
        }
    }

    private void UpdateViewLockedUI(bool force)
    {
        if (!lockUIToUserView) return;
        if (centerEyeAnchor == null) return;
        if (resolvedUIRoot == null) ResolveUIRoot();
        if (resolvedUIRoot == null) return;

        if (forceUIRootUnderCenterEye && resolvedUIRoot.parent != centerEyeAnchor)
        {
            resolvedUIRoot.SetParent(centerEyeAnchor, false);
        }

        resolvedUIRoot.localPosition = uiRootLocalPosition;
        resolvedUIRoot.localRotation = Quaternion.Euler(uiRootLocalEuler);
        resolvedUIRoot.localScale = Vector3.one * Mathf.Max(0.001f, uiRootLocalScale);
    }

    private void SetupDurry()
    {
        if (durryObject == null)
        {
            Debug.LogWarning("Durry Object가 연결되지 않았습니다. DurryRoot를 넣어주세요.");
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            // 더리는 카메라 자식으로 두면 고개를 돌릴 때 같이 따라오므로 월드 루트로 분리한다.
            durryObject.transform.SetParent(null, true);
        }

        durryObject.SetActive(true);

        if (durryVisual == null)
        {
            Transform foundVisual = durryObject.transform.Find("Durry Visual");

            if (foundVisual == null)
            {
                foundVisual = durryObject.transform.Find("DurryVisual");
            }

            if (foundVisual != null)
            {
                durryVisual = foundVisual;
            }
            else
            {
                Debug.LogWarning("Durry Visual을 찾지 못했습니다. Inspector에서 직접 연결해주세요.");
            }
        }

        ApplyDurryScale();

        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);

        if (durryRenderers == null || durryRenderers.Length == 0)
        {
            Debug.LogWarning("Durry Renderer를 찾지 못했습니다.");
            return;
        }

        durryRuntimeMaterials = new Material[durryRenderers.Length];

        for (int i = 0; i < durryRenderers.Length; i++)
        {
            Renderer renderer = durryRenderers[i];

            if (renderer == null) continue;

            if (disableDurryShadows)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Material runtimeMat = renderer.material;

            if (runtimeMat != null)
            {
                SetFloatIfHas(runtimeMat, "_ReceiveShadows", 0f);
            }

            durryRuntimeMaterials[i] = runtimeMat;
        }

        Debug.Log("Durry Renderer Count: " + durryRenderers.Length);
    }

    private void ApplyDurryScale()
    {
        if (durryObject != null)
        {
            durryObject.transform.localScale = Vector3.one * durryRootScale;
        }

        if (durryVisual != null)
        {
            durryVisual.localScale = Vector3.one * durryVisualScale;
        }
    }

    private void PlaceDurryAtStartWorldPosition()
    {
        if (durryObject == null) return;

        if (!useCurrentSceneDurryPositionOnStart)
        {
            durryObject.transform.position = durryStartWorldPosition;
            durryObject.transform.rotation = Quaternion.Euler(durryStartWorldEuler);
        }
        else if (faceDurryToUserOnStart && centerEyeAnchor != null)
        {
            durryObject.transform.rotation = GetRotationFacingUser(durryObject.transform.position, durryYawOffset);
        }

        ApplyDurryScale();
    }

    private void SetupScanZone()
    {
        if (scanZoneObject == null)
        {
            Debug.LogWarning("Scan Zone Object가 연결되지 않았습니다.");
            return;
        }

        if (detachDurryAndScanZoneFromParent)
        {
            scanZoneObject.transform.SetParent(null, true);
        }

        scanZoneRenderer = scanZoneObject.GetComponent<Renderer>();
        SetupScanZoneMaterial();

        scanZoneObject.SetActive(true);
        Debug.Log("ScanZone initial active: true");
    }

    private void SetupWorldCanvas()
    {
        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
            worldCanvas.renderMode = RenderMode.WorldSpace;

            Camera centerEyeCamera = centerEyeAnchor != null ? centerEyeAnchor.GetComponent<Camera>() : null;
            if (centerEyeCamera == null)
            {
                centerEyeCamera = Camera.main;
            }
            worldCanvas.worldCamera = centerEyeCamera;

            if (applyTextLayout)
            {
                ApplyTextLayout();
            }

            if (removeTextShadow)
            {
                RemoveTMPShadow(questText);
                RemoveTMPShadow(bosongText);
            }

            if (disableUIShadowComponents)
            {
                DisableUIShadowEffects();
            }

            if (makeCanvasBackgroundTransparent)
            {
                MakeCanvasBackgroundTransparent();
            }
        }
        else
        {
            Debug.LogWarning("World Canvas가 연결되지 않았습니다.");
        }
    }

    private void SummonDurryToUser()
    {
        if (centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        Vector3 forward = GetFlatForward();
        Vector3 right = centerEyeAnchor.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        right.Normalize();

        Vector3 durryPos = headPos + forward * durryDistance + right * durrySideOffset + Vector3.up * durryHeightOffset;

        if (durryObject != null)
        {
            durryObject.transform.position = durryPos;
            durryObject.transform.rotation = GetRotationFacingUser(durryPos, durryYawOffset);
            ApplyDurryScale();
        }

        if (moveScanZoneWhenDurrySummoned)
        {
            PlaceScanZoneInFrontOfUser(forward);
        }
    }

    private void PlaceScanZoneInFrontOfUser(Vector3 forward)
    {
        if (scanZoneObject == null || centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        Vector3 scanZonePos = headPos + forward * scanZoneDistance + Vector3.up * scanZoneHeightOffset;

        scanZoneObject.transform.position = scanZonePos;
        scanZoneObject.transform.rotation = Quaternion.LookRotation(Vector3.up, forward);
        scanZoneObject.transform.localScale = Vector3.one * scanZoneSize;
    }

    private Vector3 GetFlatForward()
    {
        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private Quaternion GetRotationFacingUser(Vector3 objectPosition, float yawOffset)
    {
        Vector3 toUser = centerEyeAnchor.position - objectPosition;
        toUser.y = 0f;

        if (toUser.sqrMagnitude < 0.001f)
        {
            toUser = -GetFlatForward();
        }

        Quaternion lookAtUser = Quaternion.LookRotation(toUser.normalized, Vector3.up);
        return lookAtUser * Quaternion.Euler(0f, yawOffset, 0f);
    }

    private void ApplyTextLayout()
    {
        ApplyTextRect(questText, questTextY, questTextHeight, questFontSize);
        ApplyTextRect(bosongText, bosongTextY, bosongTextHeight, bosongFontSize);
    }

    private void ApplyTextRect(TMP_Text text, float y, float height, float fontSize)
    {
        if (text == null) return;

        RectTransform rect = text.GetComponent<RectTransform>();

        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(textWidth, height);
        }

        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;

        if (fontSize > 0f)
        {
            text.fontSize = fontSize;
        }
    }

    private void RemoveTMPShadow(TMP_Text text)
    {
        if (text == null) return;
        if (text.fontMaterial == null) return;

        Material mat = new Material(text.fontMaterial);
        mat.name = text.name + "_NoShadow_Runtime";

        mat.DisableKeyword("UNDERLAY_ON");
        mat.DisableKeyword("UNDERLAY_INNER");

        SetFloatIfHas(mat, "_OutlineWidth", 0f);
        SetFloatIfHas(mat, "_OutlineSoftness", 0f);
        SetFloatIfHas(mat, "_FaceDilate", 0f);
        SetFloatIfHas(mat, "_UnderlayOffsetX", 0f);
        SetFloatIfHas(mat, "_UnderlayOffsetY", 0f);
        SetFloatIfHas(mat, "_UnderlayDilate", 0f);
        SetFloatIfHas(mat, "_UnderlaySoftness", 0f);
        SetColorIfHas(mat, "_UnderlayColor", new Color(0f, 0f, 0f, 0f));
        SetColorIfHas(mat, "_OutlineColor", new Color(0f, 0f, 0f, 0f));

        text.fontMaterial = mat;
    }

    private void DisableUIShadowEffects()
    {
        if (worldCanvas == null) return;

        Shadow[] shadows = worldCanvas.GetComponentsInChildren<Shadow>(true);

        foreach (Shadow shadow in shadows)
        {
            if (shadow != null)
            {
                shadow.enabled = false;
            }
        }
    }

    private void MakeCanvasBackgroundTransparent()
    {
        if (worldCanvas == null) return;

        Image[] images = worldCanvas.GetComponentsInChildren<Image>(true);

        foreach (Image image in images)
        {
            if (image == null) continue;

            string objectName = image.gameObject.name.ToLowerInvariant();

            bool looksLikeBackground =
                objectName.Contains("background") ||
                objectName.Contains("bg") ||
                objectName.Contains("panel") ||
                objectName.Contains("shadow") ||
                objectName.Contains("dim") ||
                objectName.Contains("black");

            if (!looksLikeBackground) continue;

            Color color = image.color;
            color.a = 0f;
            image.color = color;
            image.raycastTarget = false;
        }
    }

    private void SetupScanZoneMaterial()
    {
        if (scanZoneRenderer == null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            Debug.LogWarning("Unlit Shader를 찾지 못했습니다. 기존 Material을 사용합니다.");
            return;
        }

        Material mat = new Material(shader);
        mat.name = "M_ScanZone_SoftBlue_Runtime";

        Color scanColor = new Color(0.45f, 0.85f, 1f, 0.55f);

        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", scanColor);
        }

        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", scanColor);
        }

        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f);
        }

        if (mat.HasProperty("_Blend"))
        {
            mat.SetFloat("_Blend", 0f);
        }

        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (mat.HasProperty("_DstBlend"))
        {
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (mat.HasProperty("_ZWrite"))
        {
            mat.SetFloat("_ZWrite", 0f);
        }

        if (mat.HasProperty("_Cull"))
        {
            mat.SetFloat("_Cull", 0f);
        }

        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        scanZoneRenderer.material = mat;
        scanZoneRenderer.enabled = true;
    }

    private void OnPressA()
    {
        Debug.Log("A button pressed");

        step = 1;

        if (summonDurryOnMissionStart)
        {
            SummonDurryToUser();
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
            Debug.Log("ScanZone SetActive(true)");
        }

        UpdateUI(
            "미션 시작!\n더리 주변의 어질러진 물건 3개를 정리해줘.",
            $"보송력 {bosongPower}"
        );
    }

    private void OnPressTrigger()
    {
        Debug.Log("Trigger pressed: summon Durry in front of user");

        if (recenterOnTrigger)
        {
            SummonDurryToUser();
        }

        if (triggerAlsoCompletesMission)
        {
            CompleteMission();
        }
    }

    private void CompleteMission()
    {
        Debug.Log("CompleteMission called");

        if (step != 1)
        {
            Debug.Log("Mission complete ignored. Mission has not started yet.");
            return;
        }

        step = 2;
        bosongPower += rewardBosongPower;

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(false);
            Debug.Log("ScanZone SetActive(false)");
        }

        RecoverDurry();

        UpdateUI(
            $"미션 완료!\n보송력 +{rewardBosongPower}",
            $"보송력 {bosongPower}"
        );
    }

    private bool GetRightTriggerDown()
    {
        float triggerValue = OVRInput.Get(
            OVRInput.Axis1D.PrimaryIndexTrigger,
            OVRInput.Controller.RTouch
        );

        bool isPressed = triggerValue > 0.8f;
        bool pressedThisFrame = isPressed && !wasTriggerPressed;

        wasTriggerPressed = isPressed;

        return pressedThisFrame;
    }

    private void RecoverDurry()
    {
        if (durryRuntimeMaterials == null || durryRuntimeMaterials.Length == 0)
        {
            return;
        }

        float t = Mathf.Clamp01(bosongPower / 100f);

        Color dustyColor = new Color(0.55f, 0.52f, 0.48f, 1f);
        Color cleanColor = new Color(1f, 0.94f, 0.86f, 1f);

        Color currentColor = Color.Lerp(dustyColor, cleanColor, t);

        foreach (Material mat in durryRuntimeMaterials)
        {
            if (mat == null) continue;

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", currentColor);
            }
            else if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", currentColor);
            }
        }
    }

    private void UpdateUI(string questMessage, string bosongMessage)
    {
        if (questText != null)
        {
            questText.text = questMessage;
        }

        if (bosongText != null)
        {
            bosongText.text = bosongMessage;
        }

        if (applyTextLayout)
        {
            ApplyTextLayout();
        }
    }

    private void FaceCanvasToUser()
    {
        if (worldCanvas == null || centerEyeAnchor == null) return;

        Vector3 direction = worldCanvas.transform.position - centerEyeAnchor.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.001f)
        {
            worldCanvas.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    private void SetFloatIfHas(Material mat, string propertyName, float value)
    {
        if (mat != null && mat.HasProperty(propertyName))
        {
            mat.SetFloat(propertyName, value);
        }
    }

    private void SetColorIfHas(Material mat, string propertyName, Color value)
    {
        if (mat != null && mat.HasProperty(propertyName))
        {
            mat.SetColor(propertyName, value);
        }
    }
}
