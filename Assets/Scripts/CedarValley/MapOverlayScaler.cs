using System.Collections.Generic;
using CLSR.Presentation;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cedar Valley: keeps player-facing map information at a constant on-screen size while the
/// world scenery zooms. Only scenes that contain this component opt in.
///
/// * Site cylinders and storage markers: the original renderer keeps drawing nothing but stays
///   enabled (so camera fitting sees the same bounds); a stand-in visual follows its anchor and is
///   scaled by the inverse of the map magnification about the marker's ground point. The site's
///   collider is resized to match the stand-in, so hover and click match what is visible.
/// * World-space map labels (branch signs, storage tags, camp label, legacy node cards): their
///   world rects stay in place for camera fitting; their text is drawn by a screen-space twin
///   projected from the same anchor, at a fixed pixel scale for the current resolution.
/// Nothing here reads or changes game state.
/// </summary>
[DefaultExecutionOrder(200)] // after GraphCameraFraming (100) has placed the camera for this frame
public class MapOverlayScaler : MonoBehaviour
{
    [SerializeField, Tooltip("Label pixels per canvas unit on a 1060x620 reference screen.")]
    private float labelPixelScale = 0.34f;
    private const float MinimumSiteWidth = 10f, MinimumSiteWidthPerspective = 14f;

    private sealed class Marker
    {
        public Transform Anchor;
        public Renderer Source;
        public Transform Visual;
        public Vector3 GroundLocal;
        public float MeshWidth;
        public CapsuleCollider Capsule;
        public float Radius, Height;
        public Vector3 Center;
    }

    private sealed class Label
    {
        public Canvas Source;
        public RectTransform SourceRect;
        public Transform Anchor;
        public RectTransform Twin;
        public readonly List<(Text src, Text dst)> Texts = new List<(Text, Text)>();
        public bool OwnerControlsVisibility;
        public bool WasEnabled;
    }

    /// <summary>While set (telescope sighting), every marker visual and label twin is hidden.</summary>
    public static bool Suppressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Suppressed = false;

    private readonly List<Marker> markers = new List<Marker>();
    private readonly List<Label> labels = new List<Label>();
    private readonly HashSet<Canvas> known = new HashSet<Canvas>();
    private Transform visualRoot;
    private RectTransform overlay;
    private GraphCameraFraming framing;
    private float nextScan;

