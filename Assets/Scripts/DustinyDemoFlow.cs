using UnityEngine;
using TMPro;

public class DustinyDemoFlow : MonoBehaviour
{
    [Header("XR Reference")]
    public Transform centerEyeAnchor;

    [Header("Objects")]
    public GameObject durryObject;
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

    public float uiDistance = 1.4f;
    public float uiHeightOffset = 0.35f;

    public float scanZoneDistance = 1.5f;
    public float scanZoneHeightOffset = -0.35f;
    public float scanZoneSize = 0.8f;

    [Header("Reward")]
    public int rewardBosongPower = 15;

    private int bosongPower = 0;
    private int step = 0;

    private bool wasTriggerPressed = false;

    private Renderer durryRenderer;
    private Renderer scanZoneRenderer;

    private void Start()
    {
        Debug.Log("DustinyDemoFlow Start");

        if (durryObject != null)
        {
            durryObject.SetActive(true);
            durryRenderer = durryObject.GetComponent<Renderer>();
        }
        else
        {
            Debug.LogWarning("Durry Object가 연결되지 않았습니다.");
        }

        if (scanZoneObject != null)
        {
            scanZoneRenderer = scanZoneObject.GetComponent<Renderer>();
            SetupScanZoneMaterial();

            scanZoneObject.SetActive(showScanZoneOnStart);
            Debug.Log("ScanZone initial active: " + showScanZoneOnStart);
        }
        else
        {
            Debug.LogWarning("Scan Zone Object가 연결되지 않았습니다.");
        }

        if (worldCanvas != null)
        {
            worldCanvas.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("World Canvas가 연결되지 않았습니다.");
        }

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

            durryObject.transform.localScale = Vector3.one * 0.25f;
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

        // URP 투명 설정 시도
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // Transparent
        }

        if (mat.HasProperty("_Blend"))
        {
            mat.SetFloat("_Blend", 0f); // Alpha
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

        // 양면 렌더링
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
        if (durryRenderer == null) return;

        float t = Mathf.Clamp01(bosongPower / 100f);

        Color dustyColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        Color cleanColor = Color.white;

        durryRenderer.material.color = Color.Lerp(dustyColor, cleanColor, t);
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