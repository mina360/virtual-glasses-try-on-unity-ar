//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using UnityEngine;
//using UnityEngine.Networking;
//using GLTFast;

//public class ModelTestLoader : MonoBehaviour
//{
//    [Header("Model URL")]
//    public string modelUrl = "http://10.188.153.107:8000/storage/models/orange.glb";

//    [Header("Spawn Settings")]
//    public Vector3 spawnPosition = Vector3.zero;
//    public Vector3 spawnRotationEuler = Vector3.zero;
//    public float spawnScale = 0.1f; // Smaller scale for scene viewing

//    [Header("Glass Separation Settings")]
//    public bool separateGlasses = true;
//    public bool enableArmRotation = true; // Expose arm rotation for testing
//    public float leftArmTestAngle = 20f;
//    public float rightArmTestAngle = -20f;
//    public bool animateTestRotation = false; // Animate in scene if desired
//    public float testRotationSpeed = 30f;

//    private GameObject currentLoadedModel;
//    private GltfImport importer;
//    private glassScript currentGlassScriptInstance; // Reference to the separation script instance

//    void Start()
//    {
//        Debug.Log("[ModelTestLoader] Start method called. Initiating model load.");
//        LoadAndSeparateModelInScene();
//    }

//    [ContextMenu("Load And Separate Model")]
//    public async void LoadAndSeparateModelInScene()
//    {
//        Debug.Log("[ModelTestLoader] LoadAndSeparateModelInScene called.");

//        // Cleanup previous model
//        if (currentLoadedModel != null)
//        {
//            Debug.Log("[ModelTestLoader] Destroying previous loaded model.");
//            Destroy(currentLoadedModel);
//            currentLoadedModel = null;
//        }
//        if (importer != null)
//        {
//            Debug.Log("[ModelTestLoader] Disposing previous GLTFast importer.");
//            importer.Dispose();
//            importer = null;
//        }
//        if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[ModelTestLoader] Destroying previous glassScript host.");
//            Destroy(currentGlassScriptInstance.gameObject);
//            currentGlassScriptInstance = null;
//        }


//        if (string.IsNullOrEmpty(modelUrl))
//        {
//            Debug.LogError("[ModelTestLoader] Model URL is empty! Please set it in the inspector.");
//            return;
//        }

//        Debug.Log($"[ModelTestLoader] Starting download from: {modelUrl}");

//        UnityWebRequest www = UnityWebRequest.Get(modelUrl);
//        var operation = www.SendWebRequest();
//        await operation;

//        if (www.result != UnityWebRequest.Result.Success)
//        {
//            Debug.LogError($"[ModelTestLoader] Download failed: {www.error}");
//            www.Dispose();
//            return;
//        }

//        byte[] data = www.downloadHandler.data;
//        www.Dispose();

//        Debug.Log($"[ModelTestLoader] Downloaded {data.Length} bytes successfully.");

//        importer = new GltfImport();
//        bool ok = await importer.LoadGltfBinary(data);
//        if (!ok)
//        {
//            Debug.LogError("[ModelTestLoader] Failed to parse GLB data. Model might be corrupt or malformed.");
//            return;
//        }

//        GameObject tempModelParent = new GameObject("OriginalGLBModel");
//        tempModelParent.transform.position = spawnPosition;
//        tempModelParent.transform.rotation = Quaternion.Euler(spawnRotationEuler);
//        tempModelParent.transform.localScale = Vector3.one * spawnScale;
//        Debug.Log($"[ModelTestLoader] Created temporary parent 'OriginalGLBModel' at {tempModelParent.transform.position} with scale {spawnScale}.");


//        var instantiationResult = await importer.InstantiateMainSceneAsync(tempModelParent.transform);

//        if (instantiationResult)
//        {
//            currentLoadedModel = tempModelParent;
//            Debug.Log("[ModelTestLoader] GLB model instantiated successfully into 'OriginalGLBModel'.");

//            // --- Material Extraction ---
//            List<Material> originalMaterials = new List<Material>();
//            MeshRenderer[] originalRenderers = currentLoadedModel.GetComponentsInChildren<MeshRenderer>();
//            if (originalRenderers.Length > 0)
//            {
//                foreach (var r in originalRenderers)
//                {
//                    originalMaterials.AddRange(r.sharedMaterials);
//                }
//                Debug.Log($"[ModelTestLoader] Extracted {originalMaterials.Count} materials from original model.");
//            }
//            else
//            {
//                Debug.LogWarning("[ModelTestLoader] No MeshRenderers found on original model for material extraction.");
//            }
//            // --- End Material Extraction ---

//            MeshFilter sourceMeshFilter = currentLoadedModel.GetComponentInChildren<MeshFilter>();

