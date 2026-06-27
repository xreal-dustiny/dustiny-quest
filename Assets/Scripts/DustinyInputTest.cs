using UnityEngine;
using TMPro;

public class DustinyInputTest : MonoBehaviour
{
    [Header("References")]
    public Transform centerEyeAnchor;
    public OVRHand leftHand;
    public OVRHand rightHand;
    public Transform leftWristTransform;
    public TMP_Text debugText;

    [Header("Wrist View Zone")]
    public float wristViewMinX = -0.75f;
    public float wristViewMaxX = 0.35f;
    public float wristViewMinY = -0.75f;
    public float wristViewMaxY = 0.25f;
    public float wristViewMinZ = 0.15f;
    public float wristViewMaxZ = 1.15f;

    private void Update()
    {
        bool leftTracked = IsHandTracked(leftHand);
        bool rightTracked = IsHandTracked(rightHand);

        bool rightIndex = rightTracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        bool rightMiddle = rightTracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Middle);
        bool rightRing = rightTracked && rightHand.GetFingerIsPinching(OVRHand.HandFinger.Ring);

        Vector3 wristLocal = Vector3.zero;
        bool wristInZone = false;

        if (leftTracked && centerEyeAnchor != null)
        {
            Transform wrist = leftWristTransform != null ? leftWristTransform : leftHand.transform;
            wristLocal = centerEyeAnchor.InverseTransformPoint(wrist.position);

            wristInZone =
                wristLocal.x >= wristViewMinX &&
                wristLocal.x <= wristViewMaxX &&
                wristLocal.y >= wristViewMinY &&
                wristLocal.y <= wristViewMaxY &&
                wristLocal.z >= wristViewMinZ &&
                wristLocal.z <= wristViewMaxZ;
        }

        string message =
            $"Left tracked: {leftTracked}\n" +
            $"Right tracked: {rightTracked}\n" +
            $"Right index pinch: {rightIndex}\n" +
            $"Right middle pinch: {rightMiddle}\n" +
            $"Right ring pinch: {rightRing}\n" +
            $"Left wrist local: {wristLocal:F2}\n" +
            $"Wrist in zone: {wristInZone}";

        if (debugText != null)
        {
            debugText.text = message;
        }

        if (rightIndex)
        {
            Debug.Log("DustinyInputTest: Right index pinch");
        }

        if (wristInZone)
        {
            Debug.Log("DustinyInputTest: Left wrist in status zone");
        }
    }

    private bool IsHandTracked(OVRHand hand)
    {
        return hand != null && hand.IsTracked && hand.IsDataValid;
    }
}
