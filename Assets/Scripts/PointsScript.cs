using UnityEngine;
using UnityEngine.XR.ARFoundation;
using GLTFast;
using System.Collections.Generic;

[RequireComponent(typeof(ARFace))]
public class PointsScript : MonoBehaviour
{
    private ARFace arFace;

    private Transform dynamicModelInstance;
    private glassScript dynamicGlassesController;
    private GltfImport importer;

    [Header("Placement on Face")]
    [Tooltip("ARFace vertex index used for anchoring (168 = nose bridge middle)")]
    public int anchorVertexIndex = 168;

    [Tooltip("Local rotation offset for the loaded model")]
    public Vector3 rotationOffsetEuler;

    [Tooltip("Local position offset for the loaded model")]
    public Vector3 positionOffset;

    [Header("Arm Connection Scaling")]
    [Tooltip("Z-axis scale factor for LeftArmConnection and RightArmConnection parts")]
    public float armConnectionZScale = 1.2f;

    [Header("Tilt Detection Settings")]
    [Tooltip("Minimum tilt angle in degrees to trigger detection")]
    public float tiltThreshold = 5f;

    [Tooltip("Time in seconds to wait before detecting another tilt")]
    public float tiltCooldown = 0.3f;

    [Tooltip("Enable debug logs for tilt detection")]
    public bool debugTiltDetection = true;

    [Header("Arm Visibility Control")]
    [Tooltip("Enable automatic arm hiding based on face rotation")]
    public bool enableArmVisibilityControl = true;

    [Tooltip("Minimum tilt angle to hide arms (should be larger than tiltThreshold)")]
    public float armVisibilityTiltThreshold = 15f;

    private Vector3 previousRotation;
    private float lastTiltTime;
    private bool isInitialized = false;

    private int frameCounter = 0;

    private List<GameObject> dynamicLeftArmObjects = new List<GameObject>();
    private List<GameObject> dynamicRightArmObjects = new List<GameObject>();
    private List<GameObject> dynamicAlwaysVisibleObjects = new List<GameObject>();
    private List<GameObject> dynamicAlwaysHiddenObjects = new List<GameObject>();

    private List<Transform> armConnectionObjects = new List<Transform>();
    private List<Vector3> originalArmConnectionScales = new List<Vector3>();

    void Awake()
    {
        arFace = GetComponent<ARFace>();
    }

    void Start()
    {
        InitializeDynamicModel();
    }

    void Update()
    {
        PositionObjects();
        DetectFaceTilt();
        UpdateArmConnectionScaling();
    }

    private void InitializeDynamicModel()
    {
        dynamicGlassesController = FindObjectOfType<glassScript>();

        if (dynamicGlassesController == null)
        {
            Debug.LogWarning("[PointsScript] No glassScript found in scene for dynamic model positioning");
            return;
        }

        StartCoroutine(WaitForDynamicModelAndSetup());
    }

    private System.Collections.IEnumerator WaitForDynamicModelAndSetup()
    {
        while (dynamicGlassesController == null || !dynamicGlassesController.HasLoadedModel())
        {
            yield return new WaitForSeconds(0.1f);
        }

        GameObject loadedModel = dynamicGlassesController.GetLoadedModelParent();

        if (loadedModel != null)
        {
            loadedModel.transform.SetParent(arFace.transform);
            dynamicModelInstance = loadedModel.transform;

            dynamicModelInstance.localScale = Vector3.one * 0.07f;

            dynamicModelInstance.gameObject.SetActive(true);

            dynamicGlassesController.HideOriginalModel();

            if (enableArmVisibilityControl)
            {
                SetupDynamicArmVisibilityControl(loadedModel);
            }

            SetupArmConnectionScaling(loadedModel);

            Debug.Log("[PointsScript] Dynamic model successfully positioned on face");
        }
    }

    void PositionObjects()
    {
        if (!HasVertex(anchorVertexIndex)) return;

        Vector3 localAnchor = arFace.vertices[anchorVertexIndex];

        if (dynamicModelInstance != null)
        {
            dynamicModelInstance.localPosition = localAnchor + positionOffset;
            dynamicModelInstance.localRotation = Quaternion.Euler(rotationOffsetEuler);
        }
    }

    void DetectFaceTilt()
    {
        if (arFace == null || arFace.transform == null) return;

        if (arFace.trackingState != UnityEngine.XR.ARSubsystems.TrackingState.Tracking)
        {
            if (debugTiltDetection && frameCounter % 60 == 0)
                frameCounter++;
            return;
        }

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
            if (debugTiltDetection)
                return;
        }

        float timeSinceLastTilt = Time.time - lastTiltTime;
        if (timeSinceLastTilt < tiltCooldown)
        {
            return;
        }

        Vector3 rotationDiff = GetRotationDifference(currentRotation, previousRotation);