//            if (sourceMeshFilter == null)
//            {
//                Debug.LogError("[ModelTestLoader] No MeshFilter found in the loaded GLB model hierarchy! Cannot perform separation. Displaying full model.");
//            }
//            else
//            {
//                Debug.Log($"[ModelTestLoader] Found source MeshFilter: {sourceMeshFilter.name} with mesh {sourceMeshFilter.sharedMesh.name}.");
//                if (separateGlasses)
//                {
//                    Debug.Log("[ModelTestLoader] 'separateGlasses' is true. Attempting to separate glasses mesh...");
//                    await SeparateAndPlaceGlasses(sourceMeshFilter, originalMaterials.ToArray());
//                }
//                else
//                {
//                    Debug.Log("[ModelTestLoader] 'separateGlasses' is false. Keeping full model as-is.");
//                    // The model is already positioned by tempModelParent's transform
//                }
//            }

//            Debug.Log("[ModelTestLoader] Model processing complete.");
//        }
//        else
//        {
//            Debug.LogError("[ModelTestLoader] Failed to instantiate GLB main scene.");
//            Destroy(tempModelParent);
//        }
//    }

//    async Task SeparateAndPlaceGlasses(MeshFilter sourceMeshFilter, Material[] inheritedMaterials)
//    {
//        Debug.Log("[ModelTestLoader] SeparateAndPlaceGlasses method called.");

//        if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
//        {
//            Debug.LogError("[SeparateAndPlaceGlasses] Source MeshFilter or its mesh is null. Aborting separation.");
//            return;
//        }

//        // Create a temporary GameObject to host the glassScript
//        GameObject glassScriptHost = new GameObject("GlassSeparationHost");
//        // Place it at the same position/rotation/scale as the original model for separation context
//        glassScriptHost.transform.position = currentLoadedModel.transform.position;
//        glassScriptHost.transform.rotation = currentLoadedModel.transform.rotation;
//        glassScriptHost.transform.localScale = currentLoadedModel.transform.localScale;
//        Debug.Log($"[SeparateAndPlaceGlasses] Created 'GlassSeparationHost' at original model's transform: {glassScriptHost.transform.position}.");

//        glassScript separator = glassScriptHost.AddComponent<glassScript>();

//        // Configure glassScript settings
//        separator.sourceGlasses = sourceMeshFilter;
//        separator.createGameObjects = true;
//        separator.createPrefab = false;
//        separator.prefabName = "SeparatedRuntimeGlasses"; // Name for the parent of separated parts
//        separator.minVerticesPerPart = 1000;
//        separator.mergeSmallParts = true;
//        separator.mergeDistanceThreshold = 0.5f;
//        separator.maxParts = 10;
//        separator.preventGaps = true;
//        separator.boundaryOverlap = 0.05f;
//        separator.duplicateBoundaryVertices = true;
//        separator.seamWeldingTolerance = 0.0005f;
//        separator.enableArmRotation = enableArmRotation; // Use test setting
//        separator.leftArmRotationAngle = leftArmTestAngle; // Use test setting
//        separator.rightArmRotationAngle = rightArmTestAngle; // Use test setting
//        separator.animateRotation = animateTestRotation; // Use test setting
//        separator.rotationSpeed = testRotationSpeed; // Use test setting
//        separator.useLocalCoordinates = true;
//        separator.autoDetectOrientation = true;
//        Debug.Log("[SeparateAndPlaceGlasses] glassScript component added and configured.");

//        // Call the separation method
//        separator.AutoSeparateGlassesFixed();
//        Debug.Log("[SeparateAndPlaceGlasses] separator.AutoSeparateGlassesFixed() called.");


//        // The glassScript will create a new parent GameObject containing the separated parts.
//        // It's crucial that this name matches 'separator.prefabName'
//        GameObject separatedParent = GameObject.Find(separator.prefabName);

//        if (separatedParent != null)
//        {
//            Debug.Log($"[SeparateAndPlaceGlasses] Found separated parent GameObject: '{separatedParent.name}'.");

//            // --- Apply Inherited Materials ---
//            MeshRenderer[] separatedRenderers = separatedParent.GetComponentsInChildren<MeshRenderer>();
//            int materialIndex = 0;
//            if (separatedRenderers.Length > 0)
//            {
//                Debug.Log($"[SeparateAndPlaceGlasses] Applying materials to {separatedRenderers.Length} separated renderers.");
//                foreach (var renderer in separatedRenderers)
//                {
//                    if (inheritedMaterials != null && inheritedMaterials.Length > 0)
//                    {
//                        renderer.sharedMaterial = inheritedMaterials[materialIndex % inheritedMaterials.Length];
//                        // Debug.Log($"[SeparateAndPlaceGlasses] Applied material {renderer.sharedMaterial.name} to {renderer.name}.");
//                        materialIndex++;
//                    }
//                    else
//                    {
//                        renderer.sharedMaterial = new Material(Shader.Find("Standard"));
//                        Debug.LogWarning("[SeparateAndPlaceGlasses] No inherited materials found, applying default Standard material to " + renderer.name + ".");
//                    }
//                }
//            }
//            else
//            {
//                Debug.LogWarning("[SeparateAndPlaceGlasses] No MeshRenderers found on separated parts to apply materials.");
//            }
//            // --- End Apply Inherited Materials ---

