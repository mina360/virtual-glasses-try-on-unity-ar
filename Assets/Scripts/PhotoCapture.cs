using UnityEngine;
using System.IO;
using System.Collections;

public class PhotoCapture : MonoBehaviour
{
    public void CaptureScreenshot()
    {
        string fileName = $"VirtualTryOn_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
        string filePath = Path.Combine(Application.persistentDataPath, fileName);

        ScreenCapture.CaptureScreenshot(fileName);
        Debug.Log($"[PhotoCapture] Screenshot saved internally: {filePath}");

        StartCoroutine(SaveToGallery(filePath));
    }

    private IEnumerator SaveToGallery(string filePath)
    {
        yield return new WaitForEndOfFrame();

#if UNITY_ANDROID
        NativeGallery.SaveImageToGallery(filePath, "TryOns", Path.GetFileName(filePath));
        Debug.Log("[PhotoCapture] Saved to Gallery/Photos under album: Try-Ons");
#endif
    }
}
