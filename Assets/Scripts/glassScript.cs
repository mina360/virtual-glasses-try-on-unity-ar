using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using GLTFast;

public class glassScript : MonoBehaviour
{
    [Header("Model URL Settings")]
    public string defaultModelUrl = "http://10.188.153.107:8000/storage/models/model.glb";

    [Header("Output Settings")]
    public bool createGameObjects = true;

    [Header("Auto-Detection Settings")]
    [SerializeField] public int minVerticesPerPart = 10000;
    [SerializeField] public bool mergeSmallParts = true;
    [SerializeField] public float mergeDistanceThreshold = 1.0f;
    [SerializeField] public int maxParts = 10;

    [Header("Gap Prevention Settings")]
    [SerializeField] public bool preventGaps = true;
    [SerializeField] public float boundaryOverlap = 0.1f;
    [SerializeField] public bool duplicateBoundaryVertices = true;
    [SerializeField] public float seamWeldingTolerance = 0.001f;

    [Header("Arm Rotation Settings")]
    [SerializeField] public bool enableArmRotation = true;
    [SerializeField] public float leftArmRotationAngle = 20f;
    [SerializeField] public float rightArmRotationAngle = -20f;

    [Header("Coordinate System Fix")]
    [SerializeField] public bool useLocalCoordinates = true;
    [SerializeField] public bool debugCoordinates = false;
    [SerializeField] public bool autoDetectOrientation = true;

    private GameObject loadedModel;
    private GameObject separatedGlassesParent;
    private MeshFilter loadedMeshFilter;
    private GltfImport importer;

    private Material[] extractedMaterials;

    private List<GameObject> leftArmParts = new List<GameObject>();
    private List<GameObject> rightArmParts = new List<GameObject>();

    private bool modelReady = false;

    private Dictionary<int, HashSet<int>> cachedAdjacency;

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

    async void Start()
    {
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
                Debug.Log("[URLGlassScript] URL received from Flutter: " + modelUrl);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("[URLGlassScript] Failed to get URL from intent: " + e.Message);
        }
#endif

