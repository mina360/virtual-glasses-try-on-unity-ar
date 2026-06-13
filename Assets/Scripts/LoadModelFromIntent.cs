using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Networking;
using GLTFast;

[RequireComponent(typeof(ARFace))]
public class LoadModelFromIntent : MonoBehaviour
{
    private ARFace arFace;

    [Header("Firebase Model URL")]
    public string defaultModelUrl = "http://10.188.153.107:8000/storage/models/model.glb";

    [Header("Placement on Face")]
    public int anchorVertexIndex = 168;
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;
    public float scale = 0.17f;

    private GameObject modelInstance;
    private GltfImport importer;

    async void Start()
    {
        arFace = GetComponent<ARFace>();

        if (arFace == null)
        {
            Debug.LogError("[LoadModelFromIntent] ARFace component not found!");
            return;
        }

        string modelUrl = defaultModelUrl;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject intent = currentActivity.Call<AndroidJavaObject>("getIntent");

            if (intent.Call<bool>("hasExtra", "model_url_key"))
            {
                modelUrl = intent.Call<string>("getStringExtra", "model_url_key");
                Debug.Log("[LoadModelFromIntent] URL received from Flutter: " + modelUrl);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LoadModelFromIntent] Failed to get URL from intent: " + e.Message);
        }
#endif

        if (!string.IsNullOrEmpty(modelUrl))
            await DownloadAndAttachModel(modelUrl);
        else
            Debug.LogError("[LoadModelFromIntent] Model URL empty.");
    }

    async Task DownloadAndAttachModel(string url)
    {
        Debug.Log("[LoadModelFromIntent] Starting download from: " + url);

        if (modelInstance != null)
        {
            Destroy(modelInstance);
            modelInstance = null;
        }

        UnityWebRequest www = UnityWebRequest.Get(url);

        var operation = www.SendWebRequest();
        await operation;

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[LoadModelFromIntent] Download failed: " + www.error);
            www.Dispose();
            return;
        }

        byte[] data = www.downloadHandler.data;
        www.Dispose();

        Debug.Log($"[LoadModelFromIntent] Downloaded {data.Length} bytes");

        importer = new GltfImport();
        bool ok = await importer.LoadGltfBinary(data);
        if (!ok)
        {
            Debug.LogError("[LoadModelFromIntent] Failed to parse GLB data");
            return;
        }

        GameObject modelParent = new GameObject("ModelParent");
        modelParent.transform.SetParent(arFace.transform, false);

        var instantiationResult = await importer.InstantiateMainSceneAsync(modelParent.transform);

        if (instantiationResult)
        {
            modelInstance = modelParent;

            PositionModel();

            Debug.Log("[LoadModelFromIntent] Model successfully attached to ARFace.");

            LogModelInfo();
        }
        else
        {
            Debug.LogError("[LoadModelFromIntent] Failed to instantiate model");
            Destroy(modelParent);
        }
    }

    void PositionModel()
    {
        if (modelInstance == null) return;

        if (HasVertex(anchorVertexIndex))
        {
            Vector3 localPos = arFace.vertices[anchorVertexIndex];
            modelInstance.transform.localPosition = localPos + positionOffset;
            modelInstance.transform.localRotation = Quaternion.Euler(rotationOffsetEuler);
            modelInstance.transform.localScale = Vector3.one * scale;
            Debug.Log($"[LoadModelFromIntent] Model positioned at local: {modelInstance.transform.localPosition}");
        }
        else
        {
            modelInstance.transform.localPosition = positionOffset;
            modelInstance.transform.localRotation = Quaternion.Euler(rotationOffsetEuler);
            modelInstance.transform.localScale = Vector3.one * scale;

            Debug.Log("[LoadModelFromIntent] Using fallback positioning");
        }
    }

    void LogModelInfo()
    {
        if (modelInstance == null) return;

        int childCount = modelInstance.transform.childCount;
        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>();

        Debug.Log($"[LoadModelFromIntent] Model has {childCount} children and {renderers.Length} renderers");

        foreach (var renderer in renderers)
        {
            renderer.enabled = true;
        }
    }

    void Update()
    {
        if (modelInstance != null && HasVertex(anchorVertexIndex))
        {
            Vector3 localPos = arFace.vertices[anchorVertexIndex];
            modelInstance.transform.localPosition = localPos + positionOffset;
        }
    }

    bool HasVertex(int idx)
    {
        return arFace != null && arFace.vertices != null && arFace.vertices.Length > idx;
    }

    void OnDestroy()
    {
        if (importer != null)
        {
            importer.Dispose();
            importer = null;
        }

        if (modelInstance != null)
        {
            Destroy(modelInstance);
            modelInstance = null;
        }
    }
}