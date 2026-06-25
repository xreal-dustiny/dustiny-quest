using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DustinyDemoFlow : MonoBehaviour
{
    [Header("XR Reference")]
    public Transform centerEyeAnchor;

    [Header("Objects")]
    public GameObject durryObject;       // DurryRoot 넣기
    public Transform durryVisual;        // Durry Visual 넣기
    public GameObject scanZoneObject;
    public Canvas worldCanvas;

    [Header("UI")]
    public TMP_Text questText;
    public TMP_Text bosongText;

    [Header("Start / Recenter")]
    public bool placeObjectsInFrontOnStart = true;
    public bool recenterOnTrigger = true;

    // true로 켜면 매 프레임 따라와서 HUD처럼 보일 수 있음.
    // 더리가 공간 안에 떠 있는 느낌을 원하면 false 권장.
    public bool keepObjectsInFront = false;

    [Header("Input")]
    public bool bButtonCompletesMission = true;
    public bool triggerAlsoCompletesMission = false;

    [Header("Durry Placement")]
    public float durryDistance = 0.75f;

    // 더리를 더 아래로 내리고 싶으면 더 작은 음수로 바꾸기.
    // 예: -0.45 -> -0.60 -> -0.75
    public float durryHeightOffset = -0.55f;

    // 더리가 뒤를 보면 180, 정면을 보면 0으로 조절.
    public float durryYawOffset = 180f;

    [Header("Durry Size")]
    public float durryRootScale = 1.0f;
    public float durryVisualScale = 0.9f;

    [Header("UI Placement")]
    public float uiDistance = 1.25f;
    public float uiHeightOffset = 0.0f;

    [Header("Text Layout")]
    public bool applyTextLayout = true;
    public float questTextY = 360f;
    public float bosongTextY = -360f;
    public float textWidth = 1500f;
    public float questTextHeight = 180f;
    public float bosongTextHeight = 140f;
    public float questFontSize = 44f;
    public float bosongFontSize = 38f;

    [Header("Visual Cleanup")]
    public bool removeTextShadow = true;
    public bool makeCanvasBackgroundTransparent = true;
    public bool disableUIShadowComponents = true;
    public bool disableDurryShadows = true;

    [Header("Scan Zone Placement")]
    public float scanZoneDistance = 1.7f;
    public float scanZoneHeightOffset = -0.55f;
    public float scanZoneSize = 0.8f;

    [Header("Reward")]
    public int rewardBosongPower = 15;

    private int bosongPower = 0;
    private int step = 0;

    private bool wasTriggerPressed = false;

    private Renderer[] durryRenderers;
    private Renderer scanZoneRenderer;
    private Material[] durryRuntimeMaterials;

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start");

        SetupDurry();
        SetupScanZone();
        SetupWorldCanvas();

        if (placeObjectsInFrontOnStart)
        {
            RecenterObjectsInFrontOfUser();
        }

        UpdateUI(
            "Press A to start mission\nPress Trigger to call Durry",
            "Bosong Power 0"
        );
    }

    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        // 계속 켜두면 고개 방향을 따라 매 프레임 이동함.
        // 기본은 false로 두고, Trigger를 눌렀을 때만 다시 눈앞으로 부르는 방식 권장.
        if (keepObjectsInFront)
        {
            RecenterObjectsInFrontOfUser();
        }

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

        // Quest 오른손 검지 Trigger: 더리를 현재 시야 앞으로 다시 부르기
        if (GetRightTriggerDown())
        {
            OnPressTrigger();
        }

        FaceCanvasToUser();
    }

    private void SetupDurry()
    {
        if (durryObject == null)
        {
            Debug.LogWarning("Durry Object가 연결되지 않았습니다. DurryRoot를 넣어주세요.");
            return;
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
                // URP Lit 계열 머티리얼에서 그림자 수신을 줄이기 위한 보조 처리.
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

    private void SetupScanZone()
    {
        if (scanZoneObject == null)
        {
            Debug.LogWarning("Scan Zone Object가 연결되지 않았습니다.");
            return;
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

    private void RecenterObjectsInFrontOfUser()
    {
        if (centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;
        Vector3 forward = GetFlatForward();

        Vector3 durryPos = headPos + forward * durryDistance + Vector3.up * durryHeightOffset;
        Vector3 uiPos = headPos + forward * uiDistance + Vector3.up * uiHeightOffset;
        Vector3 scanZonePos = headPos + forward * scanZoneDistance + Vector3.up * scanZoneHeightOffset;

        if (durryObject != null)
        {
            durryObject.transform.position = durryPos;
            durryObject.transform.rotation = GetRotationFacingUser(durryPos, durryYawOffset);
            ApplyDurryScale();
        }

        if (worldCanvas != null)
        {
            worldCanvas.transform.position = uiPos;
            FaceCanvasToUser();

            if (applyTextLayout)
            {
                ApplyTextLayout();
            }
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.transform.position = scanZonePos;
            scanZoneObject.transform.rotation = Quaternion.LookRotation(Vector3.up, forward);
            scanZoneObject.transform.localScale = Vector3.one * scanZoneSize;
        }
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

        // 텍스트별 머티리얼 인스턴스를 만들어서 다른 TMP 오브젝트에 영향이 가지 않게 함.
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

            // 버튼/아이콘까지 지우지 않기 위해 배경으로 보이는 이름만 투명 처리.
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

        // 미션 시작 시에도 현재 시야 앞으로 한 번 정렬.
        RecenterObjectsInFrontOfUser();

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
            Debug.Log("ScanZone SetActive(true)");
        }

        UpdateUI(
            "Mission Start!\nClean 3 items around Durry.",
            $"Bosong Power {bosongPower}"
        );
    }

    private void OnPressTrigger()
    {
        Debug.Log("Trigger pressed: recenter Durry in front of user");

        if (recenterOnTrigger)
        {
            RecenterObjectsInFrontOfUser();
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
            $"Mission Complete!\nBosong Power +{rewardBosongPower}",
            $"Bosong Power {bosongPower}"
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
