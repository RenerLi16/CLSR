using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Textures for the winter art pass (generated once, then reused): frozen soil and dead grass,
// snow and rock relief (slope maps), an earth-and-snow trail bed, bark, the crown-card atlas
// (frosted twigs, conifer sprays, shrubs, grass) and the cast-shadow falloff atlas.
internal static class CedarWinterTextures
{
    public const string Folder = CedarTextures.Folder;
    public static Texture2D Soil, SnowNrm, RockNrm, Rock, TrailEarth, Bark, Foliage, ShadowAtlas;

    // Foliage atlas: 4 x 4 cells of 256 px. 0-3 lacy frosted twig sprays, 4-5 conifer sprays,
    // 6 shrub, 7 grass, 8-10 whole frosted crowns (broad, narrow, rounded) seen from the side,
    // 11 a frosted crown seen from above. Row 3 is spare.
    public const int AtlasCols = 4, AtlasRows = 4;
    public static Rect Cell(int i) => new Rect((i % AtlasCols) / (float)AtlasCols, (i / AtlasCols) / (float)AtlasRows,
                                              1f / AtlasCols, 1f / AtlasRows);

    public static void EnsureAll(bool force = false)
    {
        // Temp/CedarValleyBuild/regen_winter.txt forces regeneration on the next build.
        string flag = Path.Combine(CedarValleyBuilder.OutDir, "regen_winter.txt");
        if (File.Exists(flag)) { force = true; File.Delete(flag); }
        Directory.CreateDirectory(Folder);
        Soil = Get("CV_WinterSoil", force, () => Make(512, SoilPixel), true, false);
        SnowNrm = Get("CV_SnowRelief", force, () => SlopeMap(512, SnowHeight, 7f), false, false);
        RockNrm = Get("CV_RockRelief", force, () => SlopeMap(512, RockHeight, 5f), false, false);
        Rock = Get("CV_WinterRock", force, () => Make(512, RockPixel), true, false);
        TrailEarth = Get("CV_TrailEarth", force, () => Make(512, TrailPixel), true, false);
        Bark = Get("CV_WinterBark", force, () => Make(256, BarkPixel), true, false);
        Foliage = Get("CV_FoliageAtlas", force, FoliageAtlas, true, false, cutout: true);
        ShadowAtlas = Get("CV_ShadowAtlas", force, ShadowPixels, true, true, shadow: true);
    }

