using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Networking;
using GLTFast;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(ARFace))]
public class IntegratedARGlassesLoader : MonoBehaviour
{
    private ARFace arFace;

    [Header("Firebase Model URL")]
    public string defaultModelUrl = "http://10.188.153.107:8000/storage/models/orange.glb";

    [Header("Placement on Face")]
    public int anchorVertexIndex = 168; // Nose bridge middle
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;
    public float scale = 0.17f;

    [Header("Glass Separation Settings")]
    public Material partMaterial;
    [SerializeField] private int minVerticesPerPart = 10000;
    [SerializeField] private bool mergeSmallParts = true;
    [SerializeField] private float mergeDistanceThreshold = 1.0f;
    [SerializeField] private int maxParts = 10;
    [SerializeField] private bool preventGaps = true;
    [SerializeField] private float boundaryOverlap = 0.1f;
    [SerializeField] private bool duplicateBoundaryVertices = true;
    [SerializeField] private float seamWeldingTolerance = 0.001f;
    [SerializeField] private bool useLocalCoordinates = true;
    [SerializeField] private bool debugCoordinates = false;

    [Header("Arm Rotation Settings")]
    [SerializeField] private bool enableArmRotation = true;
    [SerializeField] private float leftArmRotationAngle = 20f;
    [SerializeField] private float rightArmRotationAngle = -20f;
    [SerializeField] private bool animateRotation = false;
    [SerializeField] private float rotationSpeed = 30f;

    [Header("Tilt Detection Settings")]
    public float tiltThreshold = 5f;
    public float tiltCooldown = 0.3f;
    public bool debugTiltDetection = true;

    [Header("Arm Visibility Control")]
    public bool enableArmVisibilityControl = true;
    public float armVisibilityTiltThreshold = 15f;

    // Private variables for model and separation
    private Transform modelInstance;
    private GltfImport importer;
    private GameObject separatedGlassesParent;

    // Arm control variables
    private List<GameObject> leftArmParts = new List<GameObject>();
    private List<GameObject> rightArmParts = new List<GameObject>();
    private List<GameObject> alwaysVisibleObjects = new List<GameObject>();
    private List<GameObject> alwaysHiddenObjects = new List<GameObject>();

    // Animation tracking
    private bool isAnimating = false;
    private float currentLeftRotation = 0f;
    private float currentRightRotation = 0f;
    private float targetLeftRotation = 0f;
    private float targetRightRotation = 0f;

    // Tilt detection variables
    private Vector3 previousRotation;
    private float lastTiltTime;
    private bool isInitialized = false;
    private int frameCounter = 0;

    async void Start()
    {
        arFace = GetComponent<ARFace>();

        if (arFace == null)
        {
            Debug.LogError("[IntegratedLoader] ARFace component not found!");
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
                Debug.Log("[IntegratedLoader] URL received from Flutter: " + modelUrl);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[IntegratedLoader] Failed to get URL from intent: " + e.Message);
        }
#endif

        if (!string.IsNullOrEmpty(modelUrl))
            await DownloadLoadAndSeparateModel(modelUrl);
        else
            Debug.LogError("[IntegratedLoader] Model URL empty.");
    }

    void Update()
    {
        // Position the separated glasses
        if (separatedGlassesParent != null && HasVertex(anchorVertexIndex))
        {
            Vector3 localAnchor = arFace.vertices[anchorVertexIndex];
            separatedGlassesParent.transform.localPosition = localAnchor + positionOffset;
            separatedGlassesParent.transform.localRotation = Quaternion.Euler(rotationOffsetEuler);
        }

        // Handle arm animations
        if (isAnimating && enableArmRotation)
        {
            bool leftComplete = UpdateArmRotation(ref currentLeftRotation, targetLeftRotation, leftArmParts, true);
            bool rightComplete = UpdateArmRotation(ref currentRightRotation, targetRightRotation, rightArmParts, false);

            if (leftComplete && rightComplete)
            {
                isAnimating = false;
            }
        }

        // Detect face tilts
        DetectFaceTilt();
    }

    async Task DownloadLoadAndSeparateModel(string url)
    {
        Debug.Log("[IntegratedLoader] Starting download from: " + url);

        // Step 1: Download the model (using LoadModelFromIntent approach)
        UnityWebRequest www = UnityWebRequest.Get(url);
        var operation = www.SendWebRequest();
        await operation;

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[IntegratedLoader] Download failed: " + www.error);
            www.Dispose();
            return;
        }

        byte[] data = www.downloadHandler.data;
        www.Dispose();

