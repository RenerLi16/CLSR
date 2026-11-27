using CLSR.Presentation;
using UnityEngine;

/// <summary>
/// Depth of field for the telescope sighting (built-in pipeline image effect). Focus is a
/// distance along this camera's viewing axis; blur grows continuously with each surface's
/// view-axis distance from that plane (see <see cref="TelescopeOptics"/>). Runs only while the
/// sighting has it enabled; IMGUI drawn afterwards (timer, readout, dial) stays sharp.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class TelescopeDepthOfField : MonoBehaviour
{
    private static readonly int LensId = Shader.PropertyToID("_TelescopeLens");
    private static readonly int DofTexId = Shader.PropertyToID("_DofTex");

    public Shader shader;
    /// <summary>Focus distance along the view axis (m).</summary>
    public float FocusDistance { get; set; } = 100f;
    /// <summary>Effective aperture (m), fixed for a sighting.</summary>
    public float Aperture { get; set; }

    private Camera cam;
    private Material material;
    private DepthTextureMode previousDepthMode;

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
        previousDepthMode = cam.depthTextureMode;
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void OnDisable()
    {
        if (cam != null) cam.depthTextureMode = previousDepthMode;
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (shader == null) shader = Shader.Find("Hidden/CLSR/Telescope Depth Of Field");
        if (shader == null || !shader.isSupported || Aperture <= 0f)
        {
            Graphics.Blit(source, destination);
            return;
        }
        if (material == null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        float height = source.height;
        float focal = TelescopeOptics.FocalPixels(height, cam.fieldOfView);
        float maxBlur = TelescopeOptics.MaxBlurFor(height);
        material.SetVector(LensId, new Vector4(Mathf.Max(0.1f, FocusDistance), Aperture, focal, maxBlur));

        int w = Mathf.Max(1, source.width / 2), h = Mathf.Max(1, source.height / 2);
        RenderTextureFormat format = RenderTextureFormat.ARGB32;
        RenderTexture pre = RenderTexture.GetTemporary(w, h, 0, format);
        RenderTexture bokeh = RenderTexture.GetTemporary(w, h, 0, format);
        pre.filterMode = bokeh.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, pre, material, 0);
        Graphics.Blit(pre, bokeh, material, 1);
        Graphics.Blit(bokeh, pre, material, 2);
        material.SetTexture(DofTexId, pre);
        Graphics.Blit(source, destination, material, 3);
        RenderTexture.ReleaseTemporary(pre);
        RenderTexture.ReleaseTemporary(bokeh);
    }
}
