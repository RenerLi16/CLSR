using System.Collections.Generic;
using CLSR.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;

// Fits the rescue graph in the space beside the planning panel, then applies
// any player-requested zoom and pan. Zoom is continuous and smoothed; wheel
// and button zoom keep the point under the cursor (or the view centre) fixed.
// The camera is presentation only: nothing here reads or changes game state.
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)] // Fit the map after its camera-facing labels have positioned themselves.
public class GraphCameraFraming : MonoBehaviour
{
    private const float PanOverscrollFraction = 0.2f;
    private const float WheelZoomPerNotch = 0.5f;

    /// <summary>
    /// Optional filter from the active UI: should a wheel event at this
    /// screen position (IMGUI coordinates, y down) zoom the map?
    /// </summary>
    public static System.Func<Vector2, bool> AllowWheelZoomAt;

    private float zoomTarget;
    private float zoomCurrent;
    private Vector2 panOffset;
    private bool hasZoomAnchor;
    private Vector3 zoomAnchorWorld;
    private Vector2 zoomAnchorViewport;
    private float MaxZoomLevel => MapViewMath.StandardMaxZoomLevel + extraZoomSteps;

    public bool CanZoomOut => zoomTarget > MapViewMath.MinZoomLevel + 0.001f;
    public bool CanZoomIn => zoomTarget < MaxZoomLevel - 0.001f;
    public bool IsDefaultView => Mathf.Approximately(zoomTarget, 0f) && Mathf.Approximately(zoomCurrent, 0f) &&
        panOffset == Vector2.zero;
    /// <summary>Current continuous zoom level (0 = fitted map).</summary>
    public float ZoomLevel => zoomCurrent;

    // Buttons step to the next whole level and keep the centre of the map board fixed.
    public void ZoomOut() => ZoomTo(Mathf.Ceil(zoomTarget - 0.001f) - 1f, null);
    public void ZoomIn() => ZoomTo(Mathf.Floor(zoomTarget + 0.001f) + 1f, null);

    /// <summary>Zooms toward a level, keeping the given screen point (pixels, y up) fixed.</summary>
    public void ZoomTo(float level, Vector2? screenAnchor)
    {
        if (cam == null || !cam.orthographic || cam.pixelWidth == 0 || cam.pixelHeight == 0)
            return;
        zoomTarget = MapViewMath.ClampLevel(level, MaxZoomLevel);
        Vector2 viewport = screenAnchor.HasValue
            ? new Vector2(screenAnchor.Value.x / cam.pixelWidth, screenAnchor.Value.y / cam.pixelHeight)
            : BoardCenterViewport();
        zoomAnchorViewport = viewport;
        zoomAnchorWorld = transform.TransformPoint(new Vector3(
            MapViewMath.ScreenToLocal(viewport.x, cam.orthographicSize, cam.aspect),
            MapViewMath.ScreenToLocal(viewport.y, cam.orthographicSize, 1f), 0f));
        hasZoomAnchor = true;
    }

    public void ResetView()
    {
        zoomTarget = 0f;
        zoomCurrent = 0f;
        panOffset = Vector2.zero;
        hasZoomAnchor = false;
    }

    public void PanByScreenDelta(Vector2 screenDelta)
    {
        if (!cam.orthographic || cam.pixelHeight == 0)
            return;

        // A drag takes over from any zoom animation's anchor.
        hasZoomAnchor = false;
        // Match the pointer's travel at the current zoom, in either direction.
        float unitsPerPixel = 2f * cam.orthographicSize / cam.pixelHeight;
        panOffset += screenDelta * unitsPerPixel;
    }

    /// <summary>Centers the existing camera workflow on an authored map point.</summary>
    public void CenterOnWorldPoint(Vector3 worldPoint)
    {
        if (!cam.orthographic)
            return;

        hasZoomAnchor = false;
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        // Centre on the map board rather than on the raw screen.
        Vector2 board = BoardCenterViewport();
        panOffset -= new Vector2(
            local.x - MapViewMath.ScreenToLocal(board.x, cam.orthographicSize, cam.aspect),
            local.y - MapViewMath.ScreenToLocal(board.y, cam.orthographicSize, 1f));
    }