    private void OnDisable()
    {
        foreach (Marker m in markers)
        {
            if (m.Source != null) m.Source.forceRenderingOff = false;
            if (m.Capsule != null) { m.Capsule.radius = m.Radius; m.Capsule.height = m.Height; m.Capsule.center = m.Center; }
            if (m.Visual != null) m.Visual.gameObject.SetActive(false);
        }
        foreach (Label l in labels)
        {
            if (l.Source != null) l.Source.enabled = l.WasEnabled;
            if (l.Twin != null) l.Twin.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (visualRoot != null) Destroy(visualRoot.gameObject);
        if (overlay != null) Destroy(overlay.gameObject);
    }

    private void Start()
    {
        visualRoot = new GameObject("Map marker visuals").transform;
        var canvasObject = new GameObject("Map label overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -20; // under any other overlay UI; the IMGUI HUD draws on top
        canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        overlay = canvasObject.GetComponent<RectTransform>();

        foreach (RescueNode node in FindObjectsByType<RescueNode>(FindObjectsSortMode.None))
            AddMarker(node.transform, node.GetComponent<CapsuleCollider>());
        GameObject camp = GameObject.Find("Graph/Nodes/N00_Camp");
        if (camp != null && !camp.TryGetComponent<RescueNode>(out _))
            AddMarker(camp.transform, camp.GetComponent<CapsuleCollider>());
        foreach (StorageCampMarker storage in FindObjectsByType<StorageCampMarker>(FindObjectsSortMode.None))
            AddMarker(storage.transform, null);
        ScanLabels();
    }

    private void AddMarker(Transform anchor, CapsuleCollider capsule)
    {
        var source = anchor.GetComponent<MeshRenderer>();
        var filter = anchor.GetComponent<MeshFilter>();
        if (source == null || filter == null || filter.sharedMesh == null) return;
        Bounds b = filter.sharedMesh.bounds;
        var visual = new GameObject(anchor.name + " marker", typeof(MeshFilter), typeof(MeshRenderer));
        visual.transform.SetParent(visualRoot, false);
        visual.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var vr = visual.GetComponent<MeshRenderer>();
        // The same material instances stay live when the node changes selection/status colour.
        vr.sharedMaterials = source.sharedMaterials;
        visual.layer = anchor.gameObject.layer;
        vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        vr.receiveShadows = false;
        source.forceRenderingOff = true;
        var m = new Marker
        {
            Anchor = anchor, Source = source, Visual = visual.transform,
            GroundLocal = new Vector3(b.center.x, b.min.y, b.center.z), MeshWidth = b.size.x, Capsule = capsule
        };
        if (capsule != null) { m.Radius = capsule.radius; m.Height = capsule.height; m.Center = capsule.center; }
        markers.Add(m);
    }

    private void ScanLabels()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c.renderMode != RenderMode.WorldSpace || known.Contains(c)) continue;
            string n = c.gameObject.name;
            Transform anchor = null;
            bool owner = false;
            if (n.StartsWith("SignLabel_")) anchor = c.transform.parent;
            else if (n.StartsWith("StorageTag_"))
            {
                string id = n.Substring("StorageTag_".Length);
                foreach (StorageCampMarker s in FindObjectsByType<StorageCampMarker>(FindObjectsSortMode.None))
                    if (s.NodeId == id) anchor = s.transform;
            }
            else if (n == "CampLabel") { GameObject camp = GameObject.Find("N00_Camp"); anchor = camp != null ? camp.transform : null; }
            else if (n == "NodeInfoLabel")
            {
                owner = true;
                float best = float.MaxValue;
                foreach (RescueNode node in FindObjectsByType<RescueNode>(FindObjectsSortMode.None))
                {
                    float d = (node.transform.position - c.transform.position).sqrMagnitude;
                    if (d < best) { best = d; anchor = node.transform; }
                }
            }
            else continue;
            known.Add(c);
            AddLabel(c, anchor != null ? anchor : c.transform, owner);
        }
    }

    private void AddLabel(Canvas source, Transform anchor, bool owner)
    {
        var src = (RectTransform)source.transform;
        var twinObject = new GameObject(source.name + " (screen)", typeof(RectTransform));
        var twin = (RectTransform)twinObject.transform;
        twin.SetParent(overlay, false);
        twin.pivot = src.pivot;
        twin.sizeDelta = src.sizeDelta;
        twin.anchorMin = twin.anchorMax = Vector2.zero;
        var label = new Label { Source = source, SourceRect = src, Anchor = anchor, Twin = twin,
                                OwnerControlsVisibility = owner, WasEnabled = source.enabled };
        foreach (Text t in source.GetComponentsInChildren<Text>(true))
        {
            GameObject copy = Instantiate(t.gameObject, twin, false);
            label.Texts.Add((t, copy.GetComponent<Text>()));
        }
        labels.Add(label);
    }

    private void LateUpdate()
    {
        if (visualRoot != null && visualRoot.gameObject.activeSelf == Suppressed) visualRoot.gameObject.SetActive(!Suppressed);
        if (overlay != null && overlay.gameObject.activeSelf == Suppressed) overlay.gameObject.SetActive(!Suppressed);
        if (Suppressed) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        if (framing == null || framing.gameObject != cam.gameObject) framing = cam.GetComponent<GraphCameraFraming>();
        bool perspective = !cam.orthographic;
        float k = framing != null ? 1f / MapViewMath.Magnification(framing.ZoomLevel) : 1f;
        float ppm = perspective ? 1f : cam.pixelHeight / (2f * cam.orthographicSize);
        // perspective: a marker keeps the on-screen size it would have at the fitted view's focus,
        // so distant sites stay as legible as near ones and nothing grows as the camera dollies in
        float refPpm = framing != null ? framing.ReferencePixelsPerMeter : ppm;
        float ui = Mathf.Clamp(Mathf.Min(Screen.width / 1060f, Screen.height / 620f), .55f, 1.35f);

        bool resizedCollider = false;
        foreach (Marker m in markers)
        {
            if (m.Anchor == null) continue;
            bool show = m.Anchor.gameObject.activeInHierarchy && m.Source.enabled;
            if (m.Visual.gameObject.activeSelf != show) m.Visual.gameObject.SetActive(show);
            m.Source.forceRenderingOff = true;
            Vector3 scale = m.Anchor.lossyScale;
            float localPpm = perspective && framing != null ? framing.PixelsPerMeterAt(m.Anchor.position) : ppm;
            float baseFactor = perspective ? refPpm / Mathf.Max(1e-4f, localPpm) : k;
            float viewedWidth = m.MeshWidth * Mathf.Abs(scale.x) * localPpm;
            float factor = m.Capsule != null && viewedWidth > 1e-5f
                ? Mathf.Max(baseFactor, (perspective ? MinimumSiteWidthPerspective : MinimumSiteWidth) * ui / viewedWidth)
                : baseFactor;
            Vector3 ground = m.Anchor.TransformPoint(m.GroundLocal);
            m.Visual.SetPositionAndRotation(ground + (m.Anchor.position - ground) * factor, m.Anchor.rotation);
            m.Visual.localScale = scale * factor;
            if (m.Capsule != null && !Mathf.Approximately(m.Capsule.radius, m.Radius * factor))
            {
                // Set height first on growth; Unity clamps the radius to half the height.
                if (factor * m.Height > m.Capsule.height) m.Capsule.height = m.Height * factor;
                m.Capsule.radius = m.Radius * factor;
                m.Capsule.height = m.Height * factor;
                m.Capsule.center = m.GroundLocal + (m.Center - m.GroundLocal) * factor;
                resizedCollider = true;
            }
        }
        // ReviewStudyFlow picks using raycasts and auto-sync is off in this project.
        if (resizedCollider) Physics.SyncTransforms();

        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; ScanLabels(); }
        float pixelScale = labelPixelScale * ui;
        foreach (Label l in labels)
        {
            if (l.Source == null) { if (l.Twin != null) l.Twin.gameObject.SetActive(false); continue; }
            if (l.Anchor == null) { l.Twin.gameObject.SetActive(false); continue; }
            if (l.OwnerControlsVisibility) l.WasEnabled = l.Source.enabled;
            bool wanted = l.Source.gameObject.activeInHierarchy && l.WasEnabled;
            l.Source.enabled = false; // the world rect stays in place for camera fitting
            Vector3 a = cam.WorldToScreenPoint(l.Anchor.position);
            Vector3 p = cam.WorldToScreenPoint(l.SourceRect.position);
            Rect viewport = cam.pixelRect;
            bool visible = wanted && a.z > 0f && viewport.Contains(new Vector2(a.x, a.y));
            if (l.Twin.gameObject.activeSelf != visible) l.Twin.gameObject.SetActive(visible);
            if (!visible) continue;
            // Keep the label's layout around its anchor in proportion to the label's own screen size.
            float labelPpm = perspective && framing != null ? framing.PixelsPerMeterAt(l.Anchor.position) : ppm;
            float worldPixels = l.SourceRect.lossyScale.x * labelPpm;
            float r = worldPixels > 1e-5f ? pixelScale / worldPixels : 1f;
            l.Twin.position = new Vector3(a.x + (p.x - a.x) * r, a.y + (p.y - a.y) * r, 0f);
            l.Twin.localScale = Vector3.one * pixelScale;
            foreach (var (src, dst) in l.Texts)
            {
                if (src == null || dst == null) continue;
                if (dst.text != src.text) dst.text = src.text;
                dst.color = src.color;
            }
        }
    }
}
