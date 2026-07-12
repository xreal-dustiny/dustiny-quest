using UnityEngine;
using UnityEngine.Android;

public class QuestCameraPermission : MonoBehaviour
{
    private const string HeadsetCameraPermission =
        "horizonos.permission.HEADSET_CAMERA";

    [Header("[ 권한 설정 ]")]
    public bool requestPermissionOnStart = true;

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (requestPermissionOnStart)
        {
            RequestCameraPermission();
        }
#else
        Debug.Log(
            "[Quest Camera] 에디터에서는 " +
            "헤드셋 카메라 권한을 요청하지 않습니다."
        );
#endif
    }

    public void RequestCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Permission.HasUserAuthorizedPermission(
                HeadsetCameraPermission))
        {
            Debug.Log(
                "[Quest Camera] 헤드셋 카메라 권한이 " +
                "이미 허용되어 있습니다."
            );

            return;
        }

        PermissionCallbacks callbacks =
            new PermissionCallbacks();

        callbacks.PermissionGranted +=
            OnPermissionGranted;

        callbacks.PermissionDenied +=
            OnPermissionDenied;

        callbacks.PermissionDeniedAndDontAskAgain +=
            OnPermissionDeniedAndDontAskAgain;

        Permission.RequestUserPermission(
            HeadsetCameraPermission,
            callbacks
        );
#endif
    }

    private void OnPermissionGranted(
        string permissionName
    )
    {
        Debug.Log(
            $"[Quest Camera] 권한 허용: {permissionName}"
        );
    }

    private void OnPermissionDenied(
        string permissionName
    )
    {
        Debug.LogError(
            $"[Quest Camera] 권한 거부: {permissionName}"
        );
    }

    private void OnPermissionDeniedAndDontAskAgain(
        string permissionName
    )
    {
        Debug.LogError(
            $"[Quest Camera] 다시 묻지 않음으로 거부됨: " +
            $"{permissionName}"
        );
    }
}