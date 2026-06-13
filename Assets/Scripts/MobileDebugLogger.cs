using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MobileDebugLogger : MonoBehaviour
{
    [Header("Debug UI")]
    public Text debugText;
    public ScrollRect scrollRect;
    public GameObject debugPanel;

    private List<string> logs = new List<string>();
    private int maxLogs = 20;

    void Awake()
    {
        // Create debug UI if not assigned
        if (debugText == null)
        {
            CreateDebugUI();
        }

        // Subscribe to Unity's log messages
        Application.logMessageReceived += HandleLog;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void CreateDebugUI()
    {
        // Create Canvas
        GameObject canvasGO = new GameObject("DebugCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Create Panel
        debugPanel = new GameObject("DebugPanel");
        debugPanel.transform.SetParent(canvasGO.transform, false);
        Image panelImage = debugPanel.AddComponent<Image>();
        panelImage.color = new Color(0, 0, 0, 0.8f);

        RectTransform panelRect = debugPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(1, 0.5f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Create Scroll Rect
        scrollRect = debugPanel.AddComponent<ScrollRect>();
        scrollRect.vertical = true;
        scrollRect.horizontal = false;

        // Create Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(debugPanel.transform, false);
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 0);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        scrollRect.content = contentRect;

        // Create Text
        GameObject textGO = new GameObject("DebugText");
        textGO.transform.SetParent(content.transform, false);
        debugText = textGO.AddComponent<Text>();
        debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        debugText.fontSize = 14;
        debugText.color = Color.white;
        debugText.alignment = TextAnchor.UpperLeft;

        RectTransform textRect = debugText.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0, 0);
        textRect.anchorMax = new Vector2(1, 1);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Add ContentSizeFitter for auto-sizing
        ContentSizeFitter fitter = textGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Add toggle button
        CreateToggleButton(canvasGO);
    }

    private void CreateToggleButton(GameObject canvas)
    {
        GameObject buttonGO = new GameObject("ToggleButton");
        buttonGO.transform.SetParent(canvas.transform, false);

        Image buttonImage = buttonGO.AddComponent<Image>();
        buttonImage.color = Color.red;

        Button button = buttonGO.AddComponent<Button>();
        button.onClick.AddListener(ToggleDebugPanel);

        RectTransform buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1, 1);
        buttonRect.anchorMax = new Vector2(1, 1);
        buttonRect.anchoredPosition = new Vector2(-50, -50);
        buttonRect.sizeDelta = new Vector2(80, 80);

        // Button text
        GameObject buttonTextGO = new GameObject("Text");
        buttonTextGO.transform.SetParent(buttonGO.transform, false);
        Text buttonText = buttonTextGO.AddComponent<Text>();
        buttonText.text = "LOG";
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 12;
        buttonText.color = Color.white;
        buttonText.alignment = TextAnchor.MiddleCenter;

        RectTransform buttonTextRect = buttonText.GetComponent<RectTransform>();
        buttonTextRect.anchorMin = Vector2.zero;
        buttonTextRect.anchorMax = Vector2.one;
        buttonTextRect.offsetMin = Vector2.zero;
        buttonTextRect.offsetMax = Vector2.zero;
    }

    private void ToggleDebugPanel()
    {
        if (debugPanel != null)
        {
            debugPanel.SetActive(!debugPanel.activeSelf);
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        string coloredLog = "";

        switch (type)
        {
            case LogType.Error:
                coloredLog = $"<color=red>[ERROR] {logString}</color>";
                break;
            case LogType.Warning:
                coloredLog = $"<color=yellow>[WARNING] {logString}</color>";
                break;
            case LogType.Log:
                coloredLog = $"<color=white>[LOG] {logString}</color>";
                break;
            default:
                coloredLog = $"[{type}] {logString}";
                break;
        }

        logs.Add($"{System.DateTime.Now:HH:mm:ss} {coloredLog}");

        // Keep only recent logs
        if (logs.Count > maxLogs)
        {
            logs.RemoveAt(0);
        }

        // Update display
        if (debugText != null)
        {
            debugText.text = string.Join("\n", logs);

            // Auto-scroll to bottom
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }

    // Custom logging methods
    public static void LogMobile(string message)
    {
        Debug.Log($"[MOBILE] {message}");
    }

    public static void LogErrorMobile(string message)
    {
        Debug.LogError($"[MOBILE] {message}");
    }
}