        Debug.Log($"[IntegratedLoader] Downloaded {data.Length} bytes");

        // Step 2: Load the GLTF model (using LoadModelFromIntent approach)
        importer = new GltfImport();
        bool ok = await importer.LoadGltfBinary(data);
        if (!ok)
        {
            Debug.LogError("[IntegratedLoader] Failed to parse GLB data");
            return;
        }

        var root = new GameObject("LoadedGLTFModel");
        var instantiationResult = await importer.InstantiateMainSceneAsync(root.transform);

        if (!instantiationResult)
        {
            Debug.LogError("[IntegratedLoader] Failed to instantiate model");
            Destroy(root);
            return;
        }

        // Fix shaders
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            foreach (var mat in renderer.materials)
            {
                if (mat.shader.name.Contains("glTF"))
                    mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            }
        }

        // Step 3: Get the main mesh for separation
        MeshFilter mainMeshFilter = root.GetComponentInChildren<MeshFilter>();
        if (mainMeshFilter == null || mainMeshFilter.mesh == null)
        {
            Debug.LogError("[IntegratedLoader] No mesh found in loaded model");
            Destroy(root);
            return;
        }

        // Step 4: Separate the glasses using the logic from glassScript
        var separatedMeshes = SeparateGlassesMesh(mainMeshFilter.mesh, root.transform);

        // Step 5: Create GameObjects from separated meshes
        separatedGlassesParent = CreateGameObjectsFromSeparatedMeshes(separatedMeshes);
        separatedGlassesParent.transform.SetParent(arFace.transform, false);
        separatedGlassesParent.transform.localScale = Vector3.one * scale;

        // Step 6: Setup arm control
        SetupArmControl(separatedGlassesParent);

        // Step 7: Apply initial arm rotation if enabled
        if (enableArmRotation)
        {
            if (animateRotation)
            {
                StartArmAnimation();
            }
            else
            {
                ApplyImmediateArmRotation();
            }
        }

        // Clean up original downloaded model
        Destroy(root);

        Debug.Log("[IntegratedLoader] Model downloaded, separated, and configured successfully.");
    }

    private Dictionary<string, Mesh> SeparateGlassesMesh(Mesh originalMesh, Transform originalTransform)
    {
        // Detect orientation and apply correction
        Quaternion correctOrientation = DetectGlassesOrientation(originalMesh);

        // Apply temporary transform for consistent separation
        Vector3 originalPos = originalTransform.position;
        Quaternion originalRot = originalTransform.rotation;
        Vector3 originalScale = originalTransform.localScale;

        originalTransform.position = Vector3.zero;
        originalTransform.rotation = correctOrientation;
        originalTransform.localScale = Vector3.one;

        try
        {
            var separatedParts = GlassesSpatialSeparation(originalMesh, originalTransform);

            if (preventGaps)
            {
                separatedParts = PreventGapsBetweenParts(separatedParts, originalMesh);
            }

            if (mergeSmallParts)
            {
                separatedParts = MergeSmallParts(separatedParts, originalMesh.vertices);
            }

            var classifiedParts = ClassifyGlassParts(separatedParts, originalMesh.vertices);
            var resultMeshes = new Dictionary<string, Mesh>();

            foreach (var part in classifiedParts)
            {
                var mesh = CreateMeshFromVertexIndices(originalMesh, part.vertices, part.name);
                if (mesh != null && mesh.vertexCount >= 25)
                {
                    resultMeshes[part.name] = mesh;
                }
            }

            return resultMeshes;
        }
        finally
        {
            // Restore original transform
            originalTransform.position = originalPos;
            originalTransform.rotation = originalRot;
            originalTransform.localScale = originalScale;
        }
    }

    private Quaternion DetectGlassesOrientation(Mesh mesh)
    {
        var bounds = mesh.bounds;
        Vector3 size = bounds.size;

        float maxDimension = Mathf.Max(size.x, size.y, size.z);
        Quaternion correctionRotation = Quaternion.identity;

        if (maxDimension == size.y)
        {
            correctionRotation = Quaternion.Euler(-90, 0, 0);
        }
        else if (maxDimension == size.z && size.x < size.y)
        {
            correctionRotation = Quaternion.Euler(0, 90, 0);
        }
        else if (size.y == maxDimension && size.z == Mathf.Min(size.x, size.y, size.z))
        {
            correctionRotation = Quaternion.Euler(0, 0, 90);
        }

        return correctionRotation;
    }

    private List<MeshPart> GlassesSpatialSeparation(Mesh mesh, Transform meshTransform)
    {
        var vertices = GetNormalizedLocalVertices(mesh, meshTransform);
        var bounds = CalculateBounds(vertices);

        var assignments = new int[vertices.Length];
        for (int i = 0; i < assignments.Length; i++)
        {
            assignments[i] = -1;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            assignments[i] = ClassifyPosition(vertices[i], bounds);
        }

        if (preventGaps)
        {
            assignments = SmoothBoundaryAssignments(assignments, vertices, mesh.triangles);
        }

        var regionGroups = new Dictionary<int, List<int>>();
        for (int i = 0; i < assignments.Length; i++)
        {
            int region = assignments[i];
            if (!regionGroups.ContainsKey(region))
                regionGroups[region] = new List<int>();
            regionGroups[region].Add(i);
        }

        string[] regionNames = {
            "LeftLens", "RightLens", "Bridge",
            "LeftArm", "RightArm", "CenterBack",
            "LeftLensFrame", "RightLensFrame", "CenterFrame",
            "LeftArmConnection", "RightArmConnection", "CenterConnection"
        };

        var parts = new List<MeshPart>();
        foreach (var kvp in regionGroups)
        {
            if (kvp.Value.Count >= 10)
            {
                string name = kvp.Key < regionNames.Length ? regionNames[kvp.Key] : $"Region_{kvp.Key}";
                parts.Add(new MeshPart(name, kvp.Value, mesh.vertices));
            }
        }

        return parts;
    }

    private Vector3[] GetNormalizedLocalVertices(Mesh mesh, Transform meshTransform)
    {
        var vertices = mesh.vertices;
        if (!useLocalCoordinates || meshTransform == null)
            return vertices;

        Vector3[] normalizedVertices = new Vector3[vertices.Length];
        Quaternion currentRotation = meshTransform.rotation;
        Quaternion inverseRotation = Quaternion.Inverse(currentRotation);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = meshTransform.TransformPoint(vertices[i]);
            Vector3 meshCenter = meshTransform.TransformPoint(mesh.bounds.center);
            Vector3 relativeToCenter = worldVertex - meshCenter;
            Vector3 normalizedRelative = inverseRotation * relativeToCenter;
            normalizedVertices[i] = normalizedRelative + mesh.bounds.center;
        }

        return normalizedVertices;
    }

    private Bounds CalculateBounds(Vector3[] vertices)
    {
        if (vertices.Length == 0) return new Bounds();

        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 vertex in vertices)
        {
            bounds.Encapsulate(vertex);
        }
        return bounds;
    }

    private int ClassifyPosition(Vector3 position, Bounds bounds)
    {
        var relativePos = position - bounds.center;
        float relX = relativePos.x / bounds.size.x;
        float relY = relativePos.y / bounds.size.y;
        float relZ = relativePos.z / bounds.size.z;

        if (relZ > 0.35f) // Front part (lenses)
        {
            if (relX < -0.2f) return 0; // Left lens
            else if (relX > 0.2f) return 1; // Right lens
            else return 2; // Bridge
        }
        else if (relZ < -0.25f) // Back part (arms)
        {
            if (relX < -0.15f) return 3; // Left arm
            else if (relX > 0.15f) return 4; // Right arm
            else return 5; // Center back
        }
        else // Middle part (frames/connections)
        {
            if (relZ > 0.05f) // Front frame
            {
                if (relX < -0.25f) return 6; // Left lens frame
                else if (relX > 0.25f) return 7; // Right lens frame
                else return 8; // Center frame
            }
            else // Connections
            {
                if (relX < -0.2f) return 9; // Left arm connection
                else if (relX > 0.2f) return 10; // Right arm connection
                else return 11; // Center connection
            }
        }
    }

    private GameObject CreateGameObjectsFromSeparatedMeshes(Dictionary<string, Mesh> meshParts)
    {
        var parentGO = new GameObject("SeparatedGlasses");
        parentGO.transform.position = transform.position;
        parentGO.transform.rotation = transform.rotation;
        parentGO.transform.localScale = transform.localScale;

        foreach (var meshPart in meshParts)
        {
            var partGO = new GameObject(meshPart.Key);
            partGO.transform.SetParent(parentGO.transform);
            partGO.transform.localPosition = Vector3.zero;
            partGO.transform.localRotation = Quaternion.identity;
            partGO.transform.localScale = Vector3.one;

            var meshFilter = partGO.AddComponent<MeshFilter>();
            var meshRenderer = partGO.AddComponent<MeshRenderer>();
            var meshCollider = partGO.AddComponent<MeshCollider>();

            meshCollider.sharedMesh = meshPart.Value;
            meshFilter.mesh = meshPart.Value;
            meshRenderer.material = partMaterial != null ? partMaterial : CreateDefaultMaterial(meshPart.Key);

            partGO.tag = "GlassPart";
        }

        CenterOnBridge(parentGO);
        return parentGO;
    }

    private void SetupArmControl(GameObject glassesGameObject)
    {
        leftArmParts.Clear();
        rightArmParts.Clear();
        alwaysVisibleObjects.Clear();
        alwaysHiddenObjects.Clear();

        Transform[] allChildren = glassesGameObject.GetComponentsInChildren<Transform>();

        foreach (Transform child in allChildren)
        {
            string name = child.name.ToLower();

            // Parts that should always be hidden
            if (name == "leftarm" || name == "rightarm")
            {
                alwaysHiddenObjects.Add(child.gameObject);
                continue;
            }

            // Parts that should always be visible (lens frames)
            if (name.Contains("lens") && name.Contains("frame"))
            {
                alwaysVisibleObjects.Add(child.gameObject);
                continue;
            }

            // Left arm parts
            if (name.Contains("left") && (name.Contains("arm") || name.Contains("frame") || name.Contains("connection")) && name != "leftarm")
            {
                leftArmParts.Add(child.gameObject);
            }
            // Right arm parts
            else if (name.Contains("right") && (name.Contains("arm") || name.Contains("frame") || name.Contains("connection")) && name != "rightarm")
            {
                rightArmParts.Add(child.gameObject);
            }
        }

        // Hide arm ends immediately
        SetArmVisibility(alwaysHiddenObjects, false);

        Debug.Log($"[Arm Control] Setup complete - Left: {leftArmParts.Count}, Right: {rightArmParts.Count}, Visible: {alwaysVisibleObjects.Count}, Hidden: {alwaysHiddenObjects.Count}");
    }

    void DetectFaceTilt()
    {
        if (arFace == null || arFace.transform == null) return;
        if (arFace.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking) return;

        Vector3 currentRotation = arFace.transform.eulerAngles;

        if (debugTiltDetection && frameCounter % 30 == 0)
        {
            Debug.Log($"[Tilt Debug] Current rotation: {currentRotation}, Previous: {previousRotation}");
        }
        frameCounter++;

        if (!isInitialized)
        {
            previousRotation = currentRotation;
            isInitialized = true;
            return;
        }

        float timeSinceLastTilt = Time.time - lastTiltTime;
        if (timeSinceLastTilt < tiltCooldown) return;

        Vector3 rotationDiff = GetRotationDifference(currentRotation, previousRotation);

        bool tiltDetected = false;
        string tiltDirection = "";

        // Roll (Z-axis) - Left/Right head tilt
        if (Mathf.Abs(rotationDiff.z) > tiltThreshold)
        {
            tiltDirection += rotationDiff.z > 0 ? "Right " : "Left ";
            tiltDetected = true;
        }

        // Pitch (X-axis) - Up/Down head tilt
        if (Mathf.Abs(rotationDiff.x) > tiltThreshold)
        {
            tiltDirection += rotationDiff.x > 0 ? "Down " : "Up ";
            tiltDetected = true;
        }

        // Yaw (Y-axis) - Left/Right head turn
        if (Mathf.Abs(rotationDiff.y) > tiltThreshold)
        {
            tiltDirection += rotationDiff.y > 0 ? "Turn-Right " : "Turn-Left ";
            tiltDetected = true;
        }

        if (tiltDetected)
        {
            if (debugTiltDetection)
                Debug.Log($"[Face Tilt Detected] {tiltDirection.Trim()} (X:{rotationDiff.x:F1}°, Y:{rotationDiff.y:F1}°, Z:{rotationDiff.z:F1}°)");

            lastTiltTime = Time.time;

            if (enableArmVisibilityControl)
            {
                HandleArmVisibility(tiltDirection, rotationDiff);
            }

            previousRotation = currentRotation;
        }
        else if (debugTiltDetection)
        {
            previousRotation = Vector3.Lerp(previousRotation, currentRotation, 0.1f);
        }
    }

    private void HandleArmVisibility(string tiltDirection, Vector3 rotationDiff)
    {
        if (Mathf.Abs(rotationDiff.y) > armVisibilityTiltThreshold)
        {
            if (rotationDiff.y > 0) // Turn-Right
            {
                SetArmVisibility(leftArmParts, true);
                SetArmVisibility(rightArmParts, false);
                if (debugTiltDetection)
                    Debug.Log("[Arm Control] Face turned RIGHT - showing LEFT arm, hiding RIGHT arm");
            }
            else // Turn-Left
            {
                SetArmVisibility(leftArmParts, false);
                SetArmVisibility(rightArmParts, true);
                if (debugTiltDetection)
                    Debug.Log("[Arm Control] Face turned LEFT - hiding LEFT arm, showing RIGHT arm");
            }
        }
        else if (Mathf.Abs(rotationDiff.y) > tiltThreshold)
        {
            SetArmVisibility(leftArmParts, true);
            SetArmVisibility(rightArmParts, true);
        }

        // Always ensure correct visibility for special parts
        SetArmVisibility(alwaysVisibleObjects, true);
        SetArmVisibility(alwaysHiddenObjects, false);
    }

    private void SetArmVisibility(List<GameObject> armParts, bool isVisible)
    {
        foreach (GameObject armPart in armParts)
        {
            if (armPart != null)
            {
                armPart.SetActive(isVisible);
            }
        }
    }

    // Helper methods from both scripts
    private Vector3 GetRotationDifference(Vector3 current, Vector3 previous)
    {
        Vector3 diff = current - previous;
        diff.x = Mathf.DeltaAngle(previous.x, current.x);
        diff.y = Mathf.DeltaAngle(previous.y, current.y);
        diff.z = Mathf.DeltaAngle(previous.z, current.z);
        return diff;
    }

    bool HasVertex(int idx)
    {
        return arFace != null && arFace.vertices != null && arFace.vertices.Length > idx;
    }

    private void CenterOnBridge(GameObject parentGO)
    {
        Transform bridgePart = null;
        foreach (Transform child in parentGO.transform)
        {
            if (child.name.ToLower().Contains("bridge"))
            {
                bridgePart = child;
                break;
            }
        }

        if (bridgePart != null)
        {
            MeshRenderer bridgeRenderer = bridgePart.GetComponent<MeshRenderer>();
            if (bridgeRenderer != null)
            {
                Vector3 bridgeCenter = bridgeRenderer.bounds.center;
                Vector3 offset = parentGO.transform.position - bridgeCenter;
                parentGO.transform.position = bridgeCenter;

                foreach (Transform child in parentGO.transform)
                {
                    child.position += offset;
                }
            }
        }
    }

    private Material CreateDefaultMaterial(string partName)
    {
        var material = new Material(Shader.Find("Standard"));

        if (partName.Contains("Left"))
            material.color = Color.red;
        else if (partName.Contains("Right"))
            material.color = Color.blue;
        else if (partName.Contains("Center") || partName.Contains("Bridge"))
            material.color = Color.green;
        else
            material.color = Color.gray;

        return material;
    }

    // Arm rotation methods
    private void StartArmAnimation()
    {
        targetLeftRotation = leftArmRotationAngle;
        targetRightRotation = rightArmRotationAngle;
        isAnimating = true;
    }

    private void ApplyImmediateArmRotation()
    {
        if (leftArmParts.Count > 0)
        {
            Vector3 leftPivot = GetArmPivotPosition(leftArmParts, true);
            RotateArmPartsAroundPivot(leftArmParts, leftPivot, leftArmRotationAngle, true);
            currentLeftRotation = leftArmRotationAngle;
        }

        if (rightArmParts.Count > 0)
        {
            Vector3 rightPivot = GetArmPivotPosition(rightArmParts, false);
            RotateArmPartsAroundPivot(rightArmParts, rightPivot, rightArmRotationAngle, false);
            currentRightRotation = rightArmRotationAngle;
        }
    }

    private bool UpdateArmRotation(ref float currentRotation, float targetRotation, List<GameObject> armParts, bool isLeftArm)
    {
        if (armParts.Count == 0) return true;

        float rotationDelta = rotationSpeed * Time.deltaTime;
        if (Mathf.Abs(targetRotation - currentRotation) < rotationDelta)
        {
            currentRotation = targetRotation;
        }
        else
        {
            currentRotation += Mathf.Sign(targetRotation - currentRotation) * rotationDelta;
        }

        Vector3 pivotPoint = GetArmPivotPosition(armParts, isLeftArm);
        RotateArmPartsAroundPivot(armParts, pivotPoint, currentRotation, isLeftArm);

        return Mathf.Approximately(currentRotation, targetRotation);
    }

    private Vector3 GetArmPivotPosition(List<GameObject> armParts, bool isLeftArm)
    {
        if (armParts.Count == 0) return Vector3.zero;

        float frontmostZ = float.MinValue;
        Vector3 pivotPoint = Vector3.zero;

        foreach (GameObject part in armParts)
        {
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Bounds bounds = renderer.bounds;
                if (bounds.max.z > frontmostZ)
                {
                    frontmostZ = bounds.max.z;
                    pivotPoint = isLeftArm
                        ? new Vector3(bounds.max.x, bounds.center.y, bounds.max.z)
                        : new Vector3(bounds.min.x, bounds.center.y, bounds.max.z);
                }
            }
        }

        return pivotPoint;
    }

    private void RotateArmPartsAroundPivot(List<GameObject> armParts, Vector3 pivotPoint, float angle, bool isLeftArm)
    {
        foreach (GameObject part in armParts)
        {
            part.transform.rotation = Quaternion.identity;
        }

        Vector3 rotationAxis = Vector3.up;
        foreach (GameObject part in armParts)
        {
            part.transform.RotateAround(pivotPoint, rotationAxis, angle);
        }
    }

    void OnDestroy()
    {
        if (importer != null)
        {
            importer.Dispose();
            importer = null;
        }
    }

    // Supporting data structures and helper methods
    private struct MeshPart
    {
        public string name;
        public List<int> vertices;
        public Vector3 center;
        public Bounds bounds;
        public List<int> boundaryVertices;

        public MeshPart(string name, List<int> vertices, Vector3[] allVertices)
        {
            this.name = name;
            this.vertices = vertices;
            this.boundaryVertices = new List<int>();

            Vector3 sum = Vector3.zero;
            foreach (int v in vertices)
            {
                sum += allVertices[v];
            }
            this.center = sum / vertices.Count;

            Vector3 min = allVertices[vertices[0]];
            Vector3 max = allVertices[vertices[0]];

            foreach (int v in vertices)
            {
                Vector3 pos = allVertices[v];
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }

            this.bounds = new Bounds((min + max) * 0.5f, max - min);
        }
    }

    // Additional helper methods for mesh processing
    private List<MeshPart> PreventGapsBetweenParts(List<MeshPart> parts, Mesh originalMesh)
    {
        var vertices = originalMesh.vertices;
        var triangles = originalMesh.triangles;
        var adjacency = BuildAdjacencyList(triangles, vertices.Length);

        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var vertexSet = new HashSet<int>(part.vertices);
            var boundaryVertices = new List<int>();

            foreach (int vertexIndex in part.vertices)
            {
                bool isBoundary = false;
                foreach (int neighborIndex in adjacency[vertexIndex])
                {
                    if (!vertexSet.Contains(neighborIndex))
                    {
                        isBoundary = true;
                        break;
                    }
                }

                if (isBoundary)
                {
                    boundaryVertices.Add(vertexIndex);
                }
            }

            var updatedPart = part;
            updatedPart.boundaryVertices = boundaryVertices;
            parts[partIndex] = updatedPart;
        }

        if (duplicateBoundaryVertices)
        {
            parts = DuplicateBoundaryVertices(parts, vertices, adjacency);
        }

        parts = ExtendPartsIntoNeighbors(parts, vertices, boundaryOverlap);
        return parts;
    }

    private Dictionary<int, List<int>> BuildAdjacencyList(int[] triangles, int vertexCount)
    {
        var adjacency = new Dictionary<int, List<int>>();

        for (int i = 0; i < vertexCount; i++)
        {
            adjacency[i] = new List<int>();
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            var v1 = triangles[i];
            var v2 = triangles[i + 1];
            var v3 = triangles[i + 2];

            adjacency[v1].Add(v2);
            adjacency[v1].Add(v3);
            adjacency[v2].Add(v1);
            adjacency[v2].Add(v3);
            adjacency[v3].Add(v1);
            adjacency[v3].Add(v2);
        }

        return adjacency;
    }

    private List<MeshPart> DuplicateBoundaryVertices(List<MeshPart> parts, Vector3[] vertices, Dictionary<int, List<int>> adjacency)
    {
        var expandedParts = new List<MeshPart>();

        foreach (var part in parts)
        {
            var expandedVertices = new HashSet<int>(part.vertices);

            foreach (int boundaryVertex in part.boundaryVertices)
            {
                foreach (int neighbor in adjacency[boundaryVertex])
                {
                    bool neighborInCurrentPart = part.vertices.Contains(neighbor);
                    if (!neighborInCurrentPart)
                    {
                        float distance = Vector3.Distance(vertices[boundaryVertex], vertices[neighbor]);
                        if (distance < seamWeldingTolerance * 10)
                        {
                            expandedVertices.Add(neighbor);
                        }
                    }
                }
            }

            var mergedPart = new MeshPart(part.name, expandedVertices.ToList(), vertices);
            expandedParts.Add(mergedPart);
        }

        return expandedParts;
    }

    private List<MeshPart> ExtendPartsIntoNeighbors(List<MeshPart> parts, Vector3[] vertices, float overlapAmount)
    {
        var extendedParts = new List<MeshPart>();

        foreach (var part in parts)
        {
            Bounds expandedBounds = part.bounds;
            expandedBounds.Expand(overlapAmount);

            var extendedVertices = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (expandedBounds.Contains(vertices[i]))
                {
                    extendedVertices.Add(i);
                }
            }

            if (extendedVertices.Count > 0)
            {
                var extendedPart = new MeshPart(part.name, extendedVertices, vertices);
                extendedParts.Add(extendedPart);
            }
        }

        return extendedParts;
    }

    private int[] SmoothBoundaryAssignments(int[] assignments, Vector3[] vertices, int[] triangles)
    {
        var adjacency = BuildAdjacencyList(triangles, vertices.Length);
        var smoothedAssignments = new int[assignments.Length];
        System.Array.Copy(assignments, smoothedAssignments, assignments.Length);

        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < assignments.Length; i++)
            {
                var neighborAssignments = new Dictionary<int, int>();

                foreach (int neighbor in adjacency[i])
                {
                    int assignment = smoothedAssignments[neighbor];
                    neighborAssignments[assignment] = neighborAssignments.ContainsKey(assignment) ?
                        neighborAssignments[assignment] + 1 : 1;
                }

                if (neighborAssignments.Count > 0)
                {
                    var mostCommon = neighborAssignments.OrderByDescending(kvp => kvp.Value).First();

                    if (mostCommon.Value >= adjacency[i].Count * 0.6f && mostCommon.Key != smoothedAssignments[i])
                    {
                        smoothedAssignments[i] = mostCommon.Key;
                    }
                }
            }
        }

        return smoothedAssignments;
    }

    private List<MeshPart> MergeSmallParts(List<MeshPart> parts, Vector3[] vertices)
    {
        var largeParts = new List<MeshPart>();
        var smallParts = new List<MeshPart>();

        foreach (var part in parts)
        {
            if (part.vertices.Count >= minVerticesPerPart)
            {
                largeParts.Add(part);
            }
            else
            {
                smallParts.Add(part);
            }
        }

        if (largeParts.Count == 0 && smallParts.Count > 0)
        {
            smallParts.Sort((a, b) => b.vertices.Count.CompareTo(a.vertices.Count));
            int partsToPromote = Mathf.Min(maxParts, smallParts.Count);
            for (int i = 0; i < partsToPromote; i++)
            {
                largeParts.Add(smallParts[i]);
            }
            smallParts.RemoveRange(0, partsToPromote);
        }

        foreach (var smallPart in smallParts)
        {
            if (largeParts.Count == 0)
            {
                if (smallPart.vertices.Count >= minVerticesPerPart / 2)
                {
                    largeParts.Add(smallPart);
                }
                continue;
            }

            MeshPart closestLargePart = largeParts[0];
            float minDistance = Vector3.Distance(smallPart.center, closestLargePart.center);

            foreach (var largePart in largeParts)
            {
                float distance = Vector3.Distance(smallPart.center, largePart.center);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestLargePart = largePart;
                }
            }

            if (minDistance <= mergeDistanceThreshold)
            {
                for (int i = 0; i < largeParts.Count; i++)
                {
                    if (Vector3.Distance(largeParts[i].center, closestLargePart.center) < 0.001f)
                    {
                        var mergedVertices = new List<int>(largeParts[i].vertices);
                        mergedVertices.AddRange(smallPart.vertices);

                        var mergedPart = new MeshPart(largeParts[i].name, mergedVertices, vertices);
                        largeParts[i] = mergedPart;
                        break;
                    }
                }
            }
            else
            {
                if (smallPart.vertices.Count >= minVerticesPerPart / 2)
                {
                    largeParts.Add(smallPart);
                }
            }
        }

        return largeParts;
    }

    private List<MeshPart> ClassifyGlassParts(List<MeshPart> parts, Vector3[] allVertices)
    {
        var classifiedParts = new List<MeshPart>();
        var nameCounts = new Dictionary<string, int>();

        foreach (var part in parts)
        {
            var newPart = part;
            if (nameCounts.ContainsKey(part.name))
            {
                nameCounts[part.name]++;
                newPart.name = $"{part.name}_{nameCounts[part.name]}";
            }
            else
            {
                nameCounts[part.name] = 1;
            }
            classifiedParts.Add(newPart);
        }

        return classifiedParts;
    }

    private Mesh CreateMeshFromVertexIndices(Mesh originalMesh, List<int> vertexIndices, string meshName)
    {
        if (vertexIndices.Count == 0) return null;

        var vertices = originalMesh.vertices;
        var triangles = originalMesh.triangles;
        var normals = originalMesh.normals;
        var uvs = originalMesh.uv;

        var vertexSet = new HashSet<int>(vertexIndices);
        var newVertexMap = new Dictionary<int, int>();
        var newVertices = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newUVs = new List<Vector2>();
        var newTriangles = new List<int>();

        foreach (var vertexIndex in vertexIndices)
        {
            newVertexMap[vertexIndex] = newVertices.Count;
            newVertices.Add(vertices[vertexIndex]);

            if (normals != null && normals.Length > vertexIndex)
                newNormals.Add(normals[vertexIndex]);

            if (uvs != null && uvs.Length > vertexIndex)
                newUVs.Add(uvs[vertexIndex]);
        }

        for (int i = 0; i < triangles.Length; i += 3)
        {
            var v1 = triangles[i];
            var v2 = triangles[i + 1];
            var v3 = triangles[i + 2];

            if (vertexSet.Contains(v1) && vertexSet.Contains(v2) && vertexSet.Contains(v3))
            {
                newTriangles.Add(newVertexMap[v1]);
                newTriangles.Add(newVertexMap[v2]);
                newTriangles.Add(newVertexMap[v3]);
            }
        }

        if (newTriangles.Count == 0) return null;

        var newMesh = new Mesh();
        newMesh.name = meshName;

        if (newVertices.Count > 65000)
        {
            newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        newMesh.vertices = newVertices.ToArray();
        newMesh.triangles = newTriangles.ToArray();

        if (newNormals.Count > 0)
            newMesh.normals = newNormals.ToArray();
        else
            newMesh.RecalculateNormals();

        if (newUVs.Count > 0)
            newMesh.uv = newUVs.ToArray();

        newMesh.RecalculateBounds();
        return newMesh;
    }

    // Public methods for external control
    public void ShowLeftArmOnly()
    {
        SetArmVisibility(leftArmParts, true);
        SetArmVisibility(rightArmParts, false);
        SetArmVisibility(alwaysVisibleObjects, true);
        SetArmVisibility(alwaysHiddenObjects, false);
    }

    public void ShowRightArmOnly()
    {
        SetArmVisibility(leftArmParts, false);
        SetArmVisibility(rightArmParts, true);
        SetArmVisibility(alwaysVisibleObjects, true);
        SetArmVisibility(alwaysHiddenObjects, false);
    }

    public void ShowBothArms()
    {
        SetArmVisibility(leftArmParts, true);
        SetArmVisibility(rightArmParts, true);
        SetArmVisibility(alwaysVisibleObjects, true);
        SetArmVisibility(alwaysHiddenObjects, false);
    }

    public void HideBothArms()
    {
        SetArmVisibility(leftArmParts, false);
        SetArmVisibility(rightArmParts, false);
        SetArmVisibility(alwaysVisibleObjects, true);
        SetArmVisibility(alwaysHiddenObjects, false);
    }

    public void SetLeftArmRotation(float angle)
    {
        leftArmRotationAngle = angle;
        if (leftArmParts.Count > 0)
        {
            if (animateRotation)
            {
                targetLeftRotation = angle;
                isAnimating = true;
            }
            else
            {
                Vector3 pivot = GetArmPivotPosition(leftArmParts, true);
                RotateArmPartsAroundPivot(leftArmParts, pivot, angle, true);
                currentLeftRotation = angle;
            }
        }
    }

    public void SetRightArmRotation(float angle)
    {
        rightArmRotationAngle = angle;
        if (rightArmParts.Count > 0)
        {
            if (animateRotation)
            {
                targetRightRotation = angle;
                isAnimating = true;
            }
            else
            {
                Vector3 pivot = GetArmPivotPosition(rightArmParts, false);
                RotateArmPartsAroundPivot(rightArmParts, pivot, angle, false);
                currentRightRotation = angle;
            }
        }
    }

    // Properties for external access
    public float LeftArmRotation
    {
        get { return currentLeftRotation; }
        set { SetLeftArmRotation(value); }
    }

    public float RightArmRotation
    {
        get { return currentRightRotation; }
        set { SetRightArmRotation(value); }
    }

    public bool IsAnimating
    {
        get { return isAnimating; }
    }
}