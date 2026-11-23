using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Review tools for the winter art pass: shader diagnostics and a fixed set of captures
// (overview, medium, close, camp, crossings, plus an oblique showcase view approximating the
// reference's framing). Output: Temp/CedarValleyBuild/winter/<prefix>_*.png.
internal static class CedarWinterReview
{
    private static string Dir => Path.Combine(CedarValleyBuilder.OutDir, "winter");

    [MenuItem("Tools/Cedar Valley/Winter/Check Shaders", false, 60)]
    private static void ShaderMenu() => CedarValleyBuilder.Run("shaders.txt", CheckShaders);

    [MenuItem("Tools/Cedar Valley/Winter/Capture Review Set", false, 61)]
    private static void CaptureMenu() => CedarValleyBuilder.Run("wintershots.txt", () =>
    {
        string prefixFile = Path.Combine(CedarValleyBuilder.OutDir, "winterprefix.txt");
        Capture(File.Exists(prefixFile) ? File.ReadAllText(prefixFile).Trim() : "after");
    });

    [MenuItem("Tools/Cedar Valley/Winter/Use Perspective Map Camera", false, 62)]
    private static void PerspectiveMenu() => CedarValleyBuilder.Run("camera.txt", () =>
    {
        CedarValleyBuilder.UsePerspectiveMapCamera();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        CedarValleyBuilder.Info("saved " + scene.path);
    });

    private static void CheckShaders()
    {
        foreach (string n in new[] { "CLSR/Winter Ground", "CLSR/Winter Lit", "CLSR/Winter Foliage", "CLSR/Winter Decal",
                                     "CLSR/Winter River Ice", "CLSR/Winter Cast Shadow" })
        {
            Shader s = Shader.Find(n);
            if (s == null) { CedarValleyBuilder.Info(n + ": MISSING"); continue; }
            var msgs = ShaderUtil.GetShaderMessages(s);
            CedarValleyBuilder.Info(n + ": " + msgs.Length + " messages, supported " + s.isSupported);
            foreach (var m in msgs) CedarValleyBuilder.Info("   " + m.severity + " line " + m.line + ": " + m.message + " (" + m.platform + ")");
        }
    }

    private static Component Find(string path, string type)
    {
        GameObject g = GameObject.Find(path);
        return g != null ? g.GetComponent(type) : null;
    }

    private static void Force(int tier)
    {
        Component c = Find("World/Landscape/Forest", "ForestDetailTiers");
        c?.GetType().GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance)?.Invoke(c, new object[] { tier });
    }

    private static void Atmosphere(Camera cam)
    {
        Component c = Find("Winter atmosphere", "WinterAtmosphere");
        c?.GetType().GetMethod("Push", BindingFlags.Public | BindingFlags.Instance)?.Invoke(c, new object[] { cam });
    }

    private static float SizeAt(float fit, float level) => fit / (level <= 4 ? 1f + level * 0.15f : 1.6f * Mathf.Pow(1.25f, level - 4));

    public static void Capture(string prefix)
    {
        Directory.CreateDirectory(Dir);
        Camera main = Camera.main;
        Quaternion rot = Quaternion.Euler(38f, 0f, 0f); // the original orthographic map angle, for comparison
        var graph = new List<Renderer>(GameObject.Find("Graph").GetComponentsInChildren<Renderer>());
        float fit = Mathf.Max(126f, CedarCapture.FitSize(rot, graph, 1.6f, 0.04f, out Vector3 centre));
        Vector3 Node(string n) { var go = GameObject.Find(n); return go != null ? go.transform.position : Vector3.zero; }
        Vector3 Bridge(string n)
        {
            var go = GameObject.Find("World/Landscape/Crossings/" + n + "/Bridge");
            return go != null ? go.GetComponent<Renderer>().bounds.center : Vector3.zero;
        }
        Vector3 woods = new Vector3(-68f, 12f, 138f);
        Vector3 camp = Node("N00_Camp");
        var log = new StringBuilder();
        Force(0); Shot(prefix + "_1_overview", rot, centre, fit, 2400, 1500, false);
        Force(1); Shot(prefix + "_2_medium_woods", rot, woods, SizeAt(fit, 6), 1600, 1000, false);
        Force(1); Shot(prefix + "_3_medium_camp_crossings", rot, camp + new Vector3(18, 0, 12), SizeAt(fit, 7), 1600, 1000, false);
        Force(2); Shot(prefix + "_4_close_woods", rot, woods, SizeAt(fit, 11), 1600, 1000, false);
        Force(2); Shot(prefix + "_5_close_camp", rot, camp, SizeAt(fit, 11), 1600, 1000, false);
        Force(2); Shot(prefix + "_6_close_lower_crossing", rot, Bridge("Lower crossing"), SizeAt(fit, 11), 1600, 1000, false);
        Force(2); Shot(prefix + "_7_close_detail", rot, woods + new Vector3(6f, 0f, -4f), SizeAt(fit, 11) * 0.45f, 1600, 1000, false);
        // oblique showcase: perspective, lower pitch, looking up the valley like the reference
        Force(1);
        Shot(prefix + "_8_oblique_valley", Quaternion.Euler(33f, 8f, 0f), camp + new Vector3(-20f, 0f, 70f), 0f, 1920, 1080, true);
        Force(2);
        Shot(prefix + "_9_oblique_close", Quaternion.Euler(36f, 20f, 0f), camp + new Vector3(-10f, 0f, 14f), 0f, 1920, 1080, true, 120f);
        Force(0); // the saved scene keeps the overview tier active, as the builder leaves it
        Atmosphere(main);
    }

    private static void Shot(string name, Quaternion rot, Vector3 target, float size, int w, int h, bool perspective, float distance = 360f)
    {
        var go = new GameObject("__WinterCapture") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(Camera.main);
        cam.orthographic = !perspective;
        if (perspective) cam.fieldOfView = 42f; else cam.orthographicSize = size;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 4000f;
        cam.transform.rotation = rot;
        cam.transform.position = target - (rot * Vector3.forward) * distance;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        Atmosphere(cam);
        // visible scene load for this view (frustum-culled renderers, before batching)
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        long tris = 0; int draws = 0;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            for (int i = 0; i < mf.sharedMesh.subMeshCount; i++) tris += (long)mf.sharedMesh.GetIndexCount(i) / 3;
            draws += mf.sharedMesh.subMeshCount;
        }
        CedarValleyBuilder.Info(name + ": " + draws + " mesh draws, " + (tris / 1000) + "k triangles in view");
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        CedarValleyBuilder.Info("shot " + name);
    }
}