        if (!string.IsNullOrEmpty(modelUrl))
            await DownloadAndSeparateModel(modelUrl);
        else
            Debug.LogError("[URLGlassScript] Model URL empty.");
    }

    async Task DownloadAndSeparateModel(string url)
    {
        Debug.Log("[URLGlassScript] Starting download from: " + url);

        if (loadedModel != null)
        {
            Destroy(loadedModel);
            loadedModel = null;
        }
        if (separatedGlassesParent != null)
        {
            Destroy(separatedGlassesParent);
            separatedGlassesParent = null;
        }
        modelReady = false;

        UnityWebRequest www = UnityWebRequest.Get(url);

        var operation = www.SendWebRequest();
        await operation;

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[URLGlassScript] Download failed: " + www.error);
            www.Dispose();
            return;
        }

        byte[] data = www.downloadHandler.data;
        www.Dispose();

        Debug.Log($"[URLGlassScript] Downloaded {data.Length} bytes");

        importer = new GltfImport();
        bool ok = await importer.LoadGltfBinary(data);
        if (!ok)
        {
            Debug.LogError("[URLGlassScript] Failed to parse GLB data");
            return;
        }

        GameObject modelParent = new GameObject("RawLoadedGlassModel");
        modelParent.transform.position = Vector3.zero;
        modelParent.transform.rotation = Quaternion.identity;
        modelParent.transform.localScale = Vector3.one;

        var instantiationResult = await importer.InstantiateMainSceneAsync(modelParent.transform);

        if (instantiationResult)
        {
            loadedModel = modelParent;
            loadedMeshFilter = loadedModel.GetComponentInChildren<MeshFilter>();

            ExtractMaterials();

            if (loadedMeshFilter == null)
            {
                Debug.LogError("[URLGlassScript] No MeshFilter found in loaded model!");
                Destroy(loadedModel);
                return;
            }

            Debug.Log("[URLGlassScript] Model successfully loaded. Starting separation...");

            AutoSeparateLoadedGlasses();
            modelReady = true;
        }
        else
        {
            Debug.LogError("[URLGlassScript] Failed to instantiate model");
            Destroy(modelParent);
        }
    }

    private void ExtractMaterials()
    {
        var renderers = loadedModel.GetComponentsInChildren<MeshRenderer>();
        var materialsList = new List<Material>();

        foreach (var renderer in renderers)
        {
            if (renderer.materials != null && renderer.materials.Length > 0)
            {
                materialsList.AddRange(renderer.materials);
            }
        }

        extractedMaterials = materialsList.ToArray();
        Debug.Log($"[URLGlassScript] Extracted {extractedMaterials.Length} materials from loaded model");
    }

    [ContextMenu("Auto Separate Loaded Glasses")]
    public void AutoSeparateLoadedGlasses()
    {
        if (loadedMeshFilter == null || loadedMeshFilter.mesh == null)
        {
            Debug.LogError("[URLGlassScript] No loaded glasses mesh available!");
            return;
        }

        Debug.Log($"[URLGlassScript] Starting automatic separation of loaded mesh with {loadedMeshFilter.mesh.vertexCount} vertices...");

        var separatedMeshes = AutoSeparateGlassesMesh(loadedMeshFilter.mesh);

        if (createGameObjects)
        {
            separatedGlassesParent = CreateGameObjectsFromMeshes(separatedMeshes);

            separatedGlassesParent.transform.position = Vector3.zero;
            separatedGlassesParent.transform.rotation = Quaternion.identity;
            separatedGlassesParent.transform.localScale = Vector3.one;
            separatedGlassesParent.SetActive(false);

            StoreArmReferences(separatedGlassesParent);

            if (enableArmRotation)
            {
                ApplyImmediateArmRotation();
            }
        }

        Debug.Log($"[URLGlassScript] Automatic separation complete! Created {separatedMeshes.Count} parts.");
    }

    public bool HasLoadedModel()
    {
        return modelReady && separatedGlassesParent != null;
    }

    public GameObject GetLoadedModelParent()
    {
        return separatedGlassesParent;
    }

    public void HideOriginalModel()
    {
        if (loadedModel != null)
        {
            loadedModel.SetActive(false);
        }
    }

    private Quaternion DetectGlassesOrientation()
    {
        if (loadedMeshFilter == null) return Quaternion.identity;

        var mesh = loadedMeshFilter.mesh;
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
        else if (size.y == maxDimension && size.z == size.x)
        {
            correctionRotation = Quaternion.Euler(0, 0, 90);
        }

        return correctionRotation;
    }

    private Dictionary<int, HashSet<int>> BuildAdjacencyListOptimized(int[] triangles, int vertexCount)
    {
        if (cachedAdjacency != null && cachedAdjacency.Count == vertexCount)
        {
            return cachedAdjacency;
        }

        var adjacency = new Dictionary<int, HashSet<int>>(vertexCount);

        for (int i = 0; i < vertexCount; i++)
        {
            adjacency[i] = new HashSet<int>();
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

        cachedAdjacency = adjacency;
        return adjacency;
    }
    private List<MeshPart> PreventGapsBetweenParts(List<MeshPart> parts, Mesh originalMesh)
    {
        var vertices = originalMesh.vertices;
        var triangles = originalMesh.triangles;
        var adjacency = BuildAdjacencyListOptimized(triangles, vertices.Length);

        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var vertexSet = new HashSet<int>(part.vertices);
            var boundaryVertices = new List<int>();

            foreach (int vertexIndex in part.vertices)
            {
                if (adjacency[vertexIndex].Any(neighborIndex => !vertexSet.Contains(neighborIndex)))
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
            parts = DuplicateBoundaryVerticesOptimized(parts, vertices, adjacency);
        }

        parts = ExtendPartsIntoNeighborsOptimized(parts, vertices, boundaryOverlap);
        return parts;
    }

    private List<MeshPart> DuplicateBoundaryVerticesOptimized(List<MeshPart> parts, Vector3[] vertices, Dictionary<int, HashSet<int>> adjacency)
    {
        var expandedParts = new List<MeshPart>();
        float expandedTolerance = seamWeldingTolerance * 10;

        foreach (var part in parts)
        {
            var expandedVertices = new HashSet<int>(part.vertices);

            foreach (int boundaryVertex in part.boundaryVertices)
            {
                Vector3 boundaryPos = vertices[boundaryVertex];

                foreach (int neighbor in adjacency[boundaryVertex])
                {
                    if (!part.vertices.Contains(neighbor))
                    {
                        if ((vertices[neighbor] - boundaryPos).sqrMagnitude < expandedTolerance * expandedTolerance)
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

    private List<MeshPart> ExtendPartsIntoNeighborsOptimized(List<MeshPart> parts, Vector3[] vertices, float overlapAmount)
    {
        var extendedParts = new List<MeshPart>();

        foreach (var part in parts)
        {
            Bounds expandedBounds = part.bounds;
            expandedBounds.Expand(overlapAmount);

            var extendedVertices = new List<int>();

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                if (vertex.x >= expandedBounds.min.x && vertex.x <= expandedBounds.max.x &&
                    vertex.y >= expandedBounds.min.y && vertex.y <= expandedBounds.max.y &&
                    vertex.z >= expandedBounds.min.z && vertex.z <= expandedBounds.max.z)
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

    public Dictionary<string, Mesh> AutoSeparateGlassesMesh(Mesh originalMesh)
    {
        var spatialComponents = GlassesSpatialSeparationOptimized(originalMesh);
        Debug.Log($"[URLGlassScript] Applied glasses-specific spatial separation, found {spatialComponents.Count} parts");

        if (preventGaps)
        {
            spatialComponents = PreventGapsBetweenParts(spatialComponents, originalMesh);
            Debug.Log("[URLGlassScript] Applied gap prevention techniques");
        }

        if (mergeSmallParts)
        {
            Vector3[] worldVertices = TransformVertices(originalMesh.vertices, loadedMeshFilter.transform);
            spatialComponents = MergeSmallPartsOptimized(spatialComponents, worldVertices);
            Debug.Log($"[URLGlassScript] After merging small parts: {spatialComponents.Count} parts");
        }

        var classifiedParts = ClassifyGlassPartsOptimized(spatialComponents);
        var resultMeshes = new Dictionary<string, Mesh>();

        foreach (var part in classifiedParts)
        {
            var mesh = CreateMeshFromVertexIndicesOptimized(originalMesh, part.vertices, part.name);
            if (mesh != null && mesh.vertexCount >= 25)
            {
                resultMeshes[part.name] = mesh;
            }
        }

        return resultMeshes;
    }

    private List<MeshPart> MergeSmallPartsOptimized(List<MeshPart> parts, Vector3[] vertices)
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

        float sqrThreshold = mergeDistanceThreshold * mergeDistanceThreshold;

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
            float minSqrDistance = (smallPart.center - closestLargePart.center).sqrMagnitude;

            foreach (var largePart in largeParts)
            {
                float sqrDistance = (smallPart.center - largePart.center).sqrMagnitude;
                if (sqrDistance < minSqrDistance)
                {
                    minSqrDistance = sqrDistance;
                    closestLargePart = largePart;
                }
            }

            if (minSqrDistance <= sqrThreshold)
            {
                for (int i = 0; i < largeParts.Count; i++)
                {
                    if ((largeParts[i].center - closestLargePart.center).sqrMagnitude < 0.001f)
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
    private List<MeshPart> ClassifyGlassPartsOptimized(List<MeshPart> parts)
    {
        var classifiedParts = new List<MeshPart>(parts.Count);
        var nameCounts = new Dictionary<string, int>();

        foreach (var part in parts)
        {
            var newPart = part;
            if (nameCounts.TryGetValue(part.name, out int count))
            {
                nameCounts[part.name] = count + 1;
                newPart.name = $"{part.name}_{count + 1}";
            }
            else
            {
                nameCounts[part.name] = 1;
            }
            classifiedParts.Add(newPart);
        }

        return classifiedParts;
    }

    private Mesh CreateMeshFromVertexIndicesOptimized(Mesh originalMesh, List<int> vertexIndices, string meshName)
    {
        if (vertexIndices.Count == 0) return null;

        var vertices = originalMesh.vertices;
        var triangles = originalMesh.triangles;
        var normals = originalMesh.normals;
        var uvs = originalMesh.uv;

        var vertexSet = new HashSet<int>(vertexIndices);
        var newVertexMap = new Dictionary<int, int>(vertexIndices.Count);
        var newVertices = new List<Vector3>(vertexIndices.Count);
        var newNormals = new List<Vector3>(vertexIndices.Count);
        var newUVs = new List<Vector2>(vertexIndices.Count);
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

    private Vector3[] TransformVertices(Vector3[] localVertices, Transform meshTransform)
    {
        if (!useLocalCoordinates || meshTransform == null)
        {
            return localVertices;
        }

        Vector3[] transformedVertices = new Vector3[localVertices.Length];
        Matrix4x4 transformMatrix = meshTransform.localToWorldMatrix;

        for (int i = 0; i < localVertices.Length; i++)
        {
            transformedVertices[i] = transformMatrix.MultiplyPoint3x4(localVertices[i]);
        }

        return transformedVertices;
    }

    private Vector3[] GetNormalizedLocalVertices(Mesh originalMesh, Transform meshTransform)
    {
        var vertices = originalMesh.vertices;

        if (!useLocalCoordinates || meshTransform == null)
        {
            return vertices;
        }

        Vector3[] normalizedVertices = new Vector3[vertices.Length];
        Quaternion currentRotation = meshTransform.rotation;
        Quaternion inverseRotation = Quaternion.Inverse(currentRotation);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = meshTransform.TransformPoint(vertices[i]);
            Vector3 meshCenter = meshTransform.TransformPoint(originalMesh.bounds.center);
            Vector3 relativeToCenter = worldVertex - meshCenter;
            Vector3 normalizedRelative = inverseRotation * relativeToCenter;

            normalizedVertices[i] = normalizedRelative + originalMesh.bounds.center;
        }

        return normalizedVertices;
    }

    private int[] SmoothBoundaryAssignments(int[] assignments, Vector3[] vertices, int[] triangles)
    {
        var adjacency = BuildAdjacencyListOptimized(triangles, vertices.Length);
        var smoothedAssignments = new int[assignments.Length];
        System.Array.Copy(assignments, smoothedAssignments, assignments.Length);

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

        return smoothedAssignments;
    }

    private List<MeshPart> GlassesSpatialSeparationOptimized(Mesh mesh)
    {
        var vertices = GetNormalizedLocalVertices(mesh, loadedMeshFilter.transform);

        var bounds = new Bounds();
        if (vertices.Length > 0)
        {
            bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                bounds.Encapsulate(vertex);
            }
        }

        var assignments = new int[vertices.Length];

        Vector3 boundsCenter = bounds.center;
        Vector3 boundsSize = bounds.size;

        for (int i = 0; i < vertices.Length; i++)
        {
            assignments[i] = ClassifyPositionOptimized(vertices[i], boundsCenter, boundsSize);
        }

        if (preventGaps)
        {
            assignments = SmoothBoundaryAssignments(assignments, vertices, mesh.triangles);
        }

        var regionGroups = new Dictionary<int, List<int>>();
        for (int i = 0; i < assignments.Length; i++)
        {
            int region = assignments[i];
            if (!regionGroups.TryGetValue(region, out List<int> group))
            {
                group = new List<int>();
                regionGroups[region] = group;
            }
            group.Add(i);
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
    private int ClassifyPositionOptimized(Vector3 position, Vector3 boundsCenter, Vector3 boundsSize)
    {
        var relativePos = position - boundsCenter;
        float relX = relativePos.x / boundsSize.x;
        float relY = relativePos.y / boundsSize.y;
        float relZ = relativePos.z / boundsSize.z;

        if (relZ > 0.35f)
        {
            if (relX < -0.2f) return 0;
            else if (relX > 0.2f) return 1;
            else return 2;
        }
        else if (relZ < -0.25f) 
        {
            if (relX < -0.15f) return 3;
            else if (relX > 0.15f) return 4;
            else return 5; 
        }
        else
        {
            if (relZ > 0.05f) 
            {
                if (relX < -0.25f) return 6;
                else if (relX > 0.25f) return 7; 
                else return 8; 
            }
            else 
            {
                if (relX < -0.2f) return 9;
                else if (relX > 0.2f) return 10;
                else return 11; 
            }
        }
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

    private void StoreArmReferences(GameObject parentGO)
    {
        leftArmParts.Clear();
        rightArmParts.Clear();

        Transform[] children = parentGO.GetComponentsInChildren<Transform>();

        foreach (Transform child in children)
        {
            string name = child.name.ToLower();

            if (name.Contains("left") && (name.Contains("arm") || name.Contains("frame")))
            {
                leftArmParts.Add(child.gameObject);
            }
            else if (name.Contains("right") && (name.Contains("arm") || name.Contains("frame")))
            {
                rightArmParts.Add(child.gameObject);
            }
        }
    }

    private void ApplyImmediateArmRotation()
    {
        if (leftArmParts.Count > 0)
        {
            Vector3 leftPivot = GetArmPivotPosition(leftArmParts, true);
            RotateArmPartsAroundPivot(leftArmParts, leftPivot, leftArmRotationAngle, true);
        }

        if (rightArmParts.Count > 0)
        {
            Vector3 rightPivot = GetArmPivotPosition(rightArmParts, false);
            RotateArmPartsAroundPivot(rightArmParts, rightPivot, rightArmRotationAngle, false);
        }
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
                    if (isLeftArm)
                    {
                        pivotPoint = new Vector3(bounds.max.x, bounds.center.y, bounds.max.z);
                    }
                    else
                    {
                        pivotPoint = new Vector3(bounds.min.x, bounds.center.y, bounds.max.z);
                    }
                }
            }
        }

        return pivotPoint;
    }

    private void RotateArmPartsAroundPivot(List<GameObject> armParts, Vector3 pivotPoint, float angle, bool isLeftArm)
    {
        foreach (GameObject part in armParts)
        {
            part.transform.localRotation = Quaternion.identity;
        }

        Vector3 rotationAxis = Vector3.up;

        foreach (GameObject part in armParts)
        {
            part.transform.RotateAround(pivotPoint, rotationAxis, angle);
        }
    }
    private GameObject CreateGameObjectsFromMeshes(Dictionary<string, Mesh> meshParts)
    {
        var parentGO = new GameObject("AutoSeparated_URLGlasses");

        parentGO.transform.position = Vector3.zero;
        parentGO.transform.rotation = Quaternion.identity;
        parentGO.transform.localScale = Vector3.one;
        parentGO.SetActive(false);

        Material[] materialsToUse = extractedMaterials != null && extractedMaterials.Length > 0
            ? extractedMaterials
            : null;

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

            if (materialsToUse != null)
            {
                meshRenderer.materials = materialsToUse;
            }
            else
            {
                meshRenderer.material = CreateDefaultMaterial(meshPart.Key);
            }

            partGO.tag = "GlassPart";
        }

        CenterOnBridge(parentGO);
        return parentGO;
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

    void OnDestroy()
    {
        if (importer != null)
        {
            importer.Dispose();
            importer = null;
        }

        if (loadedModel != null)
        {
            Destroy(loadedModel);
            loadedModel = null;
        }
        if (separatedGlassesParent != null)
        {
            Destroy(separatedGlassesParent);
            separatedGlassesParent = null;
        }

        cachedAdjacency = null;
    }

    public void SetLeftArmRotation(float angle)
    {
        leftArmRotationAngle = angle;
        if (leftArmParts.Count > 0)
        {
            Vector3 pivot = GetArmPivotPosition(leftArmParts, true);
            RotateArmPartsAroundPivot(leftArmParts, pivot, angle, true);
        }
    }

    public void SetRightArmRotation(float angle)
    {
        rightArmRotationAngle = angle;
        if (rightArmParts.Count > 0)
        {
            Vector3 pivot = GetArmPivotPosition(rightArmParts, false);
            RotateArmPartsAroundPivot(rightArmParts, pivot, angle, false);
        }
    }

    public float LeftArmRotation
    {
        get { return leftArmRotationAngle; }
        set { SetLeftArmRotation(value); }
    }

    public float RightArmRotation
    {
        get { return rightArmRotationAngle; }
        set { SetRightArmRotation(value); }
    }
}
