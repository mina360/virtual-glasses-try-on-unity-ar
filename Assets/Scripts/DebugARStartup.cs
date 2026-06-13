using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class DebugARStartup : MonoBehaviour
{
    public ARCameraManager cameraManager;

    void OnEnable()
    {
        if (cameraManager != null)
        {
            cameraManager.requestedFacingDirection = CameraFacingDirection.User;

            cameraManager.frameReceived += OnCameraFrameReceived;
        }
    }

    void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnCameraFrameReceived;
        }
    }

    void OnCameraFrameReceived(ARCameraFrameEventArgs args)
    {
        //Debug.Log("AR Camera Frame Received");
    }
}
