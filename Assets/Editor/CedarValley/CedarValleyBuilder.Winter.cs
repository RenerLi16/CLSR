using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Winter art pass: shared winter materials, the mixed frosted forest (broadleaf + conifer +
// understory) with long baked sun shadows, kit material remapping (snow on roofs), the
// runtime atmosphere and quieter trail markings. Gameplay objects are never moved here.
internal static partial class CedarValleyBuilder
{
    private static CedarForestBiome biome;
    private static Material matTwigs, matBark;
    private static MeshBuilder[] decFarWood, decFarCards, decMidWood, decMidCards, decCloseWood, decCloseCards;
    private static MeshBuilder[] spraysTpl;
    private static float shadowK = 2.25f; // shadow length per metre of height (1 / tan(sun elevation))

    // ================================================================== materials
    private static Material WMat(string name, string shader) => Mat("CVW_" + name, shader);

    private static void WinterMaterials()
    {
        CedarWinterTextures.EnsureAll();
        biome = CedarForestBiome.LoadOrCreate();
        shadowK = 1f / Mathf.Tan(biome.shadowSunElevation * Mathf.Deg2Rad);

        matGround = WMat("Ground", "CLSR/Winter Ground");
        matGround.SetTexture("_SnowTex", CedarTextures.FreshSnow); matGround.SetFloat("_SnowTile", 8f);
        matGround.SetTexture("_SnowNrm", CedarWinterTextures.SnowNrm);
        matGround.SetTexture("_PackedTex", CedarTextures.PackedSnow); matGround.SetFloat("_PackedTile", 4f);
        matGround.SetTexture("_SoilTex", CedarWinterTextures.Soil); matGround.SetFloat("_SoilTile", 5f);
        matGround.SetTexture("_RockTex", CedarWinterTextures.Rock); matGround.SetFloat("_RockTile", 7f);
        matGround.SetTexture("_RockNrm", CedarWinterTextures.RockNrm);
        matGround.SetTexture("_MacroTex", CedarTextures.Macro); matGround.SetFloat("_MacroTile", 180f);
        matGround.SetColor("_RockTint", new Color(.96f, .96f, .97f));
        matGround.SetColor("_SnowColor", new Color(.93f, .94f, .95f));
        matGround.SetFloat("_NormalStrength", .4f);
        matGround.SetColor("_ShadeSnow", new Color(.8f, .82f, .86f));

        matIce = WMat("RiverIce", "CLSR/Winter River Ice");
        matIce.SetTexture("_MainTex", CedarTextures.Ice);
        matIce.SetTexture("_MacroTex", CedarTextures.Macro);
        matIce.SetTexture("_SnowTex", CedarTextures.FreshSnow);
        matIce.SetTexture("_SnowNrm", CedarWinterTextures.SnowNrm);
        matIce.SetColor("_IceDeep", new Color(.5f, .56f, .6f));
        matIce.SetColor("_IcePale", new Color(.78f, .81f, .84f));

        matTrailbed = WMat("Trailbed", "CLSR/Winter Decal");
        matTrailbed.SetTexture("_MainTex", CedarWinterTextures.TrailEarth); matTrailbed.SetFloat("_MainWorld", 0f);
        matTrailbed.SetTexture("_NoiseTex", CedarTextures.Macro); matTrailbed.SetFloat("_NoiseTile", 7f);
        matTrailbed.SetFloat("_EdgeBreak", 1.1f);
        matTrailbed.SetColor("_Color", Color.white);

        matTrampled = WMat("TrampledSnow", "CLSR/Winter Decal");
        matTrampled.SetTexture("_MainTex", CedarTextures.PackedSnow); matTrampled.SetFloat("_MainWorld", 1f); matTrampled.SetFloat("_MainTile", 4f);
        matTrampled.SetTexture("_NoiseTex", CedarTextures.Macro); matTrampled.SetFloat("_NoiseTile", 5f);
        matTrampled.SetColor("_Color", new Color(0.95f, 0.93f, 0.9f, 0.9f));

        Material Lit(string name, Texture tex, bool world, float tile, float autoSnow, float start)
        {
            Material m = WMat(name, "CLSR/Winter Lit");
            m.SetTexture("_MainTex", tex); m.SetFloat("_MainWorld", world ? 1f : 0f); m.SetFloat("_MainTile", tile);
            m.SetTexture("_SnowTex", CedarTextures.FreshSnow); m.SetFloat("_SnowTile", 8f);
            m.SetFloat("_AutoSnow", autoSnow); m.SetFloat("_AutoSnowStart", start);
            m.SetFloat("_UseVertexColor", 1f);
            return m;
        }
        matSpruce = Lit("Spruce", CedarTextures.Needles, false, 1f, 0f, .7f); matSpruce.SetTextureScale("_MainTex", new Vector2(3f, 4f));
        matRock = Lit("Rock", CedarWinterTextures.Rock, true, 3f, .85f, .62f);
        matFireStone = Lit("FireRingStone", CedarWinterTextures.Rock, true, 2f, 0f, .7f);
        matFireStone.SetColor("_Color", new Color(.72f, .7f, .68f));
        matTimber = Lit("BridgeTimber", KitTex("OldTimber"), false, 1f, .2f, .8f);
        matLogs = Lit("BridgeLogs", CedarWinterTextures.Bark, false, 1f, .6f, .75f);
        matShard = Lit("IceShards", CedarTextures.Ice, true, 4f, 0f, .7f);
        matShard.SetColor("_Color", new Color(.78f, .81f, .84f));

        matShadow = WMat("CastShadow", "CLSR/Winter Cast Shadow");
        matShadow.SetTexture("_MainTex", CedarWinterTextures.ShadowAtlas); matShadow.SetFloat("_Strength", 1f);

        matBark = Lit("Bark", CedarWinterTextures.Bark, false, 1f, 0f, .7f);
        matBark.SetTextureScale("_MainTex", new Vector2(.35f, 1.6f));
        matBark.SetFloat("_Backlight", .45f);
        matTwigs = WMat("FrostedTwigs", "CLSR/Winter Foliage");
        matTwigs.SetTexture("_MainTex", CedarWinterTextures.Foliage);
        matTwigs.SetFloat("_Cutoff", .42f);
        matTwigs.SetFloat("_Backlight", .3f);
        matTwigs.SetFloat("_Wrap", .3f);
        AssetDatabase.SaveAssets();
    }

    /// <summary>Conifer atlas material under the winter light (replaces the old Snow Detail one).</summary>
    private static void WinterConiferMaterial(Texture atlas)
    {
        matConifer = WMat("ConiferAtlas", "CLSR/Winter Lit");
        matConifer.SetTexture("_MainTex", atlas);
        matConifer.SetTextureScale("_MainTex", Vector2.one);
        matConifer.SetFloat("_MainWorld", 0f);
        matConifer.SetColor("_Color", new Color(1.02f, 1.02f, 1.0f));
        matConifer.SetTexture("_SnowTex", CedarTextures.FreshSnow);
        matConifer.SetFloat("_AutoSnow", 0f);
        matConifer.SetFloat("_UseVertexColor", 1f);
        matConifer.SetFloat("_Wrap", .25f);
        matConifer.SetFloat("_Backlight", .3f);
    }

    private static void PrepareBroadleafLod()
    {
        int n = CedarDeciduous.Forms.Length;
        decFarWood = new MeshBuilder[n]; decFarCards = new MeshBuilder[n];
        decMidWood = new MeshBuilder[n]; decMidCards = new MeshBuilder[n];
        decCloseWood = new MeshBuilder[n]; decCloseCards = new MeshBuilder[n];
        for (int v = 0; v < n; v++)
        {
            CedarDeciduous.Build(v, 0, out decFarWood[v], out decFarCards[v]);
            CedarDeciduous.Build(v, 1, out decMidWood[v], out decMidCards[v]);
            CedarDeciduous.Build(v, 2, out decCloseWood[v], out decCloseCards[v]);
            Info($"{CedarDeciduous.Forms[v].Name}: far {Tris(decFarWood[v]) + Tris(decFarCards[v])}, mid {Tris(decMidWood[v]) + Tris(decMidCards[v])}, +close {Tris(decCloseWood[v]) + Tris(decCloseCards[v])} tris");
        }
        spraysTpl = new MeshBuilder[CedarConifers.Forms.Length];
        for (int v = 0; v < spraysTpl.Length; v++) spraysTpl[v] = CedarConifers.BuildSprays(v);
    }

    // ================================================================== kit props
    private static readonly Dictionary<Material, Material> kitWinter = new Dictionary<Material, Material>();

    /// <summary>Cedar Valley copies of the Winter Outpost materials under the winter light, with
    /// snow settling on roofs and other upward faces. The kit's own materials stay untouched.</summary>
    private static void RemapKitMaterials()
    {
        kitWinter.Clear();
        int swapped = 0;
        foreach (GameObject go in kitPlaced)
        {
            if (go == null) continue;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material src = mats[i];
                    if (src == null || src.shader == null || src.shader.name.StartsWith("CLSR/")) continue;
                    if (!kitWinter.TryGetValue(src, out Material w))
                    {
                        w = WMat("Kit_" + src.name, "CLSR/Winter Lit");
                        if (src.HasProperty("_MainTex")) { w.SetTexture("_MainTex", src.GetTexture("_MainTex")); w.SetTextureScale("_MainTex", src.GetTextureScale("_MainTex")); }
                        if (src.HasProperty("_Color")) w.SetColor("_Color", src.GetColor("_Color"));
                        w.SetFloat("_UseVertexColor", 0f);
                        w.SetTexture("_SnowTex", CedarTextures.FreshSnow);
                        string n = src.name.ToLowerInvariant();
                        bool glassOrMetal = n.Contains("glass") || n.Contains("steel") || n.Contains("iron");
                        w.SetFloat("_AutoSnow", glassOrMetal ? .25f : .9f);
                        w.SetFloat("_AutoSnowStart", n.Contains("canvas") ? .5f : .58f);
                        w.SetFloat("_Wrap", .15f);
                        kitWinter[src] = w;
                    }
                    mats[i] = w;
                    swapped++;
                }
                r.sharedMaterials = mats;
            }
        }
        Info("kit props: " + swapped + " material slots moved to " + kitWinter.Count + " winter materials (snow on roofs and ledges)");
    }

    // ================================================================== atmosphere
    /// <summary>The Cedar Valley map camera becomes a perspective camera at the showcase angle
    /// (33 degrees down, turned slightly up-valley). GraphCameraFraming fits, zooms (dolly) and
    /// pans it; overlays and forest tiers size themselves per distance.</summary>
    public static void UsePerspectiveMapCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) { Info("WARNING: no main camera"); return; }
        cam.orthographic = false;
        cam.fieldOfView = 42f;
        cam.nearClipPlane = 1.5f;
        cam.farClipPlane = 3200f;
        cam.transform.rotation = Quaternion.Euler(33f, 8f, 0f);
        Info("map camera: perspective, 42 deg FOV, 33 deg pitch, 8 deg yaw");
    }

    private static void SetupAtmosphere()
    {
        UsePerspectiveMapCamera();
        Type t = Type.GetType("WinterAtmosphere, Assembly-CSharp");
        if (t == null) { Info("WARNING: WinterAtmosphere type not found"); return; }
        GameObject host = GameObject.Find("Winter atmosphere") ?? new GameObject("Winter atmosphere");
        if (host.GetComponent(t) == null) host.AddComponent(t);
        Camera cam = Camera.main;
        if (cam != null) cam.backgroundColor = new Color(0.855f, 0.875f, 0.895f);
        Type styler = Type.GetType("MapLabelStyler, Assembly-CSharp");
        GameObject overlays = GameObject.Find("Map overlays");
        if (styler != null && overlays != null && overlays.GetComponent(styler) == null) overlays.AddComponent(styler);
        Info("winter atmosphere: board haze, zoom-dependent depth haze, pale winter sky; compact label plaques");
    }

    /// <summary>The graph's trail strokes keep their points (routes read them) but now read as a
    /// faint worn line inside the earth trail bed instead of a grey tube.</summary>
    private static void SoftenTrailMarkings()
    {
        Transform trails = GameObject.Find("Graph/Trails")?.transform;
        if (trails == null) return;
        var col = new Color(0.36f, 0.3f, 0.24f, 0.22f);
        int n = 0;
        foreach (LineRenderer lr in trails.GetComponentsInChildren<LineRenderer>(true))
        {
            lr.startColor = col; lr.endColor = col;
            lr.widthCurve = AnimationCurve.Constant(0f, 1f, 0.5f);
            n++;
        }
        Info("trail markings softened: " + n);
    }

    // ================================================================== forest
    private struct TreePick
    {
        public int Kind;      // 0 conifer, 1 broadleaf
        public int Variant;
        public Vector3 Pos;
        public float Yaw, Scale, Height, Crown, CrownBase;
        public Color Tint;
    }

    private static float S01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    private static float RiverEdge(Vector2 p) =>
        model.RiverInfo(p, out int k, out float side) - model.River.HalfW[k] - model.BankOffset(k, side);

    /// <summary>Species, form, height and tint for a seeded placement (depends only on position and index).</summary>
    private static TreePick ChooseTree(Vector4 t, int ix, int iy)
    {
        var p = new Vector2(t.x, t.z);
        float rd = model.RidgeDistance(p, out float crest);
        float slope = model.Slope(Mathf.Clamp(Mathf.RoundToInt((p.x - CedarModel.X0) / CedarModel.Step), 0, model.NX - 1),
                                  Mathf.Clamp(Mathf.RoundToInt((p.y - CedarModel.Z0) / CedarModel.Step), 0, model.NZ - 1));
        float density = model.Sample(model.Forest, p.x, p.y);
        float trail = model.Sample(model.TrailDist, p.x, p.y);
        float meadow = model.MeadowMask(p);
        float river = RiverEdge(p);
        float elevation = model.Surface(p.x, p.y);
        float r1 = CedarNoise.Rand(ix, iy, 1201), r2 = CedarNoise.Rand(ix, iy, 1202), r3 = CedarNoise.Rand(ix, iy, 1203);
        float r4 = CedarNoise.Rand(ix, iy, 1205);
        float edge = Mathf.Max(Mathf.Clamp01((0.42f - density) / 0.25f), Mathf.Max(Mathf.Clamp01((8f - trail) / 4f), meadow));

        float share = biome.valleyBroadleafShare;
        share *= 1f - S01((elevation - biome.broadleafElevation) / Mathf.Max(1f, biome.broadleafFade));
        share *= 1f - .75f * S01((slope - biome.coniferSlope) / .25f);
        if (crest > 8f && rd < 30f) share *= .35f;
        share += biome.riparianBoost * CedarNoise.Gauss(river / 30f);
        float stand = CedarNoise.Fbm(p.x / biome.standScale + 31f, p.y / biome.standScale, 1250, 3);
        share = Mathf.Clamp01(share + (stand - .5f) * 2.2f * biome.standContrast);

        var pick = new TreePick { Yaw = CedarNoise.Rand(ix, iy, 908) * 360f };
        float g = .9f + .16f * CedarNoise.Rand(ix, iy, 1204);
        if (r4 < share)
        {
            pick.Kind = 1;
            int v;
            if (r1 < biome.saplingShare * (.4f + edge)) v = 3;
            else if (river < 26f && r2 < .55f) v = 5;
            else v = r2 < .34f ? 0 : r2 < .62f ? 2 : r2 < .92f ? 1 : 4;
            pick.Variant = v;
            var form = CedarDeciduous.Forms[v];
            float h = form.Height * Mathf.Lerp(.74f, 1.18f, r3) * Mathf.Lerp(.84f, 1.08f, density) * biome.broadleafHeightScale;
            if (edge > .5f && v != 3) h *= .88f;
            pick.Scale = h / form.Height;
            pick.Height = h;
            pick.Crown = CedarDeciduous.CrownRadius(v) * pick.Scale;
            pick.CrownBase = h * Mathf.Max(.2f, form.SplitAt * .8f);
            pick.Tint = new Color(g * (1.01f - .05f * r3), g, g * (.97f + .06f * r3), 0f);
        }
        else
        {
            bool ridge = (crest > 8f && rd < 34f) || slope > .38f;
            int variant;
            if (r1 < .10f + edge * biome.saplingShare * 2.2f) variant = r3 < .52f ? 2 : 6;
            else if (ridge) variant = r2 < .62f ? 0 : r2 < .87f ? 5 : 3;
            else variant = r2 < .36f ? 1 : r2 < .65f ? 3 : r2 < .81f ? 4 : r2 < .93f ? 5 : 0;
            var form = CedarConifers.Forms[variant];
            float h = form.Height * Mathf.Lerp(.73f, 1.22f, r3) * Mathf.Lerp(.83f, 1.12f, density);
            pick.Kind = 0;
            pick.Variant = variant;
            pick.Scale = h / form.Height;
            pick.Height = h;
            pick.Crown = form.Radius * pick.Scale;
            pick.CrownBase = h * form.CrownStart;
            pick.Tint = new Color(g * (1.02f - .08f * r3), g, g * (.89f + .13f * r3), 0f);
        }
        float y = float.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.PI * 0.5f;
            y = Mathf.Min(y, model.Surface(p.x + Mathf.Cos(a) * 0.35f, p.y + Mathf.Sin(a) * 0.35f));
        }
        pick.Pos = new Vector3(p.x, y - 0.08f, p.y);
        return pick;
    }

    // Crowns may overlap (canopies interlock); edge trees keep more room than interior ones.
    private static float SpacingRadius(TreePick t, float density) => t.Kind == 0
        ? Mathf.Clamp(t.Crown * .6f + Mathf.Lerp(1.0f, .35f, density), 1.2f, 3.0f)
        : Mathf.Clamp(t.Crown * .42f + Mathf.Lerp(1.1f, .4f, density), 1.3f, 3.8f);

    private static void BuildForest()
    {
        Transform g = Group("Forest");
        trees.Clear();
        tierChunks.Clear();
        const float spacingCell = 4f, chunk = 64f;
        var spacing = new Dictionary<Vector2Int, List<(Vector2 point, float radius)>>();
        Vector2Int Cell(Vector2 p) => new(Mathf.FloorToInt(p.x / spacingCell), Mathf.FloorToInt(p.y / spacingCell));
        int broad = 0;
        // Seeded uniform proposals with variable-radius rejection: dense interiors, sparse edges,
        // and broad crowns that need (and get) more room than spire-shaped conifers.
        for (int sample = 0; sample < 140000; sample++)
        {
            float x = Mathf.Lerp(CedarModel.X0 + 2f, CedarModel.X1 - 2f, R(sample, 11, 901));
            float z = Mathf.Lerp(CedarModel.Z0 + 2f, CedarModel.Z1 - 2f, R(sample, 21, 902));
            var p = new Vector2(x, z);
            float f = model.Sample(model.Forest, x, z);
            // dense, continuous stands inside the woodland field; sparse transitional edges
            float establishment = Mathf.Clamp01(f * Mathf.Lerp(.9f, 2.6f, Mathf.SmoothStep(0f, 1f, f * 2.2f)));
            if (f < .016f || R(sample, 31, 903) > establishment) continue;
            if (model.Sample(model.TrailDist, x, z) < 4.1f || model.SurfaceNormal(x, z).y < .83f) continue;
            if (RiverEdge(p) < 5.5f) continue;
            var candidate = new Vector4(x, model.Surface(x, z), z, 0f);
            TreePick tree = ChooseTree(candidate, sample, 37);
            float radius = SpacingRadius(tree, f);
            Vector2Int cell = Cell(p);
            bool clear = true;
            for (int j = -2; j <= 2 && clear; j++)
            for (int i = -2; i <= 2 && clear; i++)
            {
                if (!spacing.TryGetValue(cell + new Vector2Int(i, j), out var neighbors)) continue;
                foreach (var neighbor in neighbors)
                {
                    float separation = radius + neighbor.radius;
                    if ((p - neighbor.point).sqrMagnitude >= separation * separation) continue;
                    clear = false;
                    break;
                }
            }
            if (!clear) continue;
            if (!spacing.TryGetValue(cell, out var occupants)) spacing[cell] = occupants = new();
            occupants.Add((p, radius));
            if (tree.Kind == 1) broad++;
            int key = Mathf.FloorToInt((x - CedarModel.X0) / chunk) + 100 * Mathf.FloorToInt((z - CedarModel.Z0) / chunk);
            trees.Add(new Vector4(x, tree.Pos.y, z, tree.Height));
            if (!tierChunks.TryGetValue(key, out var placements)) tierChunks[key] = placements = new();
            placements.Add((candidate, sample, 37));
        }
        BuildTierChunks(g);

        // Surrounding woodland outside the board (overview tier meshes only, culled in batches):
        // broadleaf in the low valley ends, conifers on the rising slopes, all fading into haze.
        var surround = new Dictionary<Vector2Int, (MeshBuilder con, MeshBuilder wood, MeshBuilder cards)>();
        int fc = 0;
        for (float z = -230f; z < 440f; z += 6f)
        for (float x = -320f; x < 320f; x += 6f)
        {
            int a = Mathf.RoundToInt(x), b = Mathf.RoundToInt(z);
            float jx = x + (R(a, b, 911) - 0.5f) * 5f, jz = z + (R(a, b, 912) - 0.5f) * 5f;
            if (jx > CedarModel.X0 - 2f && jx < CedarModel.X1 + 2f && jz > CedarModel.Z0 - 2f && jz < CedarModel.Z1 + 2f) continue;
            var point = new Vector2(jx, jz);
            float edge = RiverEdge(point);
            float y = CedarMeshes.ApronSurface(model, jx, jz);
            float up = model.SurfaceNormal(jx, jz).y;
            float d = model.WoodlandClimate(point, y, Mathf.Sqrt(Mathf.Max(0f, 1f - up * up)) / Mathf.Max(.1f, up), edge);
            if (R(a, b, 913) > d * d * .74f) continue;
            var key = new Vector2Int(Mathf.FloorToInt(jx / 128f), Mathf.FloorToInt(jz / 128f));
            if (!surround.TryGetValue(key, out var batch))
                surround[key] = batch = (new MeshBuilder(), new MeshBuilder(), new MeshBuilder());
            Quaternion rot = Quaternion.Euler(0f, R(a, b, 915) * 360f, 0f);
            float share = biome.valleyBroadleafShare * (1f - S01((y - biome.broadleafElevation) / biome.broadleafFade)) +
                          (CedarNoise.Fbm(jx / biome.standScale, jz / biome.standScale, 1250, 3) - .5f) * 1.6f * biome.standContrast;
            if (R(a, b, 918) < share)
            {
                int v = R(a, b, 919) < .4f ? 0 : R(a, b, 919) < .7f ? 2 : 1;
                float h = CedarDeciduous.Forms[v].Height * Mathf.Lerp(.75f, 1.1f, R(a, b, 914));
                Matrix4x4 m = Matrix4x4.TRS(new Vector3(jx, y - 0.2f, jz), rot, Vector3.one * (h / CedarDeciduous.Forms[v].Height));
                batch.wood.Append(decFarWood[v], m);
                batch.cards.Append(decFarCards[v], m);
            }
            else
            {
                float h = Mathf.Lerp(7f, 12f, R(a, b, 914));
                int variant = Mathf.Min(CedarConifers.Forms.Length - 1, (int)(R(a, b, 917) * CedarConifers.Forms.Length));
                if (variant is 2 or 6) h *= .55f;
                batch.con.Append(farTpl[variant], Matrix4x4.TRS(new Vector3(jx, y - 0.2f, jz), rot, Vector3.one * (h / CedarConifers.Forms[variant].Height)));
            }
            fc++;
        }
        var outskirts = Group("Surrounding woodland", g);
        var surroundMeshes = new List<Mesh>(surround.Count * 3);
        foreach (var (key, batch) in surround)
        {
            string n = "Woodland slope " + key.x + ", " + key.y;
            Mesh con = batch.con.ToMesh(n), wood = batch.wood.ToMesh(n + " broadleaf wood"), cards = batch.cards.ToMesh(n + " broadleaf crowns");
            surroundMeshes.Add(con); surroundMeshes.Add(wood); surroundMeshes.Add(cards);
            if (con.vertexCount > 0) MeshObject(n, outskirts, con, matConifer);
            if (wood.vertexCount > 0) MeshObject(n + " broadleaf wood", outskirts, wood, matBark);
            if (cards.vertexCount > 0) MeshObject(n + " broadleaf crowns", outskirts, cards, matTwigs);
        }
        if (surroundMeshes.Count > 0) SaveForestMeshes("ForestSurroundTiers", surroundMeshes);
        Info("forest: " + trees.Count + " trees (" + broad + " frosted broadleaf) in " + tierChunks.Count + " tiered chunks; surround " + fc + " trees in " + surround.Count + " batches");
    }

    /// <summary>Long baked cast shadow of a tree crown and trunk along the low sun, plus a soft contact blob.</summary>
    private static void TreeShadow(MeshBuilder cast, MeshBuilder contact, Vector2 p, TreePick t)
    {
        Vector2 dir = shadowDir;
        float top = t.Height, bottom = Mathf.Min(t.CrownBase, top * .8f);
        float centre = (top + bottom) * .5f * shadowK;
        float half = ((top - bottom) * shadowK + 2f * t.Crown) * .5f * (t.Kind == 0 ? .82f : .9f);
        float width = t.Crown * (t.Kind == 0 ? .78f : .92f);
        float strength = biome.shadowStrength * (t.Kind == 0 ? 1f : .82f);
        CedarMeshes.Shadow(cast, model, p + dir * centre, dir, half, width, strength, ShadowTint, t.Kind == 0 ? 0 : 1);
        // trunk shadow from the stem foot to the crown shadow
        float trunkLen = bottom * shadowK * .5f;
        if (trunkLen > .6f)
            CedarMeshes.Shadow(contact, model, p + dir * trunkLen, dir, trunkLen, Mathf.Max(.22f, t.Height * .02f), strength * .8f, ShadowTint, 0);
        CedarMeshes.Shadow(contact, model, p, dir, t.Crown * .55f, t.Crown * .55f, .22f, ShadowTint, 0);
    }

    private static int[] BuildTierChunk(Transform forest, Vector2Int chunkId, List<(Vector4 t, int ix, int iy)> placements)
    {
        var far = new MeshBuilder();
        var farWood = new MeshBuilder(); var farCards = new MeshBuilder();
        var mid = new MeshBuilder();
        var midWood = new MeshBuilder(); var midCards = new MeshBuilder();
        var close = new MeshBuilder();
        var closeWood = new MeshBuilder(); var closeCards = new MeshBuilder();
        var shadows = new MeshBuilder();
        var contact = new MeshBuilder();
        var clutter = new MeshBuilder();
        var stones = new MeshBuilder();
        int conifers = 0, broadleaf = 0, shrubs = 0;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        var stone = CedarMeshes.Boulder(17, 0.6f);
        foreach (var (t, ix, iy) in placements)
        {
            TreePick tr = ChooseTree(t, ix, iy);
            Matrix4x4 m = Matrix4x4.TRS(tr.Pos, Quaternion.Euler(0f, tr.Yaw, 0f), Vector3.one * tr.Scale);
            Vector2 p = new Vector2(tr.Pos.x, tr.Pos.z);
            float pathDistance = model.Sample(model.TrailDist, p.x, p.y);
            bool besideTrail = pathDistance <= 14f;
            if (tr.Kind == 0)
            {
                conifers++;
                AppendTinted(far, farTpl[tr.Variant], m, tr.Tint);
                AppendTinted(mid, (besideTrail ? midTpl : fieldTpl)[tr.Variant], m, tr.Tint);
                // needle sprays break the solid boughs into snowy foliage wherever mid zoom shows them
                AppendTinted(besideTrail ? midCards : closeCards, spraysTpl[tr.Variant], m, tr.Tint);
                if (besideTrail || R(ix, iy, 1299) < .29f)
                    AppendTinted(close, closeTpl[tr.Variant], m, tr.Tint);
            }
            else
            {
                broadleaf++;
                int v = tr.Variant;
                AppendTinted(farWood, decFarWood[v], m, tr.Tint);
                AppendTinted(farCards, decFarCards[v], m, tr.Tint);
                AppendTinted(midWood, decMidWood[v], m, tr.Tint);
                AppendTinted(midCards, decMidCards[v], m, tr.Tint);
                if (besideTrail || R(ix, iy, 1299) < .6f)
                {
                    AppendTinted(closeWood, decCloseWood[v], m, tr.Tint);
                    AppendTinted(closeCards, decCloseCards[v], m, tr.Tint);
                }
            }
            TreeShadow(shadows, contact, p, tr);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y);

            // understory: frosted shrubs at stand edges and beside trails (mid and close zoom)
            float density = model.Sample(model.Forest, p.x, p.y);
            float edge = Mathf.Max(Mathf.Clamp01((.45f - density) / .3f), Mathf.Clamp01((12f - pathDistance) / 8f));
            if (R(ix, iy, 1360) < biome.shrubDensity * (.25f + edge))
            {
                float a = R(ix, iy, 1361) * Mathf.PI * 2f;
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (tr.Crown * (1.0f + .7f * R(ix, iy, 1362)) + .8f);
                if (model.Sample(model.TrailDist, q.x, q.y) > 2.6f && RiverEdge(q) > 1.5f && model.SurfaceNormal(q.x, q.y).y > .8f)
                {
                    int sv = R(ix, iy, 1363) < .55f ? 6 : 7;
                    float s = Mathf.Lerp(.7f, 1.25f, R(ix, iy, 1364));
                    Matrix4x4 sm = Matrix4x4.TRS(new Vector3(q.x, model.Surface(q.x, q.y) - .05f, q.y), Quaternion.Euler(0f, R(ix, iy, 1365) * 360f, 0f), Vector3.one * s);
                    Color st = new Color(.95f + .08f * R(ix, iy, 1366), .95f, .93f, 0f);
                    AppendTinted(midWood, decMidWood[sv], sm, st);
                    AppendTinted(midCards, decMidCards[sv], sm, st);
                    AppendTinted(closeCards, decCloseCards[sv], sm, st);
                    CedarMeshes.Shadow(contact, model, q + shadowDir * CedarDeciduous.Forms[sv].Height * s * shadowK * .45f, shadowDir,
                        CedarDeciduous.Forms[sv].Height * s * shadowK * .5f, CedarDeciduous.CrownRadius(sv) * s, biome.shadowStrength * .7f, ShadowTint, 1);
                    shrubs++;
                }
            }

            // close-range ground detail: dropped branches and fallen logs under the canopy
            float r = CedarNoise.Rand(ix, iy, 1301);
            float crown = tr.Crown;
            if ((besideTrail || R(ix, iy, 1300) < .16f) && tr.Height > 5f && r < .46f)
            {
                bool log = r < .09f;
                float a = CedarNoise.Rand(ix, iy, 1302) * Mathf.PI * 2f;
                Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * crown * 0.9f;
                Vector2 d = new Vector2(-Mathf.Sin(a + 0.7f), Mathf.Cos(a + 0.7f)) * (log ? 1.55f + r * 8f : .55f + r);
                if (model.Sample(model.TrailDist, q.x - d.x, q.y - d.y) >= 2.3f && model.Sample(model.TrailDist, q.x + d.x, q.y + d.y) >= 2.3f)
                {
                    Vector3 a3 = new Vector3(q.x - d.x, model.Surface(q.x - d.x, q.y - d.y) + (log ? .15f : .04f), q.y - d.y);
                    Vector3 b3 = new Vector3(q.x + d.x, model.Surface(q.x + d.x, q.y + d.y) + (log ? .12f : .04f), q.y + d.y);
                    int b0 = clutter.VertexCount;
                    clutter.Cylinder(a3, b3, log ? .19f : .055f, log ? .13f : .027f, log ? 7 : 4, new Color(.87f, .82f, .74f, 0f), log);
                    for (int i = b0; i < clutter.VertexCount; i++)
                    {
                        clutter.UV[i] = new Vector2(0.09375f, 0.5f);
                        Color c = clutter.C[i]; c.a = .85f * Mathf.SmoothStep(0f, 1f, clutter.N[i].y); clutter.C[i] = c; // snow along the top
                    }
                    if (log) CedarMeshes.Shadow(contact, model, q + shadowDir * .35f, shadowDir, d.magnitude * .9f + .3f, .45f, .35f, ShadowTint, 0);
                }
            }
            if (besideTrail && tr.Height > 5f && r > .65f && tr.Kind == 0)
                for (int k = 0; k < 3; k++)
                {
                    float a = (k + CedarNoise.Rand(ix, iy, 1310 + k)) * Mathf.PI * 2f / 3f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * crown * (1.05f + 0.25f * CedarNoise.Rand(ix, iy, 1320 + k));
                    if (model.Sample(model.TrailDist, q.x, q.y) < 2.2f) continue;
                    float sz = (0.35f + 0.3f * CedarNoise.Rand(ix, iy, 1330 + k)) * tr.Scale;
                    Vector3 ground = new(q.x, model.Surface(q.x, q.y) - .02f, q.y);
                    if (r > .84f && model.SurfaceNormal(q.x, q.y).y > .90f)
                    {
                        Vector3 stem = ground + Vector3.up * .07f;
                        Clump(clutter, stem, sz * .69f, sz * .42f, a * Mathf.Rad2Deg, new Vector2(.40625f, .5f));
                        Clump(clutter, stem + Vector3.up * sz * .15f, sz * .31f, sz * .22f, a * Mathf.Rad2Deg + 35f, new Vector2(SnowU, .5f));
                    }
                    else Clump(clutter, ground, sz, sz * .3f, a * Mathf.Rad2Deg, new Vector2(SnowU, .5f));
                }
        }
        // exposed, snow-capped stones along the trail shoulders inside the chunk
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
        var meshes = new List<Mesh>();
        Mesh M(MeshBuilder b, string suffix) { Mesh mesh = b.ToMesh(n + " " + suffix); meshes.Add(mesh); return mesh; }
        Mesh mFar = M(far, "far"), mFarWood = M(farWood, "far broadleaf wood"), mFarCards = M(farCards, "far broadleaf crowns");
        Mesh mMid = M(mid, "mid"), mMidWood = M(midWood, "mid broadleaf wood"), mMidCards = M(midCards, "mid broadleaf crowns");
        Mesh mClose = M(close, "close"), mCloseWood = M(closeWood, "close broadleaf wood"), mCloseCards = M(closeCards, "close crown cards");
        Mesh mSh = M(shadows, "cast shadows"), mCt = M(contact, "contact shadows"), mCl = M(clutter, "close ground detail"), mSt = M(stones, "close stones");
        var root = new GameObject(n).transform;
        root.SetParent(forest, false);
        GameObject farRoot = new GameObject("Far"), midRoot = new GameObject("Mid"), closeRoot = new GameObject("Close");
        farRoot.transform.SetParent(root, false); midRoot.transform.SetParent(root, false); closeRoot.transform.SetParent(root, false);
        void Obj(string name, Transform parent, Mesh mesh, Material mat) { if (mesh.vertexCount > 0) MeshObject(name, parent, mesh, mat); }
        Obj("Conifers", farRoot.transform, mFar, matConifer);
        Obj("Broadleaf wood", farRoot.transform, mFarWood, matBark);
        Obj("Broadleaf crowns", farRoot.transform, mFarCards, matTwigs);
        Obj("Conifers", midRoot.transform, mMid, matConifer);
        Obj("Broadleaf wood", midRoot.transform, mMidWood, matBark);
        Obj("Broadleaf crowns", midRoot.transform, mMidCards, matTwigs);
        Obj("Contact shadows", midRoot.transform, mCt, matShadow);
        Obj("Cast shadows", root, mSh, matShadow);
        Obj("Bough detail", closeRoot.transform, mClose, matConifer);
        Obj("Branch detail", closeRoot.transform, mCloseWood, matBark);
        Obj("Twig and spray cards", closeRoot.transform, mCloseCards, matTwigs);
        Obj("Ground detail", closeRoot.transform, mCl, matConifer);
        Obj("Stones", closeRoot.transform, mSt, matRock);
        // One pack per band keeps large generated assets within normal Git file budgets.
        SaveForestMeshes("ForestTierBand" + chunkId.y / 2, meshes);
        midRoot.SetActive(false); closeRoot.SetActive(false);

        farRoots.Add(farRoot); midRoots.Add(midRoot); closeRoots.Add(closeRoot);
        Info("Tier chunk " + chunkId + ": " + placements.Count + " trees (" + conifers + " conifer, " + broadleaf + " broadleaf), " +
             shrubs + " shrubs, " + stonesN + " trail stones");
        int baseFar = Tris(far) + Tris(farWood) + Tris(farCards) + Tris(shadows);
        int baseMid = Tris(mid) + Tris(midWood) + Tris(midCards) + Tris(shadows) + Tris(contact);
        return new[] { baseFar, baseMid, baseMid + Tris(close) + Tris(closeWood) + Tris(closeCards) + Tris(clutter) + Tris(stones) };
    }
}
