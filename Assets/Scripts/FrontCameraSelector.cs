using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class FrontCameraSelector : MonoBehaviour
{
    void Awake()
    {
        var cameraManager = GetComponent<ARCameraManager>();
        if (cameraManager != null)
        {
            cameraManager.requestedFacingDirection = CameraFacingDirection.User;
        }
    }
}
