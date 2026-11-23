using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Chunked forest tiers share identical placements. Close detail adds branchlets and ground
// cover to the medium crowns. Combined meshes keep the draw cost bounded on WebGL.
internal static partial class CedarValleyBuilder
{
    private const string LodDir = "Assets/Art/CedarValley/ForestLOD/";
    public static readonly string[] Variants = { "Ridge_Spruce", "Woodland_Pine", "Wind_Bent_Sapling" };
    private const float SnowU = 0.84375f;

    private static Material matConifer;
    private static MeshBuilder[] midTpl, farTpl, fieldTpl, closeTpl;

    private static void PrepareForestLod()
    {
        // atlas: crisp swatches, no mips, clamped
        string atlasPath = LodDir + "Cedar_Conifer_Atlas.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(atlasPath);
        if (ti != null && (!ti.sRGBTexture || ti.mipmapEnabled || ti.wrapMode != TextureWrapMode.Clamp ||
                           ti.filterMode != FilterMode.Bilinear || ti.textureCompression != TextureImporterCompression.Uncompressed))
        {
            ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        WinterConiferMaterial(atlas);

        int variants = CedarConifers.Forms.Length;
        farTpl = new MeshBuilder[variants];
        midTpl = new MeshBuilder[variants];
        fieldTpl = new MeshBuilder[variants];
        closeTpl = new MeshBuilder[variants];
        for (int v = 0; v < variants; v++)
        {
            farTpl[v] = CedarConifers.Build(v, 0);
            midTpl[v] = CedarConifers.Build(v, 1);
            fieldTpl[v] = CedarConifers.Build(v, 1, simplified: true);
            closeTpl[v] = CedarConifers.Build(v, 2);
            Info($"{CedarConifers.Forms[v].Name}: {Tris(farTpl[v])} far, {Tris(fieldTpl[v])}/{Tris(midTpl[v])} mid, +{Tris(closeTpl[v])} close triangles");
        }
        PrepareBroadleafLod();
    }

    public static void PrepareForestLodPublic()
    {
        CedarTextures.LoadAll();
        Materials();
        PrepareForestLod();
    }

    /// <summary>The twelve-tree layout at Y = 0 in one tier (0 far, 1 mid, 2 close) for the flat matched test.</summary>
    public static Mesh SampleTier(int tier)
    {
        var mb = new MeshBuilder();
        foreach (Placement p in LoadLayout())
        {
            int v = Array.IndexOf(Variants, p.variant);
            Matrix4x4 m = Matrix4x4.TRS(LayoutPos(p), Quaternion.Euler(0f, LayoutYaw(p), 0f), Vector3.one * p.scale);
            mb.Append(tier == 0 ? farTpl[v] : midTpl[v], m);
            if (tier == 2) mb.Append(closeTpl[v], m);
        }
        return mb.ToMesh("flat sample tier " + tier);
    }

    private static int Tris(MeshBuilder mb) { int n = 0; foreach (var s in mb.Sub) n += s.Count / 3; return n; }

    [Serializable] private class Placement { public string variant; public float x, z, scale, yawDegrees; }
    [Serializable] private class Layout { public Placement[] treePlacements; }

    private static Placement[] LoadLayout() =>
        JsonUtility.FromJson<Layout>(File.ReadAllText(LodDir + "Woodland_Patch_Layout.json")).treePlacements;

    /// <summary>Maps an authored layout placement into the imported (Unity) mesh space.</summary>
    private static Vector3 LayoutPos(Placement p) => new Vector3(-p.x, 0f, p.z);
    private static float LayoutYaw(Placement p) => -p.yawDegrees;

    private static void Clump(MeshBuilder mb, Vector3 c, float r, float h, float yaw, Vector2 uv)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        int top = mb.Add(c + Vector3.up * h, Vector3.up, new Color(1, 1, 1, 0), uv);
        int bot = mb.Add(c - Vector3.up * h * 0.5f, Vector3.down, new Color(1, 1, 1, 0), uv);
        int ring = mb.VertexCount;
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            Vector3 d = q * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            mb.Add(c + d * r * (i % 2 == 0 ? 1f : 0.75f), (d + Vector3.up * 0.6f).normalized, new Color(1, 1, 1, 0), uv);
        }
        for (int i = 0; i < 4; i++)
        {
            int a = ring + i, b = ring + (i + 1) % 4;
            mb.Tri(top, b, a);
            mb.Tri(bot, a, b);
        }
    }

    private static readonly Dictionary<int, List<(Vector4 t, int ix, int iy)>> tierChunks = new Dictionary<int, List<(Vector4, int, int)>>();
    private static readonly List<GameObject> farRoots = new List<GameObject>(), midRoots = new List<GameObject>(), closeRoots = new List<GameObject>();

    private static void BuildTierChunks(Transform forest)
    {
        farRoots.Clear(); midRoots.Clear(); closeRoots.Clear();
        long[] sum = new long[3];
        foreach (var (key, placements) in tierChunks)
        {
            var counts = BuildTierChunk(forest, new Vector2Int(key % 100, key / 100), placements);
            for (int i = 0; i < 3; i++) sum[i] += counts[i];
        }
        Type tiersType = Type.GetType("ForestDetailTiers, Assembly-CSharp");
        if (tiersType == null) throw new InvalidOperationException("ForestDetailTiers is missing. Fix the game script compilation first.");
        else
        {
            Component c = forest.gameObject.AddComponent(tiersType);
            var so = new SerializedObject(c);
            void Fill(string prop, List<GameObject> list)
            {
                var sp = so.FindProperty(prop);
                sp.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++) sp.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            }
            Fill("farRoots", farRoots); Fill("midRoots", midRoots); Fill("closeRoots", closeRoots);
            so.FindProperty("overviewTerrain").objectReferenceValue = landscape.Find("Terrain/Overview terrain")?.gameObject;
            so.FindProperty("detailedTerrain").objectReferenceValue = landscape.Find("Terrain/Detailed terrain")?.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Info("Tiered core (" + tierChunks.Count + " chunks): far " + sum[0] + ", mid " + sum[1] +
             ", close " + sum[2] + " tris (scene totals, before frustum culling)");
    }

    private static void AppendTinted(MeshBuilder dst, MeshBuilder src, Matrix4x4 m, Color tint)
    {
        int b = dst.VertexCount;
        dst.Append(src, m);
        for (int i = b; i < dst.VertexCount; i++)
        {
            Color c = dst.C[i];
            dst.C[i] = new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);
        }
    }
}
