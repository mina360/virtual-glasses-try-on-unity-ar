using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class GlassesSeparationUtility
{
    public struct SeparationSettings
    {
        public int minVerticesPerPart;
        public bool mergeSmallParts;
        public float mergeDistanceThreshold;
        public int maxParts;
        public bool preventGaps;
        public float boundaryOverlap;
        public bool duplicateBoundaryVertices;
        public float seamWeldingTolerance;
        public bool debugCoordinates;

        public static SeparationSettings Default => new SeparationSettings
        {
            minVerticesPerPart = 10000,
            mergeSmallParts = true,
            mergeDistanceThreshold = 1.0f,
            maxParts = 10,
            preventGaps = true,
            boundaryOverlap = 0.1f,
            duplicateBoundaryVertices = true,
            seamWeldingTolerance = 0.001f,
            debugCoordinates = false
        };
    }

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

    /// <summary>
    /// Separates a glasses mesh into individual parts (lenses, arms, bridge, etc.)
    /// </summary>
    /// <param name="originalMesh">The glasses mesh to separate</param>
    /// <param name="settings">Separation settings (optional, uses defaults if null)</param>
    /// <returns>Dictionary of mesh parts with descriptive names</returns>
    public static Dictionary<string, Mesh> SeparateGlassesMesh(Mesh originalMesh, SeparationSettings? settings = null)
    {
        if (originalMesh == null)
        {
            Debug.LogError("[GlassesSeparationUtility] Original mesh is null");
            return new Dictionary<string, Mesh>();
        }

        if (originalMesh.vertices == null || originalMesh.vertices.Length == 0)
        {
            Debug.LogError("[GlassesSeparationUtility] Original mesh has no vertices");
            return new Dictionary<string, Mesh>();
        }

        if (originalMesh.triangles == null || originalMesh.triangles.Length == 0)
        {
            Debug.LogError("[GlassesSeparationUtility] Original mesh has no triangles");
            return new Dictionary<string, Mesh>();
        }

        var separationSettings = settings ?? SeparationSettings.Default;

        if (separationSettings.debugCoordinates)
        {
            Debug.Log($"[GlassesSeparationUtility] Starting separation of mesh with {originalMesh.vertexCount} vertices");
            Debug.Log($"[GlassesSeparationUtility] Mesh bounds: {originalMesh.bounds.size}");
            Debug.Log($"[GlassesSeparationUtility] Triangle count: {originalMesh.triangles.Length / 3}");
        }

        try
        {
            // Step 1: Perform spatial separation
            var spatialComponents = PerformSpatialSeparation(originalMesh, separationSettings);

            if (spatialComponents.Count == 0)
            {
                Debug.LogWarning("[GlassesSeparationUtility] No spatial components found during separation");
                return new Dictionary<string, Mesh>();
            }

            // Step 2: Prevent gaps if requested
            if (separationSettings.preventGaps)
            {
                spatialComponents = PreventGapsBetweenParts(spatialComponents, originalMesh, separationSettings);
            }

            // Step 3: Merge small parts if requested
            if (separationSettings.mergeSmallParts)
            {
                spatialComponents = MergeSmallParts(spatialComponents, originalMesh.vertices, separationSettings);
            }

            // Step 4: Classify and name parts appropriately
            var classifiedParts = ClassifyGlassParts(spatialComponents, originalMesh.vertices);

            // Step 5: Create final meshes
            var resultMeshes = new Dictionary<string, Mesh>();
            foreach (var part in classifiedParts)
            {
                var mesh = CreateMeshFromVertexIndices(originalMesh, part.vertices, part.name);
                if (mesh != null && mesh.vertexCount >= 3) // Reduced minimum threshold
                {
                    resultMeshes[part.name] = mesh;
                }
                else if (separationSettings.debugCoordinates)
                {
                    Debug.LogWarning($"[GlassesSeparationUtility] Skipped part {part.name} with {part.vertices.Count} vertices");
                }
            }

            if (separationSettings.debugCoordinates)
            {
                Debug.Log($"[GlassesSeparationUtility] Separation complete! Created {resultMeshes.Count} parts");
                foreach (var mesh in resultMeshes)
                {
                    Debug.Log($"[GlassesSeparationUtility] Part: {mesh.Key} with {mesh.Value.vertexCount} vertices");
                }
            }

            return resultMeshes;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GlassesSeparationUtility] Exception during separation: {e.Message}");
            Debug.LogError($"[GlassesSeparationUtility] Stack trace: {e.StackTrace}");
            return new Dictionary<string, Mesh>();
        }
    }

    /// <summary>
    /// Creates GameObjects from separated mesh parts with proper hierarchy
    /// </summary>
    /// <param name="separatedMeshes">Dictionary of mesh parts</param>
    /// <param name="parentName">Name for the parent GameObject</param>
    /// <param name="material">Material to apply to all parts (optional)</param>
    /// <returns>The parent GameObject containing all parts</returns>
    public static GameObject CreateGameObjectsFromSeparatedMeshes(Dictionary<string, Mesh> separatedMeshes,
        string parentName = "SeparatedGlasses", Material material = null)
    {
        var parentGO = new GameObject(parentName);

        foreach (var meshPart in separatedMeshes)
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
            meshRenderer.material = material ?? CreateDefaultMaterial(meshPart.Key);

            partGO.tag = "GlassPart";
        }

        return parentGO;
    }

    private static List<MeshPart> PerformSpatialSeparation(Mesh mesh, SeparationSettings settings)
    {
        var vertices = mesh.vertices;
        var bounds = mesh.bounds;
        var assignments = new int[vertices.Length];

        // Initialize all as unassigned
        for (int i = 0; i < assignments.Length; i++)
        {
            assignments[i] = -1;
        }

        // Classify vertices based on spatial position
        for (int i = 0; i < vertices.Length; i++)
        {
            assignments[i] = ClassifyVertexPosition(vertices[i], bounds);
        }

        // Smooth boundaries to prevent harsh cutoffs
        if (settings.preventGaps)
        {
            assignments = SmoothBoundaryAssignments(assignments, vertices, mesh.triangles, settings);
        }

        // Group vertices by assignment
        var regionGroups = new Dictionary<int, List<int>>();
        for (int i = 0; i < assignments.Length; i++)
        {
            int region = assignments[i];
            if (!regionGroups.ContainsKey(region))
                regionGroups[region] = new List<int>();
            regionGroups[region].Add(i);
        }

        // Create mesh parts
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
                parts.Add(new MeshPart(name, kvp.Value, vertices));
            }
        }

        return parts;
    }

    private static int ClassifyVertexPosition(Vector3 position, Bounds bounds)
    {
        var relativePos = position - bounds.center;
        float relX = relativePos.x / bounds.size.x;
        float relY = relativePos.y / bounds.size.y;
        float relZ = relativePos.z / bounds.size.z;

        // Classification logic for glasses parts
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

    private static int[] SmoothBoundaryAssignments(int[] assignments, Vector3[] vertices,
        int[] triangles, SeparationSettings settings)
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

    private static Dictionary<int, List<int>> BuildAdjacencyList(int[] triangles, int vertexCount)
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

    private static List<MeshPart> PreventGapsBetweenParts(List<MeshPart> parts, Mesh originalMesh,
        SeparationSettings settings)
    {
        var vertices = originalMesh.vertices;
        var triangles = originalMesh.triangles;
        var adjacency = BuildAdjacencyList(triangles, vertices.Length);

        // Add boundary vertex detection and gap prevention logic
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

        return parts;
    }

    private static List<MeshPart> MergeSmallParts(List<MeshPart> parts, Vector3[] vertices,
        SeparationSettings settings)
    {
        var largeParts = new List<MeshPart>();
        var smallParts = new List<MeshPart>();

        foreach (var part in parts)
        {
            if (part.vertices.Count >= settings.minVerticesPerPart)
            {
                largeParts.Add(part);
            }
            else
            {
                smallParts.Add(part);
            }
        }

        // Merge small parts into nearby large parts
        foreach (var smallPart in smallParts)
        {
            if (largeParts.Count == 0)
            {
                if (smallPart.vertices.Count >= settings.minVerticesPerPart / 2)
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

            if (minDistance <= settings.mergeDistanceThreshold)
            {
                // Merge with closest large part
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
        }

        return largeParts;
    }

    private static List<MeshPart> ClassifyGlassParts(List<MeshPart> parts, Vector3[] allVertices)
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

    private static Mesh CreateMeshFromVertexIndices(Mesh originalMesh, List<int> vertexIndices, string meshName)
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

    private static Material CreateDefaultMaterial(string partName)
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
}