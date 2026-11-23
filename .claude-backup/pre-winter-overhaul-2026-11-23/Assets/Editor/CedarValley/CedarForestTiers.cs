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
        matConifer = Mat("CV_ConiferAtlas", "CLSR/Snow Detail");
        matConifer.SetTexture("_MainTex", atlas);
        matConifer.SetTextureScale("_MainTex", Vector2.one);
        matConifer.SetFloat("_MainWorld", 0f);
        matConifer.SetColor("_Color", Color.white);
        SnowOn(matConifer);
        matConifer.SetFloat("_AutoSnow", 0f);

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

    // ---------------------------------------------------------------- placement rules
    private struct TreeTier { public Vector3 Pos; public float Yaw, Scale; public int Variant; public Color Tint; public float Height; }

    /// <summary>Variant, height and tint for an existing seeded placement (stable: depends only on position).</summary>
    private static TreeTier ChooseVariant(Vector4 t, int ix, int iy)
    {
        var p = new Vector2(t.x, t.z);
        float rd = model.RidgeDistance(p, out float crest);
        float slope = model.Slope(Mathf.Clamp(Mathf.RoundToInt((p.x - CedarModel.X0) / CedarModel.Step), 0, model.NX - 1),
                                  Mathf.Clamp(Mathf.RoundToInt((p.y - CedarModel.Z0) / CedarModel.Step), 0, model.NZ - 1));
        float density = model.Sample(model.Forest, p.x, p.y);
        float trail = model.Sample(model.TrailDist, p.x, p.y);
        float meadow = model.MeadowMask(p);
        float r1 = CedarNoise.Rand(ix, iy, 1201), r2 = CedarNoise.Rand(ix, iy, 1202), r3 = CedarNoise.Rand(ix, iy, 1203);
        bool ridge = (crest > 8f && rd < 34f) || slope > 0.38f;
        float edge = Mathf.Max(Mathf.Clamp01((0.42f - density) / 0.25f), Mathf.Max(Mathf.Clamp01((8f - trail) / 4f), meadow));
        int variant;
        if (r1 < .13f + edge * .43f) variant = r3 < .52f ? 2 : 6;
        else if (ridge) variant = r2 < .62f ? 0 : r2 < .87f ? 5 : 3;
        else variant = r2 < .36f ? 1 : r2 < .65f ? 3 : r2 < .81f ? 4 : r2 < .93f ? 5 : 0;
        var form = CedarConifers.Forms[variant];
        float h = form.Height * Mathf.Lerp(.73f, 1.22f, r3) * Mathf.Lerp(.83f, 1.12f, density);
        float g = .86f + .20f * CedarNoise.Rand(ix, iy, 1204);
        var tint = new Color(g * (1.02f - .08f * r3), g, g * (.89f + .13f * r3), 0f);
        float y = float.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f;
            y = Mathf.Min(y, model.Surface(p.x + Mathf.Cos(a) * 0.35f, p.y + Mathf.Sin(a) * 0.35f));
        }
        return new TreeTier
        {
            Pos = new Vector3(p.x, y - 0.08f, p.y), Yaw = CedarNoise.Rand(ix, iy, 908) * 360f,
            Scale = h / form.Height, Variant = variant, Tint = tint, Height = h
        };
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

    private static int[] BuildTierChunk(Transform forest, Vector2Int chunkId, List<(Vector4 t, int ix, int iy)> placements)
    {
        var far = new MeshBuilder();
        var mid = new MeshBuilder();
        var close = new MeshBuilder();
        var shadows = new MeshBuilder();
        var clutter = new MeshBuilder();
        var stones = new MeshBuilder();
        int[] per = new int[CedarConifers.Forms.Length];
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        var stone = CedarMeshes.Boulder(17, 0.6f);
        foreach (var (t, ix, iy) in placements)
        {
            TreeTier tr = ChooseVariant(t, ix, iy);
            per[tr.Variant]++;
            Matrix4x4 m = Matrix4x4.TRS(tr.Pos, Quaternion.Euler(0f, tr.Yaw, 0f), Vector3.one * tr.Scale);
            AppendTinted(far, farTpl[tr.Variant], m, tr.Tint);
            Vector2 p = new Vector2(tr.Pos.x, tr.Pos.z);
            float pathDistance = model.Sample(model.TrailDist, p.x, p.y);
            bool besideTrail = pathDistance <= 14f;
            AppendTinted(mid, (besideTrail ? midTpl : fieldTpl)[tr.Variant], m, tr.Tint);
            if (besideTrail || R(ix, iy, 1299) < .29f) AppendTinted(close, closeTpl[tr.Variant], m, tr.Tint);
            float crown = CedarConifers.Forms[tr.Variant].Radius * tr.Scale;
            CedarMeshes.Shadow(shadows, model, p + shadowDir * tr.Height * .26f, shadowDir,
                tr.Height * .43f, crown, .35f, ShadowTint);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);

            // close-range ground detail: snow wells and dropped branches under the canopy
            float r = CedarNoise.Rand(ix, iy, 1301);
            if ((besideTrail || R(ix, iy, 1300) < .16f) && tr.Height > 5f && r < .46f)
            {
                bool log = r < .09f;
                float a = CedarNoise.Rand(ix, iy, 1302) * Mathf.PI * 2f;
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * crown * 0.9f;
                Vector2 d = new Vector2(-Mathf.Sin(a + 0.7f), Mathf.Cos(a + 0.7f)) * (log ? 1.55f + r * 8f : .55f + r);
                if (model.Sample(model.TrailDist, q.x - d.x, q.y - d.y) < 2.3f ||
                    model.Sample(model.TrailDist, q.x + d.x, q.y + d.y) < 2.3f) continue;
                Vector3 a3 = new Vector3(q.x - d.x, model.Surface(q.x - d.x, q.y - d.y) + (log ? .15f : .04f), q.y - d.y);
                Vector3 b3 = new Vector3(q.x + d.x, model.Surface(q.x + d.x, q.y + d.y) + (log ? .12f : .04f), q.y + d.y);
                int b0 = clutter.VertexCount;
                clutter.Cylinder(a3, b3, log ? .19f : .055f, log ? .13f : .027f, log ? 7 : 4,
                    new Color(.87f, .82f, .74f, 0f), log);
                for (int i = b0; i < clutter.VertexCount; i++) clutter.UV[i] = new Vector2(0.09375f, 0.5f);
            }
            if (besideTrail && tr.Height > 5f && r > .65f)
                for (int k = 0; k < 3; k++)
                {
                    float a = (k + CedarNoise.Rand(ix, iy, 1310 + k)) * Mathf.PI * 2f / 3f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * crown * (1.05f + 0.25f * CedarNoise.Rand(ix, iy, 1320 + k));
                    if (model.Sample(model.TrailDist, q.x, q.y) < 2.2f) continue;
                    float sz = (0.35f + 0.3f * CedarNoise.Rand(ix, iy, 1330 + k)) * tr.Scale;
                    Vector3 ground = new(q.x, model.Surface(q.x, q.y) - .02f, q.y);
                    if (r > .84f && model.SurfaceNormal(q.x, q.y).y > .90f)
                    {
                        // Low evergreen brush on the edge of sheltered, mature stands.
                        Vector3 stem = ground + Vector3.up * .07f;
                        Clump(clutter, stem, sz * .69f, sz * .42f, a * Mathf.Rad2Deg, new Vector2(.40625f, .5f));
                        Clump(clutter, stem + Vector3.up * sz * .15f, sz * .31f, sz * .22f,
                            a * Mathf.Rad2Deg + 35f, new Vector2(SnowU, .5f));
                    }
                    else Clump(clutter, ground, sz, sz * .3f, a * Mathf.Rad2Deg, new Vector2(SnowU, .5f));
                }
        }
        // exposed stones along the trail shoulders inside the chunk
        int stonesN = 0;
        for (float z = minZ; z < maxZ; z += 2.2f)
        for (float x = minX; x < maxX; x += 2.2f)
        {
            int a = Mathf.RoundToInt(x * 7f), b = Mathf.RoundToInt(z * 7f);
            float px = x + (CedarNoise.Rand(a, b, 1401) - 0.5f) * 2f, pz = z + (CedarNoise.Rand(a, b, 1402) - 0.5f) * 2f;
            float td = model.Sample(model.TrailDist, px, pz);
            if (td < 1.45f || td > 3.4f || CedarNoise.Rand(a, b, 1403) > 0.35f) continue;
            float sz = 0.14f + 0.3f * CedarNoise.Rand(a, b, 1404) * CedarNoise.Rand(a, b, 1404);
            stones.Append(stone, Matrix4x4.TRS(new Vector3(px, model.Surface(px, pz) - sz * 0.3f, pz),
                Quaternion.Euler(0f, CedarNoise.Rand(a, b, 1405) * 360f, 0f), new Vector3(sz * 1.2f, sz * 0.7f, sz)));
            stonesN++;
        }

        string n = "Forest " + chunkId.x + "-" + chunkId.y + " (tiers)";
        Mesh mFar = far.ToMesh(n + " far"), mMid = mid.ToMesh(n + " mid"), mClose = close.ToMesh(n + " close"),
             mSh = shadows.ToMesh(n + " shadows"), mCl = clutter.ToMesh(n + " close ground detail"), mSt = stones.ToMesh(n + " close stones");
        var root = new GameObject(n).transform;
        root.SetParent(forest, false);
        GameObject farRoot = new GameObject("Far").gameObject, midRoot = new GameObject("Mid"), closeRoot = new GameObject("Close");
        farRoot.transform.SetParent(root, false); midRoot.transform.SetParent(root, false); closeRoot.transform.SetParent(root, false);
        MeshObject("Trees", farRoot.transform, mFar, matConifer);
        MeshObject("Trees", midRoot.transform, mMid, matConifer);
        MeshObject("Contact shadows", root, mSh, matShadow);
        if (mClose.vertexCount > 0) MeshObject("Bough detail", closeRoot.transform, mClose, matConifer);
        if (mCl.vertexCount > 0) MeshObject("Ground detail", closeRoot.transform, mCl, matConifer);
        if (mSt.vertexCount > 0) MeshObject("Stones", closeRoot.transform, mSt, matRock);
        // One pack per band keeps large generated assets within normal Git file budgets.
        SaveForestMeshes("ForestTierBand" + chunkId.y / 2, new[] { mFar, mMid, mClose, mSh, mCl, mSt });
        midRoot.SetActive(false); closeRoot.SetActive(false);

        farRoots.Add(farRoot); midRoots.Add(midRoot); closeRoots.Add(closeRoot);
        Info("Tier chunk " + chunkId + ": " + placements.Count + " trees by form [" + string.Join(", ", per) +
             "], " + stonesN + " trail stones");
        int baseDetail = Tris(mid) + Tris(shadows);
        return new[] { Tris(far) + Tris(shadows), baseDetail, baseDetail + Tris(close) + Tris(clutter) + Tris(stones) };
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