    private Vector2 BoardCenterViewport()
    {
        GetBoard(out float left, out float right, out float bottom, out float top);
        return new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
    }

    private void Update()
    {
        if (cam == null || !cam.orthographic)
            return;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f)
            return;

        Vector2 pointer = Input.mousePosition;
        Vector2 guiPointer = new Vector2(pointer.x, Screen.height - pointer.y);
        if (pointer.x < 0 || pointer.y < 0 || pointer.x > Screen.width || pointer.y > Screen.height)
            return;
        if (AllowWheelZoomAt != null && !AllowWheelZoomAt(guiPointer))
            return;

        ZoomTo(zoomTarget + wheel * WheelZoomPerNotch, pointer);
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

    [SerializeField, Min(1f)]
    [Tooltip("Extra world-label size for large maps, without enlarging the scenery.")]
    private float worldLabelScale = 1f;

    [SerializeField, Range(0, 8)]
    [Tooltip("Extra close-up zoom steps after the regular map zoom range.")]
    private int extraZoomSteps;

    internal float WorldLabelScale => worldLabelScale;

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

        GetBoard(out float left, out float right, out float bottom, out float top);

        float availableWidth = Mathf.Max(0.1f, right - left);
        float availableHeight = Mathf.Max(0.1f, top - bottom);

        // Calculate a fresh 1:1 fit before applying the player's zoom level.
        float fittedSize = Mathf.Max(
            minOrthographicSize,
            (maxY - minY) / (2f * availableHeight),
            (maxX - minX) / (2f * cam.aspect * availableWidth)
        );
        zoomCurrent = MapViewMath.Smooth(zoomCurrent, zoomTarget, Time.unscaledDeltaTime);
        float size = fittedSize / MapViewMath.Magnification(zoomCurrent);
        cam.orthographicSize = size;

        float boardCenterX = (left + right) * 0.5f;
        float boardCenterY = (bottom + top) * 0.5f;
        float mapCenterX = (minX + maxX) * 0.5f;
        float mapCenterY = (minY + maxY) * 0.5f;
        if (hasZoomAnchor)
        {
            // Keep the anchored map point under the cursor while the zoom animates.
            Vector3 anchor = transform.InverseTransformPoint(zoomAnchorWorld);
            panOffset = new Vector2(
                MapViewMath.AnchoredPan(mapCenterX, anchor.x, zoomAnchorViewport.x, boardCenterX, size, cam.aspect),
                MapViewMath.AnchoredPan(mapCenterY, anchor.y, zoomAnchorViewport.y, boardCenterY, size, 1f));
            if (Mathf.Approximately(zoomCurrent, zoomTarget))
                hasZoomAnchor = false;
        }

        float visibleWidth = 2f * size * cam.aspect * availableWidth;
        float visibleHeight = 2f * size * availableHeight;
        float panLimitX = GetPanLimit(maxX - minX, visibleWidth);
        float panLimitY = GetPanLimit(maxY - minY, visibleHeight);
        panOffset.x = Mathf.Clamp(panOffset.x, -panLimitX, panLimitX);
        panOffset.y = Mathf.Clamp(panOffset.y, -panLimitY, panLimitY);

        // Centre the fitted view, then offset it in the direction of the drag.
        float shiftX = MapViewMath.CameraShift(mapCenterX, boardCenterX, size, cam.aspect, panOffset.x);
        float shiftY = MapViewMath.CameraShift(mapCenterY, boardCenterY, size, 1f, panOffset.y);

        transform.position += transform.right * shiftX + transform.up * shiftY;
    }

    // Fit into the same board rectangle used for pointing and route rendering.
    private void GetBoard(out float left, out float right, out float bottom, out float top)
    {
        left = margin;
        right = 1f - margin;
        bottom = margin;
        top = 1f - margin;

        if (ReviewStudyFlow.TryGetLiteMapRect(out Rect board))
        {
            left = board.xMin / Screen.width + margin * .5f;
            right = board.xMax / Screen.width - margin * .5f;
            bottom = 1f - board.yMax / Screen.height + margin * .5f;
            top = 1f - board.yMin / Screen.height - margin * .5f;
        }
        else if (
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