//            // Now, destroy the original GLB model and replace currentLoadedModel with the separated parts
//            if (currentLoadedModel != null)
//            {
//                Debug.Log("[SeparateAndPlaceGlasses] Destroying original GLB model parent 'OriginalGLBModel'.");
//                Destroy(currentLoadedModel);
//            }
//            currentLoadedModel = separatedParent; // The new currentLoadedModel is the parent of separated parts

//            // Ensure the new parent maintains the original spawn position/rotation/scale
//            currentLoadedModel.transform.position = spawnPosition;
//            currentLoadedModel.transform.rotation = Quaternion.Euler(spawnRotationEuler);
//            currentLoadedModel.transform.localScale = Vector3.one * spawnScale;
//            Debug.Log($"[SeparateAndPlaceGlasses] Separated glasses parent '{currentLoadedModel.name}' positioned at {currentLoadedModel.transform.position}, rotation {currentLoadedModel.transform.rotation.eulerAngles}, scale {currentLoadedModel.transform.localScale}.");


//            currentGlassScriptInstance = separator; // Keep reference for arm control

//        }
//        else
//        {
//            Debug.LogError("[SeparateAndPlaceGlasses] Failed to find the separated glasses parent GameObject ('" + separator.prefabName + "'). Separation likely failed or naming convention changed.");
//            // If separation failed, the original currentLoadedModel remains
//        }

//        // If currentGlassScriptInstance is null (meaning separation failed), destroy the host.
//        // Otherwise, the host (with glassScript) is kept to manage arm animations/rotations.
//        if (currentGlassScriptInstance == null && glassScriptHost != null)
//        {
//            Debug.Log("[SeparateAndPlaceGlasses] Separation failed or glassScriptInstance not kept, destroying GlassSeparationHost.");
//            Destroy(glassScriptHost);
//        }
//        else if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[SeparateAndPlaceGlasses] glassScriptHost retained for arm control.");
//            // No need to nullify sourceGlasses if we're keeping the host and currentGlassScriptInstance,
//            // as the glassScript's internal logic will handle its own MeshFilter reference.
//        }
//    }

//    void OnDestroy()
//    {
//        Debug.Log("[ModelTestLoader] OnDestroy called. Cleaning up resources.");

//        if (importer != null)
//        {
//            importer.Dispose();
//            importer = null;
//        }

//        if (currentLoadedModel != null)
//        {
//            Destroy(currentLoadedModel);
//            currentLoadedModel = null;
//        }

//        if (currentGlassScriptInstance != null)
//        {
//            // Destroy the host GameObject which has the glassScript component
//            Destroy(currentGlassScriptInstance.gameObject);
//            currentGlassScriptInstance = null;
//        }
//    }

//    // Context menu items for testing arm rotation directly in the inspector
//    [ContextMenu("Open Arms (Test)")]
//    public void OpenTestArms()
//    {
//        if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[ModelTestLoader] Calling OpenArms on glassScript instance.");
//            currentGlassScriptInstance.OpenArms();
//        }
//        else
//        {
//            Debug.LogWarning("[ModelTestLoader] No active glassScript instance to open arms.");
//        }
//    }

//    [ContextMenu("Close Arms (Test)")]
//    public void CloseTestArms()
//    {
//        if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[ModelTestLoader] Calling CloseArms on glassScript instance.");
//            currentGlassScriptInstance.CloseArms();
//        }
//        else
//        {
//            Debug.LogWarning("[ModelTestLoader] No active glassScript instance to close arms.");
//        }
//    }

//    [ContextMenu("Fold Arms (Test)")]
//    public void FoldTestArms()
//    {
//        if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[ModelTestLoader] Calling FoldArms on glassScript instance.");
//            currentGlassScriptInstance.FoldArms();
//        }
//        else
//        {
//            Debug.LogWarning("[ModelTestLoader] No active glassScript instance to fold arms.");
//        }
//    }

//    [ContextMenu("Reset Arms (Test)")]
//    public void ResetTestArms()
//    {
//        if (currentGlassScriptInstance != null)
//        {
//            Debug.Log("[ModelTestLoader] Calling ResetArmPosition on glassScript instance.");
//            currentGlassScriptInstance.ResetArmPosition();
//        }
//        else
//        {
//            Debug.LogWarning("[ModelTestLoader] No active glassScript instance to reset arms.");
//        }
//    }

//    // You can add a button in the inspector to trigger a full reload
//    [ContextMenu("Reload Model")]
//    public void ReloadModel()
//    {
//        Debug.Log("[ModelTestLoader] Reload Model triggered via Context Menu.");
//        LoadAndSeparateModelInScene();
//    }
//}