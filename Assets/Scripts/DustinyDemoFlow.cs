using UnityEngine;
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

    [Header("Debug / Test")]
    public bool showScanZoneOnStart = true;
    public bool keepObjectsInFront = false;

    [Header("Placement")]
    public float durryDistance = 1.5f;
    public float durryHeightOffset = -0.15f;

    [Header("Durry Size")]
    public float durryRootScale = 1.0f;
    public float durryVisualScale = 2.0f;

    [Header("UI Placement")]
    public float uiDistance = 1.4f;
    public float uiHeightOffset = 0.35f;

    [Header("Scan Zone Placement")]
    public float scanZoneDistance = 1.5f;
    public float scanZoneHeightOffset = -0.35f;
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

        PlaceObjectsInFrontOfUser();

        UpdateUI(
            "Press A to start mission",
            "Bosong Power 0"
        );
    }

    private void Update()
    {
        if (centerEyeAnchor == null)
        {
            return;
        }

        if (keepObjectsInFront)
        {
            PlaceObjectsInFrontOfUser();
        }

        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            OnPressA();
        }

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

        // Durry Visual이 Inspector에서 연결되지 않았을 때 자동으로 찾기
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

        // 루트는 위치 기준. 스케일은 기본 1 권장.
        durryObject.transform.localScale = Vector3.one * durryRootScale;

        // 실제 캐릭터 크기는 Visual에서 조절.
        if (durryVisual != null)
        {
            durryVisual.localScale = Vector3.one * durryVisualScale;
        }

        // 더리는 여러 Sphere로 이루어져 있으므로 자식 Renderer를 전부 가져옴.
        durryRenderers = durryObject.GetComponentsInChildren<Renderer>(true);

        if (durryRenderers == null || durryRenderers.Length == 0)
        {
            Debug.LogWarning("Durry Renderer를 찾지 못했습니다.");
            return;
        }

        // 런타임에서 색을 바꾸기 위해 Material 인스턴스 생성.
        durryRuntimeMaterials = new Material[durryRenderers.Length];

        for (int i = 0; i < durryRenderers.Length; i++)
        {
            Renderer renderer = durryRenderers[i];

            if (renderer == null) continue;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material runtimeMat = renderer.material;
            durryRuntimeMaterials[i] = runtimeMat;
        }

        Debug.Log("Durry Renderer Count: " + durryRenderers.Length);
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

        scanZoneObject.SetActive(showScanZoneOnStart);
        Debug.Log("ScanZone initial active: " + showScanZoneOnStart);
    }

    private void SetupWorldCanvas()
    {
        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("World Canvas가 연결되지 않았습니다.");
        }
    }

    private void PlaceObjectsInFrontOfUser()
    {
        if (centerEyeAnchor == null) return;

        Vector3 headPos = centerEyeAnchor.position;

        Vector3 forward = centerEyeAnchor.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();

        if (durryObject != null)
        {
            durryObject.transform.position =
                headPos + forward * durryDistance + Vector3.up * durryHeightOffset;

            durryObject.transform.rotation =
                Quaternion.LookRotation(forward, Vector3.up);

            // 중요:
            // 여기서 0.25로 줄이면 안 됨.
            durryObject.transform.localScale = Vector3.one * durryRootScale;

            if (durryVisual != null)
            {
                durryVisual.localScale = Vector3.one * durryVisualScale;
            }
        }

        if (worldCanvas != null)
        {
            worldCanvas.transform.position =
                headPos + forward * uiDistance + Vector3.up * uiHeightOffset;

            FaceCanvasToUser();
        }

        if (scanZoneObject != null)
        {
            scanZoneObject.transform.position =
                headPos + forward * scanZoneDistance + Vector3.up * scanZoneHeightOffset;

            scanZoneObject.transform.rotation =
                Quaternion.LookRotation(Vector3.up, forward);

            scanZoneObject.transform.localScale =
                Vector3.one * scanZoneSize;
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

        // ScanZone은 임시 표시용이라 투명 사용.
        // 더리 몸체에는 Transparent 사용하지 않는 것을 추천.
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

        PlaceObjectsInFrontOfUser();

        if (scanZoneObject != null)
        {
            scanZoneObject.SetActive(true);
            Debug.Log("ScanZone SetActive(true)");
        }

        UpdateUI(
            "Mission Start!\nClean 3 items around Durry.\nPress Trigger when done.",
            $"Bosong Power {bosongPower}"
        );
    }

    private void OnPressTrigger()
    {
        Debug.Log("Trigger pressed");

        if (step != 1)
        {
            Debug.Log("Trigger ignored. Mission has not started yet.");
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
            $"Mission Complete!\nBosong Power +{rewardBosongPower}\nPress A for next mission.",
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
    }

    private void FaceCanvasToUser()
    {
        if (worldCanvas == null || centerEyeAnchor == null) return;

        Vector3 direction = worldCanvas.transform.position - centerEyeAnchor.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            worldCanvas.transform.rotation = Quaternion.LookRotation(direction);
        }
    }
}