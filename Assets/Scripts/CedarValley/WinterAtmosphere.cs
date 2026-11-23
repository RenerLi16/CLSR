using UnityEngine;

/// <summary>
/// Cedar Valley winter atmosphere: pushes the shared lighting and haze settings used by the
/// CLSR/Winter shaders. Haze always softens the scenery outside the playable board (the
/// surrounding hills fade into a pale winter sky); haze with view depth is added only as the
/// player zooms in, so the strategic overview stays completely clear. Presentation only.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(220)] // after GraphCameraFraming (100) and the overlay/LOD passes
public class WinterAtmosphere : MonoBehaviour
{
    [Header("Haze")]
    [SerializeField] private Color hazeColor = new Color(0.855f, 0.875f, 0.895f);
    [Tooltip("Playable board in world XZ (x0, z0, x1, z1). Nothing inside it is hazed at overview zoom.")]
    [SerializeField] private Vector4 board = new Vector4(-228f, -150f, 228f, 330f);
    [Tooltip("Haze growth per metre outside the board.")]
    [SerializeField, Range(0f, 0.05f)] private float outsideDensity = 0.0105f;
    [SerializeField, Range(0f, 1f)] private float maxHaze = 0.86f;
    [Tooltip("Depth haze per metre beyond the focus point, reached at close zoom.")]
    [SerializeField, Range(0f, 0.02f)] private float closeDepthDensity = 0.0042f;
    [SerializeField] private float depthStart = 12f;
    [Tooltip("Pixels per metre where depth haze starts and where it is full.")]
    [SerializeField] private Vector2 depthHazeZoom = new Vector2(3.2f, 12f);
    [Tooltip("Perspective map view: haze per metre beyond the focus point (aerial perspective toward the horizon).")]
    [SerializeField, Range(0f, 0.01f)] private float perspectiveDepthDensity = 0.0016f;

    [Header("Light")]
    [SerializeField, Range(0f, 1f)] private float wrap = 0.3f;
    [SerializeField, Range(0.2f, 2f)] private float ambientScale = 1f;
    [SerializeField, Range(0.2f, 2f)] private float sunScale = 1f;
    [SerializeField, Range(0.5f, 0.98f)] private float shoulderKnee = 0.8f;
    [SerializeField] private bool setCameraBackground = true;

    private static readonly int HazeColorId = Shader.PropertyToID("_WinterHazeColor");
    private static readonly int BoardId = Shader.PropertyToID("_WinterBoard");
    private static readonly int HazeId = Shader.PropertyToID("_WinterHaze");
    private static readonly int LightId = Shader.PropertyToID("_WinterLight");
    private static readonly int RefDepthId = Shader.PropertyToID("_WinterRefDepth");

    private void OnEnable() => Push(Camera.main);
    private void OnValidate() => Push(Camera.main);
    private void LateUpdate() => Push(Camera.main);

    private void OnDisable()
    {
        Shader.SetGlobalVector(HazeId, Vector4.zero);
        Shader.SetGlobalVector(LightId, Vector4.zero);
    }

    /// <summary>Updates the globals for a camera (also used by editor captures).</summary>
    public void Push(Camera cam)
    {
        Shader.SetGlobalColor(HazeColorId, hazeColor);
        Shader.SetGlobalVector(BoardId, board);
        Shader.SetGlobalVector(LightId, new Vector4(wrap, ambientScale, sunScale, shoulderKnee));
        float depthDensity = 0f, refDepth = 0f;
        if (cam != null)
        {
            if (setCameraBackground && cam.clearFlags == CameraClearFlags.SolidColor)
                cam.backgroundColor = hazeColor;
            Transform t = cam.transform;
            float fy = t.forward.y;
            refDepth = fy < -0.05f ? (t.position.y - 5f) / -fy : 0f;
            if (!cam.orthographic)
                depthDensity = perspectiveDepthDensity;
            else if (cam.orthographicSize > 0f)
            {
                float ppm = cam.pixelHeight / (2f * cam.orthographicSize);
                float k = Mathf.InverseLerp(depthHazeZoom.x, depthHazeZoom.y, ppm);
                depthDensity = closeDepthDensity * k * k * (3f - 2f * k);
            }
        }
        Shader.SetGlobalVector(HazeId, new Vector4(outsideDensity, depthDensity, maxHaze, depthStart));
        Shader.SetGlobalFloat(RefDepthId, refDepth);
    }
}
