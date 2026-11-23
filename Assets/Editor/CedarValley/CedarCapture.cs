using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Review captures for the Cedar Valley rebuild, rendered from the game camera's angle.
// Full-map framing mirrors GraphCameraFraming's 1:1 fit (graph renderers + margin);
// close-ups use the game's maximum zoom (1:1 size / (1.6 * 1.25^7)).
internal static class CedarCapture
{
    private struct Shot { public string Name; public Vector3 Target; public float Size; public bool TopDown; }

    [MenuItem("Tools/Cedar Valley/Capture Review Shots", false, 20)]
    private static void CaptureMenu() => CedarValleyBuilder.Run("capture.txt", () => CaptureAll(null));

    public static void CaptureAll(string only)
    {
        Camera main = Camera.main;
        GameObject graph = GameObject.Find("Graph");
        var rs = new List<Renderer>(graph.GetComponentsInChildren<Renderer>());
        Quaternion rot = main.transform.rotation;
        float fitted = Mathf.Max(126f, FitSize(rot, rs, 1.6f, 0.04f, out Vector3 centre)); // the game never fits tighter than the scene's 126
        float maxZoom = fitted / (1.6f * Mathf.Pow(1.25f, 7));
        CedarValleyBuilder.Info("1:1 fit ortho size " + fitted.ToString("0.0") + ", max-zoom size " + maxZoom.ToString("0.0"));

        Vector3 Node(string n) { var go = GameObject.Find(n); return go != null ? go.transform.position : Vector3.zero; }
        Vector3 BridgeAt(string n)
        {
            var go = GameObject.Find("World/Landscape/Crossings/" + n + "/Bridge");
            return go != null ? go.GetComponent<Renderer>().bounds.center : Vector3.zero;
        }
        var shots = new List<Shot>
        {
            new Shot { Name = "01_full_map", Target = centre, Size = fitted },
            new Shot { Name = "02_camp_maxzoom", Target = Node("N00_Camp"), Size = maxZoom },
            new Shot { Name = "03_camp_detail", Target = Node("N00_Camp") + new Vector3(0, 0, -2), Size = maxZoom * 0.55f },
            new Shot { Name = "04_lower_crossing_maxzoom", Target = BridgeAt("Lower crossing"), Size = maxZoom },
            new Shot { Name = "05_lower_crossing_detail", Target = BridgeAt("Lower crossing"), Size = maxZoom * 0.42f },
            new Shot { Name = "06_upper_crossing_maxzoom", Target = BridgeAt("Upper crossing"), Size = maxZoom },
            new Shot { Name = "07_upper_crossing_detail", Target = BridgeAt("Upper crossing"), Size = maxZoom * 0.42f },
            new Shot { Name = "08_camp_and_crossings", Target = Node("N00_Camp") + new Vector3(18, 0, 12), Size = maxZoom * 2.4f },
            new Shot { Name = "09_plan_topdown", Target = new Vector3(0, 0, 100), Size = 205f, TopDown = true },
        };
        string dir = Path.Combine(CedarValleyBuilder.OutDir, "shots");
        Directory.CreateDirectory(dir);
        foreach (Shot s in shots)
        {
            if (only != null && !s.Name.Contains(only)) continue;
            Render(main, s.TopDown ? Quaternion.Euler(90f, 0f, 0f) : rot, s.Target, s.Size, Path.Combine(dir, s.Name + ".png"), s.TopDown ? 1400 : 1600, s.TopDown ? 1400 : 1000);
            CedarValleyBuilder.Info("shot " + s.Name + " size " + s.Size.ToString("0.0"));
        }
    }

    public static float FitSize(Quaternion rot, List<Renderer> rs, float aspect, float margin, out Vector3 centre)
    {
        Matrix4x4 toCam = Matrix4x4.Rotate(Quaternion.Inverse(rot));
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        Vector3 sum = Vector3.zero;
        foreach (Renderer r in rs)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 c = toCam.MultiplyPoint3x4(p);
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x); minY = Mathf.Min(minY, c.y); maxY = Mathf.Max(maxY, c.y);
            }
        }
        float avail = 1f - 2f * margin;
        float size = Mathf.Max((maxY - minY) / (2f * avail), (maxX - minX) / (2f * aspect * avail));
        Vector3 cc = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
        centre = Matrix4x4.Rotate(rot).MultiplyPoint3x4(cc);
        // project the centre onto the ground plane through the view direction
        Vector3 fwd = rot * Vector3.forward;
        float t = (5f - centre.y) / fwd.y;
        centre += fwd * t;
        return size;
    }

    private static void Render(Camera main, Quaternion rot, Vector3 target, float size, string path, int w, int h)
    {
        var go = new GameObject("__CedarCapture") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 3000f;
        cam.transform.rotation = rot;
        cam.transform.position = target - (rot * Vector3.forward) * 340f;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
    }
}
