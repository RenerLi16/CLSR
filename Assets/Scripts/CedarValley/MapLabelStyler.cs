using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cedar Valley tactical overlay styling, layered on the screen-space label twins that
/// <see cref="MapOverlayScaler"/> creates. Labels become compact plaques: slightly smaller type,
/// a soft drop shadow instead of a heavy outline, and a translucent backplate that hugs the text.
/// Overlapping plaques are nudged apart and every plaque is kept inside the screen. Site markers
/// get a soft dark ground ring so they separate from snow and scenery. Text content, label
/// anchors, marker positions, hit areas and camera fitting are untouched. Presentation only.
/// </summary>
[DefaultExecutionOrder(230)] // after MapOverlayScaler (200) has placed this frame's twins
public class MapLabelStyler : MonoBehaviour
{
    [SerializeField, Range(0.5f, 1.2f), Tooltip("Label size relative to MapOverlayScaler's scale.")]
    private float sizeFactor = 0.82f;
    [SerializeField] private Color plateColor = new Color(0.075f, 0.095f, 0.11f, 0.58f);
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField, Tooltip("Screen margin (pixels) labels are kept inside.")]
    private float screenMargin = 6f;
    [SerializeField] private Color ringColor = new Color(0.06f, 0.08f, 0.1f, 0.42f);

    private sealed class Plate
    {
        public RectTransform Twin;
        public RectTransform Back;
        public Text[] Texts;
        public string Last;
        public Rect LocalRect;
    }

    private readonly List<Plate> plates = new List<Plate>();
    private readonly HashSet<RectTransform> styled = new HashSet<RectTransform>();
    private readonly HashSet<Transform> ringed = new HashSet<Transform>();
    private readonly List<(Plate p, Rect r)> placed = new List<(Plate, Rect)>();
    private RectTransform overlay;
    private Transform markers;
    private Sprite plateSprite;
    private Material ringMaterial;
    private float nextScan;

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.5f;
            Scan();
        }
        Layout();
    }

    private void Scan()
    {
        if (overlay == null) overlay = GameObject.Find("Map label overlay")?.transform as RectTransform;
        if (markers == null) markers = GameObject.Find("Map marker visuals")?.transform;
        if (overlay != null)
            foreach (RectTransform twin in overlay)
                if (styled.Add(twin)) Style(twin);
        if (markers != null)
            foreach (Transform m in markers)
                if (ringed.Add(m)) Ring(m);
    }

    private void Style(RectTransform twin)
    {
        var texts = twin.GetComponentsInChildren<Text>(true);
        if (texts.Length == 0) return;
        foreach (Text t in texts)
        {
            foreach (Outline o in t.GetComponents<Outline>()) if (o.GetType() == typeof(Outline)) Destroy(o);
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = shadowColor;
            shadow.effectDistance = new Vector2(2.5f, -2.5f);
            t.lineSpacing = 0.92f;
        }
        var back = new GameObject("Plate", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        back.SetParent(twin, false);
        back.SetAsFirstSibling();
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        var img = back.GetComponent<Image>();
        img.sprite = PlateSprite();
        img.type = Image.Type.Sliced;
        img.color = plateColor;
        img.raycastTarget = false;
        plates.Add(new Plate { Twin = twin, Back = back, Texts = texts });
    }

    private void Layout()
    {
        placed.Clear();
        Rect screen = new Rect(screenMargin, screenMargin, Screen.width - 2f * screenMargin, Screen.height - 2f * screenMargin);
        // camp first, then branch signs, then storage tags: earlier plates keep their spot
        plates.Sort((a, b) => Priority(a).CompareTo(Priority(b)));
        foreach (Plate p in plates)
        {
            if (p.Twin == null || !p.Twin.gameObject.activeInHierarchy) continue;
            p.Twin.localScale *= sizeFactor;
            Fit(p);
            Rect r = ScreenRect(p);
            Vector2 shift = Vector2.zero;
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                foreach (var (_, other) in placed)
                {
                    Rect cur = new Rect(r.position + shift, r.size);
                    if (!cur.Overlaps(other)) continue;
                    // move vertically away from the other plate by the overlap, plus a gap
                    float up = other.yMax - cur.yMin + 4f, down = cur.yMax - other.yMin + 4f;
                    shift.y += up < down ? up : -down;
                    moved = true;
                }
                if (!moved) break;
            }
            Rect final = new Rect(r.position + shift, r.size);
            if (final.xMin < screen.xMin) shift.x += screen.xMin - final.xMin;
            if (final.xMax > screen.xMax) shift.x -= final.xMax - screen.xMax;
            if (final.yMin < screen.yMin) shift.y += screen.yMin - final.yMin;
            if (final.yMax > screen.yMax) shift.y -= final.yMax - screen.yMax;
            p.Twin.position += (Vector3)shift;
            placed.Add((p, new Rect(r.position + shift, r.size)));
        }
    }

    private static int Priority(Plate p)
    {
        if (p.Twin == null) return 9;
        string n = p.Twin.name;
        return n.StartsWith("CampLabel") ? 0 : n.StartsWith("SignLabel_") ? 1 : n.StartsWith("StorageTag_") ? 2 : 3;
    }

    // The backplate hugs the text's preferred size inside the label rect, following its alignment.
    private static void Fit(Plate p)
    {
        Text t = p.Texts[0];
        if (t == null) return;
        string key = t.text + t.alignment;
        var tr = (RectTransform)t.transform;
        if (key == p.Last && p.Back.sizeDelta != Vector2.zero) return;
        p.Last = key;
        Rect area = tr.rect;
        Vector2 offset = (Vector2)tr.localPosition;
        float w = Mathf.Min(area.width, t.preferredWidth) + 22f, h = Mathf.Min(area.height, t.preferredHeight) + 10f;
        if (string.IsNullOrEmpty(t.text)) { p.Back.gameObject.SetActive(false); return; }
        p.Back.gameObject.SetActive(true);
        float x = offset.x + area.center.x, y = offset.y + area.center.y;
        switch (t.alignment)
        {
            case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft:
                x = offset.x + area.xMin + w * 0.5f - 11f; break;
            case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight:
                x = offset.x + area.xMax - w * 0.5f + 11f; break;
        }
        p.Back.localPosition = new Vector3(x, y, 0f);
        p.Back.sizeDelta = new Vector2(w, h);
        p.LocalRect = new Rect(x - w * 0.5f, y - h * 0.5f, w, h);
    }

    private static Rect ScreenRect(Plate p)
    {
        Vector3 a = p.Twin.TransformPoint(new Vector3(p.LocalRect.xMin, p.LocalRect.yMin, 0f));
        Vector3 b = p.Twin.TransformPoint(new Vector3(p.LocalRect.xMax, p.LocalRect.yMax, 0f));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    private Sprite PlateSprite()
    {
        if (plateSprite != null) return plateSprite;
        const int s = 32;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        const float radius = 9f;
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - s * 0.5f) - (s * 0.5f - radius));
            float dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - s * 0.5f) - (s * 0.5f - radius));
            float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        plateSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
        return plateSprite;
    }

    // A soft dark ring on the ground under each site marker (follows the marker's scaling).
    private void Ring(Transform marker)
    {
        var mf = marker.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        if (ringMaterial == null)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.72f) / 0.26f);
                a = a * a * (3f - 2f * a) * Mathf.Clamp01((1f - r) * 8f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            ringMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = ringColor, renderQueue = 2460 };
        }
        Bounds b = mf.sharedMesh.bounds;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Ground ring";
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(marker, false);
        quad.transform.localPosition = new Vector3(b.center.x, b.min.y + 0.06f, b.center.z);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        float d = Mathf.Max(b.size.x, b.size.z) * 2.1f;
        quad.transform.localScale = new Vector3(d, d, 1f);
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = ringMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }
}
