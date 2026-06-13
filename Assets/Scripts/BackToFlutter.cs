using UnityEngine;
using UnityEngine.InputSystem;

public class BackToFlutter : MonoBehaviour
{
    private Controls controls;

    private void Awake()
    {
        controls = new Controls();
    }

    private void OnEnable()
    {
        controls.UI.Enable();
        controls.UI.Back.performed += OnBack;
    }

    private void OnDisable()
    {
        controls.UI.Back.performed -= OnBack;
        controls.UI.Disable();
    }

    private void OnBack(InputAction.CallbackContext ctx)
    {
        ExitToFlutter();
    }

    public void ExitToFlutter()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            currentActivity.Call("finish");
        }
#else
        Debug.Log("ExitToFlutter called (Editor)");
#endif
    }
}
