using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cedar Valley forest detail switching by projected size on screen. The map camera is
/// orthographic and zooms by changing orthographicSize, so on-screen size is
/// camera pixel height / (2 * orthographicSize) pixels per meter. Each forest chunk has a
/// Far, Mid and Close roots built from the same tree placements. Close adds detail to Mid
/// without duplicating or replacing its geometry. Hysteresis keeps thresholds stable.
/// </summary>
[DefaultExecutionOrder(210)] // after GraphCameraFraming has set this frame's orthographic size
public class ForestDetailTiers : MonoBehaviour
{
    [Tooltip("Pixels per meter above which individual tree shapes appear (mid tier).")]
    [SerializeField] private float midAbovePixelsPerMeter = 2.65f;
    [Tooltip("Pixels per meter above which close detail appears (branches, trunks, snow, ground clutter).")]
    [SerializeField] private float closeAbovePixelsPerMeter = 7.6f;
    [Tooltip("Fractional hysteresis around each threshold.")]
    [SerializeField, Range(0f, 0.3f)] private float buffer = 0.08f;
    [SerializeField] private List<GameObject> farRoots = new List<GameObject>();
    [SerializeField] private List<GameObject> midRoots = new List<GameObject>();
    [SerializeField] private List<GameObject> closeRoots = new List<GameObject>();
    [SerializeField] private GameObject overviewTerrain;
    [SerializeField] private GameObject detailedTerrain;

    private int tier = -1;
    private int forced = -1;
    // perspective: every chunk picks its own tier from its distance to the camera
    private int[] chunkTier;
    private Bounds[] chunkBounds;

    public int Tier => tier;
    public float PixelsPerMeter { get; private set; }

    public void SetRoots(List<GameObject> far, List<GameObject> mid, List<GameObject> close)
    {
        farRoots = far; midRoots = mid; closeRoots = close; tier = -1;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || forced >= 0) return;
        if (!cam.orthographic) { LatePerspective(cam); return; }
        if (cam.orthographicSize <= 0f) return;
        float ppm = cam.pixelHeight / (2f * cam.orthographicSize);
        PixelsPerMeter = ppm;
        int want = tier;
        float up = 1f + buffer, down = 1f - buffer;
        if (tier < 0) want = ppm >= closeAbovePixelsPerMeter ? 2 : ppm >= midAbovePixelsPerMeter ? 1 : 0;
        else if (tier == 0) { if (ppm > midAbovePixelsPerMeter * up) want = ppm > closeAbovePixelsPerMeter * up ? 2 : 1; }
        else if (tier == 1)
        {
            if (ppm > closeAbovePixelsPerMeter * up) want = 2;
            else if (ppm < midAbovePixelsPerMeter * down) want = 0;
        }
        else if (ppm < closeAbovePixelsPerMeter * down) want = ppm < midAbovePixelsPerMeter * down ? 0 : 1;
        if (want != tier) Apply(want);
    }

    private void LatePerspective(Camera cam)
    {
        int n = Mathf.Min(farRoots.Count, Mathf.Min(midRoots.Count, closeRoots.Count));
        if (chunkBounds == null || chunkBounds.Length != n)
        {
            chunkBounds = new Bounds[n];
            chunkTier = new int[n];
            for (int i = 0; i < n; i++)
            {
                chunkTier[i] = -1;
                bool any = false;
                foreach (GameObject root in new[] { farRoots[i], midRoots[i] })
                {
                    if (root == null) continue;
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) continue;
                        Bounds b = mf.sharedMesh.bounds; // chunk meshes are built in world space
                        if (!any) { chunkBounds[i] = b; any = true; } else chunkBounds[i].Encapsulate(b);
                    }
                }
            }
        }
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 eye = cam.transform.position;
        float up = 1f + buffer, down = 1f - buffer;
        for (int i = 0; i < n; i++)
        {
            float d = Mathf.Sqrt(Mathf.Max(1f, chunkBounds[i].SqrDistance(eye)));
            float ppm = cam.pixelHeight / (2f * d * tanHalf);
            int t = chunkTier[i], want = t;
            if (t < 0) want = ppm >= closeAbovePixelsPerMeter ? 2 : ppm >= midAbovePixelsPerMeter ? 1 : 0;
            else if (t == 0) { if (ppm > midAbovePixelsPerMeter * up) want = ppm > closeAbovePixelsPerMeter * up ? 2 : 1; }
            else if (t == 1) { if (ppm > closeAbovePixelsPerMeter * up) want = 2; else if (ppm < midAbovePixelsPerMeter * down) want = 0; }
            else if (ppm < closeAbovePixelsPerMeter * down) want = ppm < midAbovePixelsPerMeter * down ? 0 : 1;
            if (want == t) continue;
            chunkTier[i] = want;
            SetOne(farRoots[i], want == 0);
            SetOne(midRoots[i], want >= 1);
            SetOne(closeRoots[i], want == 2);
        }
        // terrain detail follows the board-centre focus
        float focusPpm = cam.pixelHeight / (2f * Mathf.Max(1f, FocusDistance(cam)) * tanHalf);
        PixelsPerMeter = focusPpm;
        int terrain = focusPpm >= midAbovePixelsPerMeter ? 1 : 0;
        if (terrain != tier)
        {
            tier = terrain;
            if (overviewTerrain != null) overviewTerrain.SetActive(terrain == 0);
            if (detailedTerrain != null) detailedTerrain.SetActive(terrain > 0);
        }
    }

    private static float FocusDistance(Camera cam) =>
        cam.TryGetComponent(out GraphCameraFraming framing) ? framing.FocusDistance : 300f;

    private static void SetOne(GameObject g, bool on) { if (g != null && g.activeSelf != on) g.SetActive(on); }

    /// <summary>Editor capture support: force a tier (or -1 for automatic).</summary>
    public void Apply(int t)
    {
        forced = t;
        if (chunkTier != null) for (int i = 0; i < chunkTier.Length; i++) chunkTier[i] = -1;
        if (t < 0) { tier = -1; return; }
        tier = t;
        Set(farRoots, t == 0);
        Set(midRoots, t >= 1);
        Set(closeRoots, t == 2);
        if (overviewTerrain != null) overviewTerrain.SetActive(t <= 0);
        if (detailedTerrain != null) detailedTerrain.SetActive(t > 0);
    }

    private static void Set(List<GameObject> roots, bool on)
    {
        foreach (GameObject g in roots) if (g != null && g.activeSelf != on) g.SetActive(on);
    }
}
