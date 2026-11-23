using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Review renders for the forest detail tiers. Use the authored map's fit for sizing
// and read transition thresholds from the scene instead of duplicating their values.
internal static class CedarForestCapture
{
    private static string Dir => Path.Combine(CedarValleyBuilder.OutDir, "forest");
    private static float SizeAt(float fit, float level) => fit / (level <= 4 ? 1f + level * 0.15f : 1.6f * Mathf.Pow(1.25f, level - 4));

    [MenuItem("Tools/Cedar Valley/Forest/Capture Chunk Views (current scene)", false, 40)]
    private static void ChunkMenu() => CedarValleyBuilder.Run("forestshots.txt", () => Chunk(File.Exists(Path.Combine(CedarValleyBuilder.OutDir, "shotprefix.txt"))
        ? File.ReadAllText(Path.Combine(CedarValleyBuilder.OutDir, "shotprefix.txt")).Trim() : "after"));

    private static Component Tiers()
    {
        GameObject f = GameObject.Find("World/Landscape/Forest");
        return f != null ? f.GetComponent("ForestDetailTiers") : null;
    }

    private static void Force(int tier)
    {
        Component c = Tiers();
        if (c == null) return;
        c.GetType().GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance)?.Invoke(c, new object[] { tier });
    }

    private static void Chunk(string prefix)
    {
        Directory.CreateDirectory(Dir);
        Camera main = Camera.main;
        Quaternion rot = main.transform.rotation;
        Vector3 area = new Vector3(-68f, 12f, 138f);
        var graph = new List<Renderer>(GameObject.Find("Graph").GetComponentsInChildren<Renderer>());
        float fit = Mathf.Max(main.orthographicSize, CedarCapture.FitSize(rot, graph, 1.6f, 0.04f, out Vector3 centre));
        Component detail = Tiers();
        bool tiers = detail != null;
        CedarValleyBuilder.Info(prefix + ": tiers component " + (tiers ? "present" : "absent (old LOD groups)"));

        // full map at 1:1 rendered large, plus a crop around the sample chunk as the player sees it
        if (tiers) Force(0);
        Texture2D full = Render(main, rot, centre, fit, 3200, 2000);
        Vector2 uv = ViewportOf(rot, centre, fit, 1.6f, area);
        Texture2D crop = Crop(full, uv, 700, 440);
        Save(full, prefix + "_1_full_map");
        Save(crop, prefix + "_2_full_map_chunk_crop");
        if (tiers) Force(1);
        Save(Render(main, rot, area, SizeAt(fit, 6), 1600, 1000), prefix + "_3_mid_zoom6");
        if (tiers) Force(2);
        Save(Render(main, rot, area, SizeAt(fit, 11), 1600, 1000), prefix + "_4_close_zoom11");
        Save(Render(main, rot, area + new Vector3(6f, 0f, -4f), SizeAt(fit, 11) * 0.45f, 1600, 1000), prefix + "_5_close_detail");
        if (tiers)
        {
            // the same view on either side of each threshold, at the screen size the threshold is defined for
            var settings = new SerializedObject(detail);
            float farMid = 720f / (2f * settings.FindProperty("midAbovePixelsPerMeter").floatValue);
            float midClose = 720f / (2f * settings.FindProperty("closeAbovePixelsPerMeter").floatValue);
            Force(0); Save(Render(main, rot, area, farMid, 1280, 720), prefix + "_6a_threshold_far");
            Force(1); Save(Render(main, rot, area, farMid, 1280, 720), prefix + "_6b_threshold_mid");
            Save(Render(main, rot, area, midClose, 1280, 720), prefix + "_7a_threshold_mid");
            Force(2); Save(Render(main, rot, area, midClose, 1280, 720), prefix + "_7b_threshold_close");
            Force(0);
        }
    }

    private static Vector2 ViewportOf(Quaternion rot, Vector3 centre, float size, float aspect, Vector3 p)
    {
        Vector3 d = Quaternion.Inverse(rot) * (p - centre);
        return new Vector2(0.5f + d.x / (2f * size * aspect), 0.5f + d.y / (2f * size));
    }

    // ---------------------------------------------------------------- flat matched sample
    [MenuItem("Tools/Cedar Valley/Forest/Flat Sample Patch Test", false, 41)]
    private static void FlatMenu() => CedarValleyBuilder.Run("flattest.txt", Flat);

    private static void Flat()
    {
        Directory.CreateDirectory(Dir);
        var made = new List<GameObject>();
        try
        {
            CedarValleyBuilder.PrepareForestLodPublic();
            Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/CedarValley/Generated/Materials/CV_ConiferAtlas.mat");
            Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/CedarValley/Generated/Materials/CV_Ground.mat");
            Vector3 o = new Vector3(3000f, 0f, 3000f);
            GameObject Obj(string n, Mesh m, Material mm, Vector3 at)
            {
                var g = new GameObject("__flat " + n) { hideFlags = HideFlags.DontSave };
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>().sharedMaterial = mm;
                g.transform.position = at;
                made.Add(g);
                return g;
            }
            var quad = new MeshBuilder();
            int a = quad.Add(new Vector3(-40, 0, -30), Vector3.up, new Color(0, 0, 0, 1), Vector2.zero), b = quad.Add(new Vector3(-40, 0, 30), Vector3.up, new Color(0, 0, 0, 1), Vector2.zero);
            int c = quad.Add(new Vector3(40, 0, 30), Vector3.up, new Color(0, 0, 0, 1), Vector2.zero), d = quad.Add(new Vector3(40, 0, -30), Vector3.up, new Color(0, 0, 0, 1), Vector2.zero);
            quad.Quad(a, b, c, d);
            Obj("ground", quad.ToMesh("flat ground"), ground, o);
            Mesh patch = null;
            foreach (UnityEngine.Object x in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/CedarValley/ForestLOD/Woodland_Patch_Far.obj")) if (x is Mesh m) patch = m;
            Mesh mid = CedarValleyBuilder.SampleTier(1), farRebuilt = CedarValleyBuilder.SampleTier(0), close = CedarValleyBuilder.SampleTier(2);
            Camera main = Camera.main;
            Quaternion rot = main.transform.rotation;
            GameObject gp = Obj("patch", patch, mat, o), gm = Obj("mid", mid, mat, o), gf = Obj("far rebuilt", farRebuilt, mat, o), gc = Obj("close", close, mat, o);
            void Only(GameObject g) { foreach (GameObject x in new[] { gp, gm, gf, gc }) x.SetActive(x == g); }
            float fit = main.orthographicSize;
            foreach (var (label, size) in new[] { ("overview", SizeAt(fit, 0)), ("mid", SizeAt(fit, 6)), ("close", SizeAt(fit, 11)) })
            {
                int w = 1280, h = 720;
                Only(gp); Save(Render(main, rot, o + Vector3.up * 4f, size * 0.25f, w, h), "flat_" + label + "_A_patch_far_original");
                Only(gf); Save(Render(main, rot, o + Vector3.up * 4f, size * 0.25f, w, h), "flat_" + label + "_B_far_from_placements");
                Only(gm); Save(Render(main, rot, o + Vector3.up * 4f, size * 0.25f, w, h), "flat_" + label + "_C_mid_models");
                Only(gc); Save(Render(main, rot, o + Vector3.up * 4f, size * 0.25f, w, h), "flat_" + label + "_D_close");
            }
            // silhouette agreement: far-from-placements vs original patch (bounds per tree)
            CedarValleyBuilder.Info("flat test: original patch " + patch.triangles.Length / 3 + " tris / " + patch.vertexCount + " verts; rebuilt far " +
                                    farRebuilt.triangles.Length / 3 + " tris; mid " + mid.triangles.Length / 3 + "; close " + close.triangles.Length / 3);
            CedarValleyBuilder.Info("flat test: bounds patch " + patch.bounds.ToString("F2") + " | rebuilt far " + farRebuilt.bounds.ToString("F2") + " | mid " + mid.bounds.ToString("F2"));
        }
        finally
        {
            foreach (GameObject g in made) if (g != null) UnityEngine.Object.DestroyImmediate(g);
        }
    }

    // ---------------------------------------------------------------- render helpers
    private static Texture2D Render(Camera main, Quaternion rot, Vector3 target, float size, int w, int h)
    {
        var go = new GameObject("__CedarForestCapture") { hideFlags = HideFlags.HideAndDontSave };
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
        RenderTexture.active = null;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(go);
        return tex;
    }

    private static Texture2D Crop(Texture2D src, Vector2 uvCentre, int w, int h)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uvCentre.x * src.width) - w / 2, 0, src.width - w);
        int y = Mathf.Clamp(Mathf.RoundToInt(uvCentre.y * src.height) - h / 2, 0, src.height - h);
        var t = new Texture2D(w, h, TextureFormat.RGB24, false);
        t.SetPixels(src.GetPixels(x, y, w, h));
        t.Apply();
        return t;
    }

    private static void Save(Texture2D t, string name)
    {
        File.WriteAllBytes(Path.Combine(Dir, name + ".png"), t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        CedarValleyBuilder.Info("shot " + name);
    }
}