        if (enableArmVisibilityControl)
        {
            HandleArmVisibilityForModel("", rotationDiff, dynamicLeftArmObjects, dynamicRightArmObjects,
                                       dynamicAlwaysVisibleObjects, dynamicAlwaysHiddenObjects);
        }

        bool tiltDetected = false;
        string tiltDirection = "";

        if (Mathf.Abs(rotationDiff.z) > tiltThreshold)
        {
            tiltDirection += rotationDiff.z > 0 ? "Right " : "Left ";
            tiltDetected = true;
        }

        if (Mathf.Abs(rotationDiff.x) > tiltThreshold)
        {
            tiltDirection += rotationDiff.x > 0 ? "Down " : "Up ";
            tiltDetected = true;
        }

        if (Mathf.Abs(rotationDiff.y) > tiltThreshold)
        {
            tiltDirection += rotationDiff.y > 0 ? "Turn-Right " : "Turn-Left ";
            tiltDetected = true;
        }

        if (tiltDetected)
        {
            Debug.Log($"[Face Tilt Detected] Person tilted their face: {tiltDirection.Trim()} (Diff: X:{rotationDiff.x:F1}°, Y:{rotationDiff.y:F1}°, Z:{rotationDiff.z:F1}°)");
            lastTiltTime = Time.time;

            if (enableArmVisibilityControl)
            {
                HandleArmVisibilityForModel(tiltDirection, rotationDiff, dynamicLeftArmObjects, dynamicRightArmObjects,
                                           dynamicAlwaysVisibleObjects, dynamicAlwaysHiddenObjects);
            }

            previousRotation = currentRotation;
        }
        else if (debugTiltDetection)
        {
            previousRotation = Vector3.Lerp(previousRotation, currentRotation, 0.1f);
        }
    }

    Vector3 GetRotationDifference(Vector3 current, Vector3 previous)
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

    void OnDestroy()
    {
        if (importer != null)
        {
            importer.Dispose();
        }
    }

    #region Arm Connection Scaling

    private void SetupArmConnectionScaling(GameObject glassesGameObject)
    {
        armConnectionObjects.Clear();
        originalArmConnectionScales.Clear();

        Transform[] allChildren = glassesGameObject.GetComponentsInChildren<Transform>();

        foreach (Transform child in allChildren)
        {
            string name = child.name;

            if (name == "LeftArmConnection" || name == "RightArmConnection")
            {
                armConnectionObjects.Add(child);
                originalArmConnectionScales.Add(child.localScale);

                Debug.Log($"[Arm Connection Scaling] Found {name} - Original scale: {child.localScale}");
            }
        }

        ApplyArmConnectionScaling();

        Debug.Log($"[Arm Connection Scaling] Setup complete - Found {armConnectionObjects.Count} arm connection objects");
    }

    private void UpdateArmConnectionScaling()
    {
        //
    }

    private void ApplyArmConnectionScaling()
    {
        for (int i = 0; i < armConnectionObjects.Count; i++)
        {
            if (armConnectionObjects[i] != null && i < originalArmConnectionScales.Count)
            {
                Vector3 newScale = originalArmConnectionScales[i];
                newScale.z = originalArmConnectionScales[i].z * armConnectionZScale;
                armConnectionObjects[i].localScale = newScale;

                Debug.Log($"[Arm Connection Scaling] Applied Z-scale {armConnectionZScale} to {armConnectionObjects[i].name}");
            }
        }
    }

    public void UpdateArmConnectionZScale(float newZScale)
    {
        armConnectionZScale = newZScale;
        ApplyArmConnectionScaling();
    }

    public void ResetArmConnectionScaling()
    {
        for (int i = 0; i < armConnectionObjects.Count; i++)
        {
            if (armConnectionObjects[i] != null && i < originalArmConnectionScales.Count)
            {
                armConnectionObjects[i].localScale = originalArmConnectionScales[i];
            }
        }
        Debug.Log("[Arm Connection Scaling] Reset to original scales");
    }

    #endregion

    #region Arm Visibility Control

    private void SetupDynamicArmVisibilityControl(GameObject glassesGameObject)
    {
        FindArmPartsByName(glassesGameObject, dynamicLeftArmObjects, dynamicRightArmObjects, dynamicAlwaysVisibleObjects, dynamicAlwaysHiddenObjects);

        SetArmVisibility(dynamicAlwaysHiddenObjects, false);

        Debug.Log($"[Arm Control] Dynamic Model - Found {dynamicLeftArmObjects.Count} left arm parts, {dynamicRightArmObjects.Count} right arm parts");
    }

    private void FindArmPartsByName(GameObject parent, List<GameObject> leftArms, List<GameObject> rightArms,
                                   List<GameObject> alwaysVisible, List<GameObject> alwaysHidden)
    {
        leftArms.Clear();
        rightArms.Clear();
        alwaysVisible.Clear();
        alwaysHidden.Clear();

        Transform[] allChildren = parent.GetComponentsInChildren<Transform>();

        foreach (Transform child in allChildren)
        {
            string name = child.name.ToLower();

            if (name == "leftarm" || name == "rightarm")
            {
                alwaysHidden.Add(child.gameObject);
                continue;
            }

            if (name.Contains("lensframe") || name.Contains("lens"))
            {
                alwaysVisible.Add(child.gameObject);
                continue;
            }

            if (name.Contains("left") && (name.Contains("arm") || name.Contains("frame") || name.Contains("connection")) && name != "leftarm")
            {
                leftArms.Add(child.gameObject);
            }
            else if (name.Contains("right") && (name.Contains("arm") || name.Contains("frame") || name.Contains("connection")) && name != "rightarm")
            {
                rightArms.Add(child.gameObject);
            }
        }

        if (leftArms.Count == 0 && rightArms.Count == 0)
        {
            foreach (Transform child in allChildren)
            {
                string name = child.name.ToLower();

                if (name.Contains("lensframe") || name.Contains("lens") ||
                    alwaysHidden.Contains(child.gameObject) ||
                    name == "leftarm" || name == "rightarm")
                    continue;

                if (name.Contains("left"))
                {
                    leftArms.Add(child.gameObject);
                }
                else if (name.Contains("right"))
                {
                    rightArms.Add(child.gameObject);
                }
            }
        }
    }

    private void HandleArmVisibilityForModel(string tiltDirection, Vector3 rotationDiff,
                                       List<GameObject> leftArms, List<GameObject> rightArms,
                                       List<GameObject> alwaysVisible, List<GameObject> alwaysHidden)
    {
        Vector3 currentRotation = arFace.transform.eulerAngles;

        float normalizedY = currentRotation.y;
        if (normalizedY > 180f) normalizedY -= 360f;

        if (Mathf.Abs(normalizedY) > tiltThreshold)
        {
            if (normalizedY > 0)
            {
                SetArmVisibility(leftArms, true);
                SetArmVisibility(rightArms, false);
                Debug.Log($"[Arm Control] Face turning right ({normalizedY:F1}°) - showing LEFT arm only");
            }
            else 
            {
                SetArmVisibility(leftArms, false);
                SetArmVisibility(rightArms, true);
                Debug.Log($"[Arm Control] Face turning left ({normalizedY:F1}°) - showing RIGHT arm only");
            }
        }
        else
        {
            SetArmVisibility(leftArms, true);
            SetArmVisibility(rightArms, true);
            Debug.Log($"[Arm Control] No tilt ({normalizedY:F1}°) - showing BOTH arms");
        }

        SetArmVisibility(alwaysVisible, true);
        SetArmVisibility(alwaysHidden, false);
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

    public void ShowLeftArmOnly()
    {
        SetArmVisibility(dynamicLeftArmObjects, true);
        SetArmVisibility(dynamicRightArmObjects, false);
        SetArmVisibility(dynamicAlwaysVisibleObjects, true);
        SetArmVisibility(dynamicAlwaysHiddenObjects, false);

        Debug.Log("[Arm Control] Manual: Showing LEFT arm only for dynamic model");
    }

    public void ShowRightArmOnly()
    {
        SetArmVisibility(dynamicLeftArmObjects, false);
        SetArmVisibility(dynamicRightArmObjects, true);
        SetArmVisibility(dynamicAlwaysVisibleObjects, true);
        SetArmVisibility(dynamicAlwaysHiddenObjects, false);

        Debug.Log("[Arm Control] Manual: Showing RIGHT arm only for dynamic model");
    }

    public void ShowBothArms()
    {
        SetArmVisibility(dynamicLeftArmObjects, true);
        SetArmVisibility(dynamicRightArmObjects, true);
        SetArmVisibility(dynamicAlwaysVisibleObjects, true);
        SetArmVisibility(dynamicAlwaysHiddenObjects, false);

        Debug.Log("[Arm Control] Manual: Showing BOTH arms for dynamic model");
    }

    public void HideBothArms()
    {
        SetArmVisibility(dynamicLeftArmObjects, false);
        SetArmVisibility(dynamicRightArmObjects, false);
        SetArmVisibility(dynamicAlwaysVisibleObjects, true);
        SetArmVisibility(dynamicAlwaysHiddenObjects, false);

        Debug.Log("[Arm Control] Manual: Hiding BOTH arms for dynamic model");
    }

    #endregion

    #region Inspector Validation

    void OnValidate()
    {
        if (Application.isPlaying && armConnectionObjects.Count > 0)
        {
            ApplyArmConnectionScaling();
        }
    }

    #endregion
}