using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fits the rescue graph in the space beside the planning panel, then applies
// any player-requested zoom and pan.
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)] // Fit the map after its camera-facing labels have positioned themselves.
public class GraphCameraFraming : MonoBehaviour
{
    private const int MinZoomLevel = -2;
    private const int MaxZoomLevel = 4;
    private const float MagnificationPerStep = 0.15f;
    private const float PanOverscrollFraction = 0.2f;

    private int zoomLevel;
    private Vector2 panOffset;

    public bool CanZoomOut => zoomLevel > MinZoomLevel;
    public bool CanZoomIn => zoomLevel < MaxZoomLevel;
    public bool IsDefaultView => zoomLevel == 0 && panOffset == Vector2.zero;

    public void ZoomOut() => zoomLevel = Mathf.Max(MinZoomLevel, zoomLevel - 1);
    public void ZoomIn() => zoomLevel = Mathf.Min(MaxZoomLevel, zoomLevel + 1);

    public void ResetView()
    {
        zoomLevel = 0;
        panOffset = Vector2.zero;
    }

    public void PanByScreenDelta(Vector2 screenDelta)
    {
        if (!cam.orthographic || cam.pixelHeight == 0)
            return;

        // Match the pointer's travel at the current zoom, in either direction.
        float unitsPerPixel = 2f * cam.orthographicSize / cam.pixelHeight;
        panOffset += screenDelta * unitsPerPixel;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AttachOnSceneLoad()
    {
        SceneManager.sceneLoaded -= Attach;
        SceneManager.sceneLoaded += Attach;
    }

    private static void Attach(Scene scene, LoadSceneMode mode)
    {
        Camera mainCamera = Camera.main;

        if (
            mainCamera != null &&
            mainCamera.GetComponent<GraphCameraFraming>() == null
        )
        {
            mainCamera.gameObject.AddComponent<GraphCameraFraming>();
        }
    }

    [SerializeField, Range(0f, 0.2f)]
    [Tooltip("Empty space kept around the graph, as a fraction of the screen.")]
    private float margin = 0.04f;

    private Camera cam;
    private float minOrthographicSize;
    private RescuePlanningUI planningUI;
    private RescueNodeLabel[] labels;
    private readonly List<Renderer> graphRenderers = new List<Renderer>();
    private readonly Vector3[] corners = new Vector3[4];

    private float minX, maxX, minY, maxY;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        minOrthographicSize = cam.orthographicSize;
    }

    private void Start()
    {
        planningUI = FindAnyObjectByType<RescuePlanningUI>();
        labels = FindObjectsByType<RescueNodeLabel>(FindObjectsSortMode.None);
        HashSet<Transform> graphRoots = new HashSet<Transform>();

        foreach (RescueNode node in FindObjectsByType<RescueNode>(FindObjectsSortMode.None))
        {
            graphRoots.Add(node.transform.root);
        }

        foreach (Transform root in graphRoots)
        {
            graphRenderers.AddRange(root.GetComponentsInChildren<Renderer>());
        }
    }

    private void LateUpdate()
    {
        if (!cam.orthographic || graphRenderers.Count == 0)
        {
            return;
        }

        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;

        foreach (Renderer graphRenderer in graphRenderers)
        {
            if (graphRenderer == null || !graphRenderer.enabled)
            {
                continue;
            }

            Bounds b = graphRenderer.bounds;

            for (int i = 0; i < 8; i++)
            {
                Include(new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z
                ));
            }
        }

        foreach (RescueNodeLabel label in labels)
        {
            if (label == null || label.LabelRect == null)
            {
                continue;
            }

            // Reserve each hover card's space so that showing one never shifts the fitted view.
            label.LabelRect.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
                Include(corner);
        }

        foreach (WorldLabel worldLabel in WorldLabel.Active)
        {
            if (worldLabel == null || worldLabel.Rect == null)
            {
                continue;
            }

            worldLabel.Rect.GetWorldCorners(corners);

            foreach (Vector3 corner in corners)
            {
                Include(corner);
            }
        }

        // Screen area to the right of the planning panel, in 0..1 viewport units.
        float left = margin;

        if (
            planningUI != null &&
            planningUI.PanelRect != null &&
            planningUI.PanelRect.gameObject.activeInHierarchy
        )
        {
            planningUI.PanelRect.GetWorldCorners(corners);
            left = corners[2].x / Screen.width + margin;
        }
        else if (ReviewStudyFlow.IsActive)
        {
            left = ReviewStudyFlow.PanelWidth / Screen.width + margin;
        }

        float right = 1f - margin;
        float bottom = margin;
        float top = 1f - margin;
        float availableWidth = Mathf.Max(0.1f, right - left);
        float availableHeight = top - bottom;

        // Calculate a fresh 1:1 fit before applying the player's zoom level.
        float fittedSize = Mathf.Max(
            minOrthographicSize,
            (maxY - minY) / (2f * availableHeight),
            (maxX - minX) / (2f * cam.aspect * availableWidth)
        );
        float size = fittedSize / (1f + zoomLevel * MagnificationPerStep);
        cam.orthographicSize = size;

        float visibleWidth = 2f * size * cam.aspect * availableWidth;
        float visibleHeight = 2f * size * availableHeight;
        float panLimitX = GetPanLimit(maxX - minX, visibleWidth);
        float panLimitY = GetPanLimit(maxY - minY, visibleHeight);
        panOffset.x = Mathf.Clamp(panOffset.x, -panLimitX, panLimitX);
        panOffset.y = Mathf.Clamp(panOffset.y, -panLimitY, panLimitY);

        // Centre the fitted view, then offset it in the direction of the drag.
        float targetX = ((left + right) * 0.5f - 0.5f) * 2f * size * cam.aspect;
        float targetY = ((bottom + top) * 0.5f - 0.5f) * 2f * size;
        float shiftX = (minX + maxX) * 0.5f - targetX - panOffset.x;
        float shiftY = (minY + maxY) * 0.5f - targetY - panOffset.y;

        transform.position += transform.right * shiftX + transform.up * shiftY;
    }

    private static float GetPanLimit(float mapSize, float visibleSize) =>
        Mathf.Max(0f, (mapSize - visibleSize) * 0.5f) + visibleSize * PanOverscrollFraction;

    private void Include(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint);
        minX = Mathf.Min(minX, p.x);
        maxX = Mathf.Max(maxX, p.x);
        minY = Mathf.Min(minY, p.y);
        maxY = Mathf.Max(maxY, p.y);
    }
}
