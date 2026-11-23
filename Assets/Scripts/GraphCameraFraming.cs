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
        panOffset == Vector2.zero && groundPan == Vector2.zero;

    /// <summary>Pixels per metre of the fitted (zoom 0) view at the board centre; overlays size against it.</summary>
    public float ReferencePixelsPerMeter => cam == null || cam.pixelHeight == 0 ? 1f : cam.orthographic
        ? cam.pixelHeight / (2f * Mathf.Max(0.01f, fittedOrthoSize))
        : cam.pixelHeight / (2f * Mathf.Max(0.01f, fitDistance) * TanHalfFov);

    /// <summary>Pixels per metre at a world point for the current view (orthographic: uniform).</summary>
    public float PixelsPerMeterAt(Vector3 world)
    {
        if (cam == null || cam.pixelHeight == 0) return 1f;
        if (cam.orthographic) return cam.pixelHeight / (2f * cam.orthographicSize);
        float depth = Vector3.Dot(world - transform.position, transform.forward);
        return cam.pixelHeight / (2f * Mathf.Max(0.5f, depth) * TanHalfFov);
    }

    /// <summary>Distance from the camera to the board-centre ground point (perspective map view).</summary>
    public float FocusDistance => distance;
    /// <summary>Current continuous zoom level (0 = fitted map).</summary>
    public float ZoomLevel => zoomCurrent;

    // Buttons step to the next whole level and keep the centre of the map board fixed.
    public void ZoomOut() => ZoomTo(Mathf.Ceil(zoomTarget - 0.001f) - 1f, null);
    public void ZoomIn() => ZoomTo(Mathf.Floor(zoomTarget + 0.001f) + 1f, null);

    /// <summary>Zooms toward a level, keeping the given screen point (pixels, y up) fixed.</summary>
    public void ZoomTo(float level, Vector2? screenAnchor)
    {
        if (cam == null || cam.pixelWidth == 0 || cam.pixelHeight == 0)
            return;
        zoomTarget = MapViewMath.ClampLevel(level, MaxZoomLevel);
        Vector2 viewport = screenAnchor.HasValue
            ? new Vector2(screenAnchor.Value.x / cam.pixelWidth, screenAnchor.Value.y / cam.pixelHeight)
            : BoardCenterViewport();
        zoomAnchorViewport = viewport;
        if (!cam.orthographic)
        {
            // keep the ground point under the anchor fixed while the camera dollies in or out
            // measured from where the camera is about to be (a pan or re-centre may be pending this frame)
            Vector3 eye = fitFocus + GroundRight * groundPan.x + GroundAhead * groundPan.y - transform.forward * distance;
            if (!screenAnchor.HasValue && hasCentreTarget)
            {
                // button zoom after centring on a site keeps that site (at its real height) centred
                zoomAnchorWorld = centreTarget;
                hasZoomAnchor = true;
                return;
            }
            hasZoomAnchor = GroundHit(eye, ViewDir(viewport), out zoomAnchorWorld);
            return;
        }
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
        groundPan = Vector2.zero;
        hasZoomAnchor = false;
        hasCentreTarget = false;
    }

    public void PanByScreenDelta(Vector2 screenDelta)
    {
        if (cam == null || cam.pixelHeight == 0)
            return;
        if (!cam.orthographic)
        {
            hasZoomAnchor = false;
            hasCentreTarget = false;
            // the ground under the board centre follows the pointer
            float metresPerPixel = 2f * distance * TanHalfFov / cam.pixelHeight;
            groundPan.x -= screenDelta.x * metresPerPixel;
            groundPan.y -= screenDelta.y * metresPerPixel / Mathf.Max(0.2f, -transform.forward.y);
            return;
        }

        // A drag takes over from any zoom animation's anchor.
        hasZoomAnchor = false;
        // Match the pointer's travel at the current zoom, in either direction.
        float unitsPerPixel = 2f * cam.orthographicSize / cam.pixelHeight;
        panOffset += screenDelta * unitsPerPixel;
    }

    /// <summary>Centers the existing camera workflow on an authored map point.</summary>
    public void CenterOnWorldPoint(Vector3 worldPoint)
    {
        if (cam == null)
            return;
        if (!cam.orthographic)
        {
            hasZoomAnchor = false;
            // points well above the ground (e.g. minimap picks on the camera plane) drop along the view
            if (Mathf.Abs(worldPoint.y - planeY) > 60f &&
                GroundHit(worldPoint, transform.forward, out Vector3 dropped))
                worldPoint = dropped;
            Vector3 focus = FocusFor(worldPoint, BoardCenterViewport(), distance);
            groundPan = ToPan(focus - fitFocus);
            centreTarget = worldPoint;
            hasCentreTarget = true;
            return;
        }

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
        if (cam == null)
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
    private float fittedOrthoSize = 1f;

    // ---- perspective map view (Cedar Valley): fixed pitch/yaw, the camera dollies along its
    // view direction to zoom and slides over the ground to pan. Fitting keeps every graph object
    // and label inside the board, as the orthographic fit does.
    [SerializeField, Tooltip("Ground plane height (m) used for perspective zoom anchors and panning.")]
    private float groundLevel = 5f;
    [SerializeField, Tooltip("Closest the perspective camera may come to its focus point (m).")]
    private float minFocusDistance = 28f;
    private Vector3 fitFocus;
    private float fitDistance = 300f;
    private Vector2 groundPan;
    private Vector3 centreTarget;
    private float planeY = 5f;
    private readonly List<Vector3> heightSamples = new List<Vector3>();
    private bool hasCentreTarget;
    private float distance = 300f;
    private float nextFit;
    private Vector2Int fitScreen;
    private Rect fitBoard;
    private readonly List<Vector3> fitPoints = new List<Vector3>();
    private Vector2 mapMin, mapMax;
    private float TanHalfFov => Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

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
            // trail points lie on the terrain: they tell the perspective camera how high the ground is
            foreach (LineRenderer line in root.GetComponentsInChildren<LineRenderer>(true))
                for (int i = 0; i < line.positionCount; i += 2)
                    heightSamples.Add(line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i)));
        }
    }

    private void LateUpdate()
    {
        if (graphRenderers.Count == 0)
        {
            return;
        }
        if (!cam.orthographic)
        {
            LatePerspective();
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
        fittedOrthoSize = fittedSize;
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

    // ================================================================ perspective view
    private void LatePerspective()
    {
        GetBoard(out float left, out float right, out float bottom, out float top);
        var board = Rect.MinMaxRect(left, bottom, right, top);
        var screen = new Vector2Int(cam.pixelWidth, cam.pixelHeight);
        if (Time.unscaledTime >= nextFit || screen != fitScreen || board != fitBoard)
        {
            nextFit = Time.unscaledTime + 0.5f;
            fitScreen = screen;
            fitBoard = board;
            CollectFitPoints();
            FitPerspective(board);
        }

        zoomCurrent = MapViewMath.Smooth(zoomCurrent, zoomTarget, Time.unscaledDeltaTime);
        distance = Mathf.Max(minFocusDistance, fitDistance / MapViewMath.Magnification(zoomCurrent));
        if (hasZoomAnchor)
        {
            groundPan = ToPan(FocusFor(zoomAnchorWorld, zoomAnchorViewport, distance) - fitFocus);
            if (Mathf.Approximately(zoomCurrent, zoomTarget))
                hasZoomAnchor = false;
        }

        // pan limits: every part of the map (graph and labels) can be brought to the board centre,
        // plus a little overscroll, at any zoom
        float visibleRight = 2f * distance * TanHalfFov * cam.aspect * board.width;
        float visibleAhead = 2f * distance * TanHalfFov * board.height / Mathf.Max(0.2f, -transform.forward.y);
        Vector2 f0 = ToPan(fitFocus);
        groundPan.x = ClampAxis(groundPan.x, mapMin.x - f0.x, mapMax.x - f0.x, visibleRight);
        groundPan.y = ClampAxis(groundPan.y, mapMin.y - f0.y, mapMax.y - f0.y, visibleAhead);

        Vector3 focus = fitFocus + GroundRight * groundPan.x + GroundAhead * groundPan.y;
        // zoomed in, the view pivots on the real ground height under the board centre (ridges,
        // the cliff pass bench), so close views never sink into a hillside
        float w = Mathf.InverseLerp(0.5f, 4f, zoomCurrent);
        float wantPlane = Mathf.Lerp(groundLevel, HeightAt(focus), w);
        planeY = MapViewMath.Smooth(planeY, wantPlane, Time.unscaledDeltaTime, 6f);
        focus.y = planeY;
        transform.position = focus - transform.forward * distance;
    }

    private static float ClampAxis(float pan, float min, float max, float visible)
    {
        float over = visible * PanOverscrollFraction, half = visible * 0.5f;
        float lo = min + half, hi = max - half;
        if (lo > hi) lo = hi = 0f; // the whole extent already fits: the fitted view is the centre
        return Mathf.Clamp(pan, Mathf.Min(lo, 0f) - over, Mathf.Max(hi, 0f) + over);
    }

    // Inverse-distance-weighted height of the trail and site samples near a ground point.
    private float HeightAt(Vector3 p)
    {
        float sum = 0f, wsum = 0f;
        foreach (Vector3 q in heightSamples)
        {
            float dx = q.x - p.x, dz = q.z - p.z, d2 = dx * dx + dz * dz;
            if (d2 > 90f * 90f) continue;
            float wgt = 1f / (d2 + 25f);
            sum += q.y * wgt; wsum += wgt;
        }
        return wsum > 0f ? sum / wsum : groundLevel;
    }

    private Vector3 GroundRight => Flat(transform.right);
    private Vector3 GroundAhead => Flat(transform.forward);
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
    private Vector2 ToPan(Vector3 d) => new Vector2(Vector3.Dot(d, GroundRight), Vector3.Dot(d, GroundAhead));

    private Vector3 ViewDir(Vector2 viewport)
    {
        float t = TanHalfFov;
        var d = new Vector3((viewport.x - 0.5f) * 2f * t * cam.aspect, (viewport.y - 0.5f) * 2f * t, 1f);
        return transform.rotation * d.normalized;
    }

    private bool GroundHit(Vector3 origin, Vector3 dir, out Vector3 hit)
    {
        hit = origin;
        if (dir.y > -1e-4f) return false;
        hit = origin + dir * ((planeY - origin.y) / dir.y);
        return true;
    }

    /// <summary>Focus (on the ground plane) that puts world point a at viewport v, at a given distance.</summary>
    private Vector3 FocusFor(Vector3 a, Vector2 v, float dist)
    {
        Vector3 fwd = transform.forward, dir = ViewDir(v);
        if (dir.y > -1e-4f) return fitFocus;
        float t = (a.y + fwd.y * dist - planeY) / dir.y;
        return a - dir * t + fwd * dist;
    }

    private void CollectFitPoints()
    {
        fitPoints.Clear();
        foreach (Renderer graphRenderer in graphRenderers)
        {
            if (graphRenderer == null || !graphRenderer.enabled) continue;
            Bounds b = graphRenderer.bounds;
            for (int i = 0; i < 8; i++)
                fitPoints.Add(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
        }
        foreach (RescueNodeLabel label in labels)
        {
            if (label == null || label.LabelRect == null) continue;
            label.LabelRect.GetWorldCorners(corners);
            fitPoints.AddRange(corners);
        }
        foreach (WorldLabel worldLabel in WorldLabel.Active)
        {
            if (worldLabel == null || worldLabel.Rect == null) continue;
            worldLabel.Rect.GetWorldCorners(corners);
            fitPoints.AddRange(corners);
        }
        float rx0 = float.MaxValue, rx1 = float.MinValue, ry0 = float.MaxValue, ry1 = float.MinValue;
        Vector3 fwd = transform.forward;
        foreach (Vector3 raw in fitPoints)
        {
            // where the view ray through this point meets the ground: the focus that centres it
            Vector3 p = fwd.y < -1e-3f ? raw + fwd * ((groundLevel - raw.y) / fwd.y) : raw;
            float x = Vector3.Dot(p, GroundRight), y = Vector3.Dot(p, GroundAhead);
            rx0 = Mathf.Min(rx0, x); rx1 = Mathf.Max(rx1, x); ry0 = Mathf.Min(ry0, y); ry1 = Mathf.Max(ry1, y);
        }
        mapMin = fitPoints.Count > 0 ? new Vector2(rx0, ry0) : Vector2.one * -50f;
        mapMax = fitPoints.Count > 0 ? new Vector2(rx1, ry1) : Vector2.one * 50f;
    }

    // Viewport extents of the fit points for a camera at focus - forward * dist; false if any is behind.
    private bool Extents(Vector3 focus, float dist, out Rect extents)
    {
        Vector3 c = focus - transform.forward * dist;
        Quaternion inv = Quaternion.Inverse(transform.rotation);
        float t = TanHalfFov, x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        extents = default;
        foreach (Vector3 p in fitPoints)
        {
            Vector3 q = inv * (p - c);
            if (q.z < 1f) return false;
            float vx = 0.5f + q.x / (q.z * 2f * t * cam.aspect), vy = 0.5f + q.y / (q.z * 2f * t);
            x0 = Mathf.Min(x0, vx); x1 = Mathf.Max(x1, vx); y0 = Mathf.Min(y0, vy); y1 = Mathf.Max(y1, vy);
        }
        extents = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    private void FitPerspective(Rect board)
    {
        if (fitPoints.Count == 0) return;
        float keepPlane = planeY;
        planeY = groundLevel;
        FitPerspectiveAtGround(board);
        planeY = keepPlane;
    }

    private void FitPerspectiveAtGround(Rect board)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in fitPoints) sum += p;
        Vector3 centre = sum / fitPoints.Count;
        Vector3 focus = new Vector3(centre.x, groundLevel, centre.z);
        float dist = fitDistance > 1f ? fitDistance : 400f;
        for (int pass = 0; pass < 4; pass++)
        {
            float lo = 10f, hi = 6000f;
            for (int i = 0; i < 26; i++)
            {
                float mid = (lo + hi) * 0.5f;
                bool fits = Extents(focus, mid, out Rect e) && e.width <= board.width && e.height <= board.height;
                if (fits) hi = mid; else lo = mid;
            }
            dist = hi;
            if (!Extents(focus, dist, out Rect ext)) break;
            // slide the focus so the fitted extents sit centred in the board
            Vector3 camPos = focus - transform.forward * dist;
            if (!GroundHit(camPos, ViewDir(ext.center), out Vector3 under)) break;
            focus = FocusFor(under, board.center, dist);
            focus.y = groundLevel;
        }
        fitFocus = focus;
        fitDistance = dist;
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
