//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.XR.ARFoundation;

//[RequireComponent(typeof(ARFace))]
//public class FaceMeshDebugger : MonoBehaviour
//{
//    private ARFace arFace;

//    [Header("Glasses Alignment")]
//    public Transform glassesRoot;     
//    public Transform glassesNoseAnchor;
//    public int faceNoseVertexIndex = 10;

//    [Header("Debug Spheres")]
//    public GameObject debugSpherePrefab;
//    public List<int> debugIndices2 = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 ,8, 9, 10, 11, 12, 13, 14 ,15, 16 ,17 , 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34 , 35, 36 ,37, 38, 39 , 40, 41, 42, 43,44, 45, 46, 47, 48 ,49, 50, 51, 52, 53 ,54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69 , 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89 ,90 , 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105  }; // Vertices you want to visualize
//    public List<int> debugIndices = new List<int> { 9, 10, 105, 107 };
//    private List<GameObject> debugSpheres = new List<GameObject>();


//    void Awake()
//    {
//        arFace = GetComponent<ARFace>();
//    }

//    void OnEnable()
//    {
//        if (arFace == null)
//            arFace = GetComponent<ARFace>();
//    }
//    void Start()
//    {
//        if (debugSpherePrefab == null || arFace == null || !arFace.vertices.IsCreated)
//            return;

//        int total = arFace.vertices.Length;
//        int groupSize = total / 3;

//        for (int i = 0; i < total; i += 2)
//        {
//            GameObject sphere = Instantiate(debugSpherePrefab, Vector3.zero, Quaternion.identity);
//            sphere.transform.localScale = Vector3.one * 0.005f;

//            Renderer rend = sphere.GetComponent<Renderer>();
//            if (i < groupSize)
//                rend.material.color = Color.blue;
//            else if (i < 2 * groupSize)
//                rend.material.color = Color.red;
//            else
//                rend.material.color = Color.magenta;

//            debugSpheres.Add(sphere);
//        }
//    }

//    void Update()
//    {
//        if (arFace == null || !arFace.vertices.IsCreated || debugSpherePrefab == null)
//            return;

//        foreach (var sphere in debugSpheres)
//        {
//            Destroy(sphere);
//        }
//        debugSpheres.Clear();

//        if (arFace.leftEye == null || arFace.rightEye == null)
//            return;

//        Vector3 leftEyePos = arFace.leftEye.position;
//        Vector3 rightEyePos = arFace.rightEye.position;
//        Vector3 centerBetweenEyes = (leftEyePos + rightEyePos) / 2f;

//        float radius = 0.02f; 

//        for (int i = 0; i < arFace.vertices.Length; i++)
//        {
//            Vector3 worldPos = arFace.transform.TransformPoint(arFace.vertices[i]);

//            float distanceToCenter = Vector3.Distance(worldPos, centerBetweenEyes);
//            if (distanceToCenter <= radius)
//            {
//                GameObject sphere = Instantiate(debugSpherePrefab, worldPos, Quaternion.identity);
//                sphere.transform.localScale = Vector3.one * 0.005f;
//                sphere.GetComponent<Renderer>().material.color = Color.cyan;
//                debugSpheres.Add(sphere);
//            }
//        }
//    }
//}