    private static Texture2D Get(string name, bool force, Func<Color[,]> make, bool srgb, bool clamp, bool cutout = false, bool shadow = false)
    {
        string path = Folder + "/" + name + ".png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null && !force) return existing;
        Color[,] px = make();
        int w = px.GetLength(0), h = px.GetLength(1);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
        var flat = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) flat[y * w + x] = px[x, y];
        tex.SetPixels(flat);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = srgb;
        imp.mipmapEnabled = true;
        imp.wrapMode = clamp || cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = cutout || shadow ? 1 : 4;
        imp.maxTextureSize = Mathf.Max(w, h);
        imp.alphaIsTransparency = cutout || shadow;
        if (cutout)
        {
            imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = 0.42f;
        }
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color[,] Make(int size, Func<float, float, Color> pixel)
    {
        var px = new Color[size, size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[x, y] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
        return px;
    }

    private static Color Mix(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);

    // ------------------------------------------------------------ soil (tile 5 m)
    private static Color SoilPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 6, 1501, 5);
        float fine = CedarNoise.FbmP(u, v, 40, 1502, 2);
        Color earth = Mix(new Color(0.33f, 0.28f, 0.23f), new Color(0.47f, 0.41f, 0.33f), n * 0.8f + fine * 0.2f);
        // dead grass in clumps; strands run in loose directions
        float clump = Mathf.Clamp01((CedarNoise.FbmP(u, v, 9, 1503, 3) - 0.42f) * 3.2f);
        float strands = CedarNoise.FbmP(u * 1f, v, 90, 1504, 2);
        float strands2 = CedarNoise.FbmP(u + v * 0.3f, v - u * 0.2f, 70, 1505, 2);
        float grass = clump * Mathf.Clamp01((Mathf.Max(strands, strands2) - 0.45f) * 4f);
        Color straw = Mix(new Color(0.55f, 0.48f, 0.34f), new Color(0.71f, 0.63f, 0.47f), fine);
        Color c = Mix(earth, straw, grass * 0.85f);
        // pebbles
        Vector2 w = CedarNoise.WorleyP(u, v, 26, 1506);
        float pebble = Mathf.Clamp01((0.28f - w.x) * 6f) * (CedarNoise.Rand(Mathf.FloorToInt(u * 26), Mathf.FloorToInt(v * 26), 1507) > 0.62f ? 1f : 0f);
        c = Mix(c, new Color(0.52f, 0.51f, 0.49f), pebble * 0.7f);
        // snow lingering in hollows between tufts
        float hollow = Mathf.Clamp01((0.38f - n) * 3f) * (1f - grass);
        c = Mix(c, new Color(0.86f, 0.88f, 0.9f), hollow * 0.55f);
        c.a = Mathf.Clamp01(0.25f + grass * 0.65f + pebble * 0.4f - hollow * 0.3f); // emergence height
        return c;
    }

    // ------------------------------------------------------------ relief maps
    private static float SnowHeight(float u, float v)
    {
        float drift = CedarNoise.FbmP(u, v, 3, 1511, 4);
        float grain = CedarNoise.FbmP(u, v, 64, 1512, 2);
        float warp = CedarNoise.FbmP(u, v, 5, 1513, 2) * 4f;
        float ripple = Mathf.Sin((u * 0.86f + v * 0.5f) * Mathf.PI * 2f * 14f + warp) * 0.5f + 0.5f;
        return drift * 1.0f + ripple * 0.06f * drift + grain * 0.1f;
    }

    private static float RockHeight(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 5, 1521, 5);
        Vector2 w = CedarNoise.WorleyP(u, v, 7, 1522);
        Vector2 w2 = CedarNoise.WorleyP(u, v, 21, 1523);
        float crack = Mathf.Clamp01(1f - (w.y - w.x) * 7f);
        float crack2 = Mathf.Clamp01(1f - (w2.y - w2.x) * 10f);
        return n * 0.9f + w.x * 0.15f - crack * 0.12f - crack2 * 0.05f;
    }

    private static Color[,] SlopeMap(int size, Func<float, float, float> height, float gain)
    {
        var h = new float[size, size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) h[x, y] = height((x + 0.5f) / size, (y + 0.5f) / size);
        var px = new Color[size, size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = h[(x + 1) % size, y] - h[(x + size - 1) % size, y];
            float dz = h[x, (y + 1) % size] - h[x, (y + size - 1) % size];
            float sx = Mathf.Clamp(-dx * gain * size / 512f * 0.5f, -0.5f, 0.5f);
            float sz = Mathf.Clamp(-dz * gain * size / 512f * 0.5f, -0.5f, 0.5f);
            px[x, y] = new Color(0.5f + sx, 0.5f + sz, 0.5f, 1f);
        }
        return px;
    }

    // ------------------------------------------------------------ rock: grey with muted brown
    private static Color RockPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 5, 1521, 5);
        Vector2 w = CedarNoise.WorleyP(u, v, 7, 1522);
        Vector2 w2 = CedarNoise.WorleyP(u, v, 21, 1523);
        float crack = Mathf.Clamp01(1f - (w.y - w.x) * 7f);
        float crack2 = Mathf.Clamp01(1f - (w2.y - w2.x) * 10f);
        float strata = Mathf.Sin((v * 7f + n * 1.4f) * Mathf.PI * 2f) * 0.5f + 0.5f;
        float warm = CedarNoise.FbmP(u, v, 3, 1524, 3);
        Color grey = Mix(new Color(0.40f, 0.40f, 0.41f), new Color(0.63f, 0.62f, 0.61f), 0.15f + 0.7f * n + 0.15f * strata);
        Color brown = Mix(new Color(0.42f, 0.37f, 0.31f), new Color(0.58f, 0.52f, 0.45f), n);
        Color c = Mix(grey, brown, Mathf.Clamp01((warm - 0.4f) * 2.2f) * 0.3f);
        c *= 0.96f + 0.08f * w.x;
        c *= 1f - 0.12f * crack - 0.05f * crack2;
        c *= 0.9f + 0.2f * strata;
        float lichen = Mathf.Clamp01((CedarNoise.FbmP(u, v, 16, 1525, 3) - 0.68f) * 5f);
        c = Mix(c, new Color(0.55f, 0.56f, 0.45f), lichen * 0.3f);
        c.a = 1f;
        return c;
    }

    // ------------------------------------------------------------ trail bed: u across 2.4 m, v along 2.4 m
    private static Color TrailPixel(float u, float v)
    {
        float across = Mathf.Abs(u - 0.5f) * 2f;
        float n = CedarNoise.FbmP(u, v, 6, 1531, 4);
        float fine = CedarNoise.FbmP(u, v, 40, 1532, 2);
        float wob = (CedarNoise.FbmP(0.3f, v, 3, 1533, 2) - 0.5f) * 0.07f;
        Color packed = Mix(new Color(0.74f, 0.73f, 0.71f), new Color(0.86f, 0.86f, 0.85f), 0.3f + 0.5f * n + 0.2f * fine);
        Color earth = Mix(new Color(0.36f, 0.30f, 0.24f), new Color(0.52f, 0.44f, 0.34f), n * 0.7f + fine * 0.3f);
        Color loose = Mix(new Color(0.86f, 0.88f, 0.9f), new Color(0.95f, 0.955f, 0.965f), fine);
        Color c = packed;
        // two worn lanes where boots, sleds and runners expose the frozen earth
        float lanes = 0f;
        foreach (float r in new[] { 0.36f, 0.64f })
        {
            float d = Mathf.Abs(u - r - wob);
            lanes = Mathf.Max(lanes, Mathf.Exp(-(d * d) / 0.0042f));
        }
        float mud = Mathf.Clamp01(lanes * (0.55f + 0.9f * (CedarNoise.FbmP(u, v, 4, 1534, 3) - 0.35f)) + (fine - 0.5f) * 0.35f);
        c = Mix(c, earth, Mathf.SmoothStep(0f, 1f, mud));
        // gravel and grit in the lanes
        int ix = Mathf.FloorToInt(u * 512), iy = Mathf.FloorToInt(v * 512);
        if (CedarNoise.Rand(ix, iy, 1535) > 0.985f) c *= mud > 0.3f ? 1.25f : 0.8f;
        // loose, wind-blown snow on the shoulders
        float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.98f, across + (fine - 0.5f) * 0.25f));
        c = Mix(c, loose, edge);
        c.a = 1f;
        return c;
    }

    private static Color BarkPixel(float u, float v)
    {
        // u runs along the stem (cylinder UV x), v around it
        float n = CedarNoise.FbmP(u * 0.25f, v, 10, 1541, 4);
        float fissure = Mathf.Clamp01((CedarNoise.FbmP(u * 0.15f, v, 18, 1542, 2) - 0.58f) * 5f);
        Color c = Mix(new Color(0.26f, 0.24f, 0.22f), new Color(0.47f, 0.44f, 0.41f), n);
        c *= 1f - 0.4f * fissure;
        float lichen = Mathf.Clamp01((CedarNoise.FbmP(u, v, 12, 1543, 3) - 0.66f) * 4f);
        c = Mix(c, new Color(0.58f, 0.6f, 0.53f), lichen * 0.35f);
        c.a = 1f;
        return c;
    }

    // ------------------------------------------------------------ crown card atlas
    private sealed class Canvas2D
    {
        public readonly int W, H;
        public readonly float[,] A, F, Tn; // coverage, frost amount, tone
        public readonly Color[,] Base;
        public Canvas2D(int w, int h) { W = w; H = h; A = new float[w, h]; F = new float[w, h]; Tn = new float[w, h]; Base = new Color[w, h]; }

        // Antialiased capsule stamp in pixel coordinates, clipped to the cell rect.
        public float Tone = 1f;
        public void Capsule(Vector2 a, Vector2 b, float ra, float rb, Color col, float frost, RectInt clip)
        {
            int x0 = Mathf.Max(clip.xMin, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - Mathf.Max(ra, rb) - 2));
            int x1 = Mathf.Min(clip.xMax - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + Mathf.Max(ra, rb) + 2));
            int y0 = Mathf.Max(clip.yMin, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - Mathf.Max(ra, rb) - 2));
            int y1 = Mathf.Min(clip.yMax - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + Mathf.Max(ra, rb) + 2));
            Vector2 ab = b - a;
            float l2 = Mathf.Max(1e-5f, ab.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                float r = Mathf.Lerp(ra, rb, t);
                float d = Vector2.Distance(p, a + ab * t);
                float cov = Mathf.Clamp01(r + 0.5f - d);
                if (cov <= 0f) continue;
                if (cov >= A[x, y])
                {
                    Base[x, y] = col;
                    // frost sits on the upper side of thicker twigs, all over the fine ones
                    Vector2 side = new Vector2(-ab.y, ab.x).normalized;
                    float upper = Mathf.Clamp01(0.5f + Vector2.Dot(p - (a + ab * t), side.y >= 0 ? side : -side) / Mathf.Max(0.5f, r));
                    F[x, y] = Mathf.Clamp01(frost * Mathf.Lerp(1f, upper, Mathf.Clamp01((r - 1f) / 2.5f)));
                    Tn[x, y] = Tone;
                }
                A[x, y] = Mathf.Max(A[x, y], cov);
            }
        }

        public void Dot(Vector2 c, float r, Color col, float frost, RectInt clip) => Capsule(c, c, r, r, col, frost, clip);
    }

    private static Color[,] FoliageAtlas()
    {
        const int cell = 256;
        var cv = new Canvas2D(cell * AtlasCols, cell * AtlasRows);
        for (int i = 0; i < AtlasCols * AtlasRows; i++)
        {
            var clip = new RectInt((i % AtlasCols) * cell, (i / AtlasCols) * cell, cell, cell);
            cv.Tone = 1f;
            if (i < 4) TwigCluster(cv, clip, i);
            else if (i < 6) ConiferSpray(cv, clip, i);
            else if (i == 6) Shrub(cv, clip);
            else if (i == 7) GrassTuft(cv, clip);
            else if (i < 11) Crown(cv, clip, i - 8);
            else if (i == 11) CrownFromAbove(cv, clip);
        }
        var px = new Color[cv.W, cv.H];
        Color frostCol = new Color(0.87f, 0.885f, 0.905f), fill = new Color(0.66f, 0.68f, 0.7f, 0f);
        for (int y = 0; y < cv.H; y++)
        for (int x = 0; x < cv.W; x++)
        {
            float a = cv.A[x, y];
            if (a <= 0f) { px[x, y] = fill; continue; }
            float sparkle = 0.94f + 0.12f * CedarNoise.Rand(x, y, 1551);
            Color c = Mix(cv.Base[x, y], frostCol * sparkle, cv.F[x, y]) * (cv.Tn[x, y] > 0f ? cv.Tn[x, y] : 1f);
            c.a = a;
            px[x, y] = c;
        }
        return px;
    }

    // A frosted twig cluster grows from the bottom centre of the cell (the card's attach point).
    private static void TwigCluster(Canvas2D cv, RectInt clip, int variant)
    {
        int seed = 1600 + variant * 37;
        var bark = new Color(0.25f, 0.23f, 0.22f);
        float spread = new[] { 0.95f, 0.55f, 1.2f, 0.75f }[variant];
        float droop = new[] { 0f, -0.1f, 0.35f, 0.05f }[variant];
        int stems = new[] { 3, 2, 4, 3 }[variant];
        int counter = 0;
        void Grow(Vector2 p, float angle, float len, float width, int depth)
        {
            if (depth > 5 || len < 3f) return;
            Vector2 prev = p;
            float a = angle;
            const int segs = 3;
            for (int s = 1; s <= segs; s++)
            {
                a += (CedarNoise.Rand(counter, s, seed) - 0.5f) * 0.35f + droop * 0.08f * Mathf.Sign(Mathf.Cos(a));
                Vector2 next = prev + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len / segs;
                float w0 = Mathf.Lerp(width, width * 0.62f, (s - 1) / (float)segs), w1 = Mathf.Lerp(width, width * 0.62f, s / (float)segs);
                float frost = Mathf.Lerp(0.3f, 0.85f, Mathf.Clamp01((3.2f - w1) / 2.6f));
                cv.Tone = Mathf.Lerp(0.8f, 1.04f, CedarNoise.Rand(counter, 3, seed));
                cv.Capsule(prev, next, w0, w1, bark, frost, clip);
                // hoar frost crystals fuzz the finest twigs
                if (w1 < 0.9f && depth >= 4)
                    for (int k = 0; k < 1; k++)
                    {
                        Vector2 q = Vector2.Lerp(prev, next, CedarNoise.Rand(counter, 40 + k + s * 5, seed))
                                    + new Vector2(CedarNoise.Rand(counter, 60 + k, seed) - 0.5f, CedarNoise.Rand(counter, 80 + k, seed) - 0.5f) * 3.2f;
                        cv.Dot(q, 0.55f + 0.5f * CedarNoise.Rand(counter, 90 + k, seed), bark, 1f, clip);
                    }
                // side twigs off this segment
                int idx = counter++;
                if (depth < 5)
                {
                    float side = (idx & 1) == 0 ? 1f : -1f;
                    float ca = a + side * Mathf.Lerp(0.42f, 0.95f, CedarNoise.Rand(idx, 7, seed)) * spread;
                    Grow(next, ca, len * Mathf.Lerp(0.42f, 0.62f, CedarNoise.Rand(idx, 8, seed)), Mathf.Max(0.45f, w1 * 0.66f), depth + 1);
                }
                prev = next;
            }
            // terminal fork
            Grow(prev, a + 0.38f * spread, len * 0.6f, Mathf.Max(0.45f, width * 0.55f), depth + 1);
            Grow(prev, a - 0.38f * spread, len * 0.55f, Mathf.Max(0.45f, width * 0.5f), depth + 1);
        }
        Vector2 root = new Vector2(clip.x + clip.width * 0.5f, clip.y + 2f);
        for (int s = 0; s < stems; s++)
        {
            float ang = Mathf.PI * 0.5f + (s - (stems - 1) * 0.5f) * 0.42f * spread + (CedarNoise.Rand(s, 1, seed) - 0.5f) * 0.2f;
            Grow(root, ang, clip.height * Mathf.Lerp(0.27f, 0.33f, CedarNoise.Rand(s, 2, seed)), 3.0f, 0);
        }
    }

    // A whole frosted broadleaf crown (no trunk below the fork): dense fine twigs coated in hoar
    // frost, slightly darker and greyer inside where twigs shade each other.
    private static void Crown(Canvas2D cv, RectInt clip, int habit)
    {
        int seed = 1650 + habit * 53;
        var bark = new Color(0.24f, 0.22f, 0.21f);
        float spread = new[] { 1.05f, 0.5f, 0.85f }[habit];
        int leaders = new[] { 4, 2, 3 }[habit];
        Vector2 centre = new Vector2(clip.x + clip.width * 0.5f, clip.y + clip.height * 0.5f);
        float maxR = clip.width * 0.47f;
        int counter = 0;
        void Grow(Vector2 p, float angle, float len, float width, int depth)
        {
            if (depth > 6 || len < 2.2f) return;
            Vector2 prev = p;
            float a = angle;
            for (int s = 1; s <= 3; s++)
            {
                a += (CedarNoise.Rand(counter, s, seed) - 0.5f) * 0.4f;
                Vector2 next = prev + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len / 3f;
                // keep a rounded crown envelope inside the cell
                Vector2 rel = (next - centre) / maxR;
                rel.x *= habit == 1 ? 1.5f : 1.08f;
                rel.y *= 0.92f;
                // an irregular, lobed outline with a few deep gaps, not a smooth dome
                float ang = Mathf.Atan2(rel.y, rel.x);
                float lobe = 0.82f + 0.22f * CedarNoise.FbmP(ang / (Mathf.PI * 2f) + 0.5f, habit * 0.31f, 7, seed + 5, 3);
                if (rel.magnitude > lobe) return;
                if (depth >= 3 && CedarNoise.Rand(counter, 17, seed) < 0.07f) return;
                float w1 = Mathf.Max(0.45f, width * (1f - s * 0.12f));
                float inner = Mathf.Clamp01(rel.magnitude);
                cv.Tone = Mathf.Lerp(0.74f, 1.03f, inner * inner) * Mathf.Lerp(0.9f, 1.05f, CedarNoise.Rand(counter, 7, seed));
                cv.Capsule(prev, next, width * (1f - (s - 1) * 0.12f), w1, bark, Mathf.Lerp(0.35f, 0.93f, Mathf.Clamp01((2.6f - w1) / 2.1f)), clip);
                if (w1 < 0.8f && CedarNoise.Rand(counter, 11, seed) < 0.2f)
                {
                    Vector2 q = next + new Vector2(CedarNoise.Rand(counter, 12, seed) - 0.5f, CedarNoise.Rand(counter, 13, seed) - 0.5f) * 3f;
                    cv.Dot(q, 0.7f, bark, 1f, clip);
                }
                int idx = counter++;
                float side = (idx & 1) == 0 ? 1f : -1f;
                Grow(next, a + side * Mathf.Lerp(0.4f, 0.9f, CedarNoise.Rand(idx, 8, seed)) * spread,
                    len * Mathf.Lerp(0.45f, 0.62f, CedarNoise.Rand(idx, 9, seed)), w1 * 0.68f, depth + 1);
                prev = next;
            }
            Grow(prev, a + 0.35f * spread, len * 0.62f, width * 0.55f, depth + 1);
            Grow(prev, a - 0.35f * spread, len * 0.58f, width * 0.5f, depth + 1);
        }
        Vector2 root = new Vector2(centre.x, clip.y + 3f);
        for (int l = 0; l < leaders; l++)
        {
            float ang = Mathf.PI * 0.5f + (l - (leaders - 1) * 0.5f) * 0.5f * spread;
            Grow(root, ang, clip.height * 0.36f, 4.2f, 0);
        }
    }

    // A frosted crown seen from above: limbs radiate from the centre, fine twigs at the rim.
    private static void CrownFromAbove(Canvas2D cv, RectInt clip)
    {
        int seed = 1690;
        var bark = new Color(0.24f, 0.22f, 0.21f);
        Vector2 c = new Vector2(clip.x + clip.width * 0.5f, clip.y + clip.height * 0.5f);
        float maxR = clip.width * 0.47f;
        int counter = 0;
        void Grow(Vector2 p, float angle, float len, float width, int depth)
        {
            if (depth > 5 || len < 2.2f) return;
            Vector2 prev = p;
            float a = angle;
            for (int s = 1; s <= 3; s++)
            {
                a += (CedarNoise.Rand(counter, s, seed) - 0.5f) * 0.45f;
                Vector2 next = prev + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len / 3f;
                if ((next - c).magnitude > maxR) return;
                float w1 = Mathf.Max(0.45f, width * 0.88f);
                cv.Tone = Mathf.Lerp(0.85f, 1.03f, (next - c).magnitude / maxR);
                cv.Capsule(prev, next, width, w1, bark, Mathf.Lerp(0.3f, 0.92f, Mathf.Clamp01((2.6f - w1) / 2.1f)), clip);
                int idx = counter++;
                float side = (idx & 1) == 0 ? 1f : -1f;
                Grow(next, a + side * Mathf.Lerp(0.45f, 0.95f, CedarNoise.Rand(idx, 8, seed)),
                    len * Mathf.Lerp(0.45f, 0.62f, CedarNoise.Rand(idx, 9, seed)), w1 * 0.66f, depth + 1);
                prev = next;
            }
            Grow(prev, a + 0.4f, len * 0.6f, width * 0.55f, depth + 1);
            Grow(prev, a - 0.4f, len * 0.55f, width * 0.5f, depth + 1);
        }
        for (int l = 0; l < 7; l++)
            Grow(c, l * Mathf.PI * 2f / 7f + CedarNoise.Rand(l, 1, seed) * 0.5f, maxR * 0.62f, 3.6f, 0);
    }

    private static void ConiferSpray(Canvas2D cv, RectInt clip, int variant)
    {
        int seed = 1700 + variant * 13;
        float snowy = variant == 4 ? 0.35f : 0.65f;
        Vector2 root = new Vector2(clip.x + clip.width * 0.5f, clip.y + 4f);
        Vector2 tip = new Vector2(clip.x + clip.width * 0.5f, clip.yMax - 10f);
        var twig = new Color(0.30f, 0.25f, 0.2f);
        cv.Capsule(root, tip, 3f, 1f, twig, 0.1f, clip);
        for (int k = 0; k < 900; k++)
        {
            float t = CedarNoise.Rand(k, 1, seed);
            Vector2 on = Vector2.Lerp(root, tip, t);
            float reach = clip.width * 0.45f * Mathf.Sin(Mathf.Clamp01(t * 1.1f) * Mathf.PI * 0.85f + 0.2f);
            float side = CedarNoise.Rand(k, 2, seed) < 0.5f ? -1f : 1f;
            float along = CedarNoise.Rand(k, 3, seed);
            Vector2 a = on + new Vector2(side * reach * along * 0.85f, -along * 6f);
            Vector2 b = a + new Vector2(side * (5f + 6f * CedarNoise.Rand(k, 4, seed)), 6f + 5f * CedarNoise.Rand(k, 5, seed));
            float g = CedarNoise.Rand(k, 6, seed);
            var needle = Mix(new Color(0.13f, 0.2f, 0.16f), new Color(0.24f, 0.31f, 0.24f), g);
            // snow lies in clumps on the upper face of the spray
            float clump = CedarNoise.Fbm(a.x / 14f, a.y / 14f, seed + 3, 2);
            float frost = Mathf.Clamp01((clump - (1f - snowy)) * 3.5f) * (b.y > a.y ? 1f : 0.4f);
            cv.Capsule(a, b, 1.3f, 0.6f, needle, frost, clip);
        }
    }

    private static void Shrub(Canvas2D cv, RectInt clip)
    {
        int seed = 1800;
        Vector2 root = new Vector2(clip.x + clip.width * 0.5f, clip.y + 2f);
        for (int s = 0; s < 14; s++)
        {
            float ang = Mathf.PI * 0.5f + (CedarNoise.Rand(s, 1, seed) - 0.5f) * 1.7f;
            float len = clip.height * Mathf.Lerp(0.45f, 0.85f, CedarNoise.Rand(s, 2, seed));
            Vector2 p = root + new Vector2((CedarNoise.Rand(s, 3, seed) - 0.5f) * 30f, 0f);
            var stem = Mix(new Color(0.38f, 0.22f, 0.18f), new Color(0.47f, 0.37f, 0.3f), CedarNoise.Rand(s, 4, seed));
            Vector2 prev = p;
            for (int k = 1; k <= 6; k++)
            {
                ang += (CedarNoise.Rand(s, 10 + k, seed) - 0.5f) * 0.3f;
                Vector2 next = prev + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len / 6f;
                cv.Capsule(prev, next, 2.2f - k * 0.25f, 2f - k * 0.25f, stem, 0.25f + k * 0.07f, clip);
                if (k > 1)
                {
                    float sa = ang + ((k & 1) == 0 ? 0.7f : -0.7f);
                    cv.Capsule(next, next + new Vector2(Mathf.Cos(sa), Mathf.Sin(sa)) * len * 0.18f, 1f, 0.5f, stem, 0.55f, clip);
                }
                prev = next;
            }
        }
    }

    private static void GrassTuft(Canvas2D cv, RectInt clip)
    {
        int seed = 1900;
        Vector2 root = new Vector2(clip.x + clip.width * 0.5f, clip.y + 2f);
        for (int s = 0; s < 70; s++)
        {
            float ang = Mathf.PI * 0.5f + (CedarNoise.Rand(s, 1, seed) - 0.5f) * 1.3f;
            float len = clip.height * Mathf.Lerp(0.35f, 0.9f, CedarNoise.Rand(s, 2, seed));
            Vector2 p = root + new Vector2((CedarNoise.Rand(s, 3, seed) - 0.5f) * 50f, 0f);
            var straw = Mix(new Color(0.55f, 0.47f, 0.33f), new Color(0.74f, 0.66f, 0.5f), CedarNoise.Rand(s, 4, seed));
            float bend = (CedarNoise.Rand(s, 5, seed) - 0.5f) * 0.6f;
            Vector2 prev = p;
            for (int k = 1; k <= 4; k++)
            {
                ang += bend * 0.25f;
                Vector2 next = prev + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len / 4f;
                cv.Capsule(prev, next, 1.3f - k * 0.2f, 1.1f - k * 0.2f, straw, k == 1 ? 0.5f : 0.08f, clip);
                prev = next;
            }
        }
    }

    // ------------------------------------------------------------ shadow atlas (256 x 128)
    private static Color[,] ShadowPixels()
    {
        const int h = 128, w = 256;
        var px = new Color[w, h];
        var twigs = new Canvas2D(128, 128);
        var clip = new RectInt(0, 0, 128, 128);
        for (int s = 0; s < 4; s++)
        {
            int seed = 2000 + s;
            Vector2 c = new Vector2(64, 64);
            for (int k = 0; k < 9; k++)
            {
                float a = CedarNoise.Rand(k, 1, seed) * Mathf.PI * 2f;
                Vector2 end = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (30f + 30f * CedarNoise.Rand(k, 2, seed));
                twigs.Capsule(c + (end - c) * 0.2f, end, 3.2f, 1.2f, Color.black, 0f, clip);
                for (int j = 0; j < 7; j++)
                {
                    Vector2 q = Vector2.Lerp(c, end, 0.3f + 0.7f * CedarNoise.Rand(k, 10 + j, seed));
                    float b = a + (CedarNoise.Rand(k, 20 + j, seed) - 0.5f) * 2.2f;
                    twigs.Capsule(q, q + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 14f, 1.6f, 0.8f, Color.black, 0f, clip);
                }
            }
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int lx = x % 128;
            float dx = (lx + 0.5f) / 64f - 1f, dy = (y + 0.5f) / 64f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Clamp01(1f - r);
            a = a * a * (3f - 2f * a);
            if (x >= 128)
            {
                // blur-free dapple: branch coverage plus light gaps of varying size
                float gaps = CedarNoise.FbmP((lx + 0.5f) / 128f, (y + 0.5f) / 128f, 6, 2011, 3);
                float dapple = Mathf.Clamp01(0.32f + twigs.A[lx, y] * 0.55f + (gaps - 0.45f) * 0.9f);
                a *= Mathf.Lerp(0.55f, 1f, dapple) * Mathf.Lerp(1f, dapple, Mathf.Clamp01(r * 1.3f));
            }
            px[x, y] = new Color(1f, 1f, 1f, a);
        }
        return px;
    }
}
