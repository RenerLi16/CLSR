using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates the tileable, world-scale detail textures used by the rebuilt Cedar Valley
// landscape. Each texture is authored for a known tile size in meters so close-up detail
// (boot prints, sled ruts, ice cracks, rock fractures) stays at real-world size.
internal static class CedarTextures
{
    public const string Folder = "Assets/Art/CedarValley/Generated/Textures";

    public static Texture2D FreshSnow, PackedSnow, TrailBed, Rock, Ice, Macro, Blob, Needles, Bark;

    public static void GenerateAll()
    {
        Directory.CreateDirectory(Folder);
        FreshSnow = Make("CV_FreshSnow", 512, FreshSnowPixel, true);      // tile 8 m
        PackedSnow = Make("CV_PackedSnow", 512, PackedSnowPixel(), true); // tile 4 m
        TrailBed = Make("CV_TrailBed", 512, TrailBedPixel(), true);       // 2.4 m wide x 2.4 m long
        Rock = Make("CV_Rock", 512, RockPixel, true);                     // tile 6 m
        Ice = Make("CV_RiverIce", 512, IcePixel, true);                   // tile 12 m
        Macro = Make("CV_Macro", 256, MacroPixel, false);                 // tile 180 m
        Blob = Make("CV_ContactShadow", 128, BlobPixel, false, clamp: true);
        Needles = Make("CV_Needles", 256, NeedlePixel, true);
        Bark = Make("CV_Bark", 256, BarkPixel, true);
    }

    public static void LoadAll()
    {
        FreshSnow = Load("CV_FreshSnow"); PackedSnow = Load("CV_PackedSnow"); TrailBed = Load("CV_TrailBed");
        Rock = Load("CV_Rock"); Ice = Load("CV_RiverIce"); Macro = Load("CV_Macro"); Blob = Load("CV_ContactShadow");
        Needles = Load("CV_Needles"); Bark = Load("CV_Bark");
    }

    private static Texture2D Load(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".png");

    private static Texture2D Make(string name, int size, Func<float, float, Color> pixel, bool srgb, bool clamp = false)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
        tex.SetPixels(px);
        tex.Apply();
        string path = Folder + "/" + name + ".png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = srgb;
        imp.alphaIsTransparency = clamp;
        imp.mipmapEnabled = true;
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = clamp ? 1 : 4;
        imp.maxTextureSize = size;
        imp.textureCompression = TextureImporterCompression.Compressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color Mix(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);

    // ---------- fresh snow: soft drifts, wind ripples, sparkle ----------
    private static Color FreshSnowPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 6, 11, 5);
        float fine = CedarNoise.FbmP(u, v, 48, 12, 2);
        float warp = CedarNoise.FbmP(u, v, 4, 13, 3) * 6f;
        float ripple = Mathf.Sin((u * 0.8f + v * 0.6f) * Mathf.PI * 2f * 22f + warp) * 0.5f + 0.5f;
        Color lo = new Color(0.84f, 0.89f, 0.95f), hi = new Color(0.975f, 0.985f, 1f);
        Color c = Mix(lo, hi, 0.25f + 0.6f * n + 0.15f * fine);
        c *= 0.985f + 0.03f * ripple * n;
        int ix = Mathf.FloorToInt(u * 512), iy = Mathf.FloorToInt(v * 512);
        if (CedarNoise.Rand(ix, iy, 14) > 0.9965f) c = Mix(c, Color.white, 0.8f);
        c.a = 1f;
        return c;
    }

    // ---------- packed / trampled snow with boot prints (tile 4 m) ----------
    private struct Print { public float x, y, ang, len; }

    private static Print[] RandomPrints(int count, int seed, float lenMin, float lenMax)
    {
        var prints = new Print[count];
        for (int i = 0; i < count; i++)
            prints[i] = new Print
            {
                x = CedarNoise.Rand(i, 1, seed), y = CedarNoise.Rand(i, 2, seed),
                ang = CedarNoise.Rand(i, 3, seed) * Mathf.PI * 2f,
                len = Mathf.Lerp(lenMin, lenMax, CedarNoise.Rand(i, 4, seed))
            };
        return prints;
    }

    // Boot print depth at offset (dx,dy) in meters from print centre, rotated into print space.
    private static float BootDepth(float dx, float dy, float ang, float len)
    {
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        float lx = dx * ca + dy * sa, ly = -dx * sa + dy * ca;
        float w = len * 0.36f;
        // sole (front) and heel (back) lobes
        float sole = (lx / w) * (lx / w) + ((ly - len * 0.16f) / (len * 0.34f)) * ((ly - len * 0.16f) / (len * 0.34f));
        float heel = (lx / (w * 0.85f)) * (lx / (w * 0.85f)) + ((ly + len * 0.3f) / (len * 0.18f)) * ((ly + len * 0.3f) / (len * 0.18f));
        float d = Mathf.Min(sole, heel);
        if (d > 1.6f) return 0f;
        return d < 1f ? 1f - d * 0.25f : -(1.6f - d) * 0.4f; // >0 inside print, small raised rim
    }

    private static Func<float, float, Color> PackedSnowPixel()
    {
        const float tile = 4f;
        Print[] prints = RandomPrints(150, 21, 0.26f, 0.31f);
        return (u, v) =>
        {
            float n = CedarNoise.FbmP(u, v, 8, 22, 4);
            float fine = CedarNoise.FbmP(u, v, 64, 23, 2);
            Color c = Mix(new Color(0.66f, 0.71f, 0.78f), new Color(0.82f, 0.86f, 0.91f), 0.3f + 0.55f * n + 0.15f * fine);
            float depth = 0f;
            foreach (Print p in prints)
            {
                float dx = Mathf.Repeat(u - p.x + 0.5f, 1f) - 0.5f, dy = Mathf.Repeat(v - p.y + 0.5f, 1f) - 0.5f;
                dx *= tile; dy *= tile;
                if (dx * dx + dy * dy > 0.09f) continue;
                float b = BootDepth(dx, dy, p.ang, p.len);
                depth = b > 0f ? Mathf.Max(depth, b) : depth + b * 0.2f;
            }
            c *= 1f - 0.09f * Mathf.Clamp01(depth) + 0.04f * Mathf.Clamp01(-depth * 3f);
            // icy, polished patches
            float glaze = Mathf.Clamp01((CedarNoise.FbmP(u, v, 5, 24, 3) - 0.62f) * 6f);
            c = Mix(c, new Color(0.80f, 0.86f, 0.92f), glaze * 0.4f);
            c.a = 1f;
            return c;
        };
    }

    // ---------- trail bed: u = across (0..1 over 2.4 m), v = along (2.4 m per tile) ----------
    private static Func<float, float, Color> TrailBedPixel()
    {
        const float width = 2.4f;
        Print[] prints = new Print[40];
        for (int i = 0; i < prints.Length; i++)
        {
            float side = (i & 1) == 0 ? -1f : 1f;
            float lane = CedarNoise.Rand(i, 9, 31) < 0.5f ? 0.44f : 0.56f;
            prints[i] = new Print
            {
                x = lane + side * 0.045f + (CedarNoise.Rand(i, 1, 31) - 0.5f) * 0.05f,
                y = (i + CedarNoise.Rand(i, 2, 31) * 0.5f) / prints.Length,
                ang = Mathf.PI * 0.5f + (CedarNoise.Rand(i, 3, 31) - 0.5f) * 0.5f,
                len = Mathf.Lerp(0.26f, 0.31f, CedarNoise.Rand(i, 4, 31))
            };
        }
        return (u, v) =>
        {
            float across = Mathf.Abs(u - 0.5f) * 2f; // 0 centre .. 1 edge
            float n = CedarNoise.FbmP(u, v, 8, 32, 4);
            float lumps = CedarNoise.FbmP(u, v, 24, 33, 3);
            Color bed = Mix(new Color(0.53f, 0.58f, 0.65f), new Color(0.68f, 0.73f, 0.79f), 0.35f + 0.5f * n);
            Color loose = Mix(new Color(0.84f, 0.88f, 0.94f), new Color(0.96f, 0.975f, 1f), lumps);
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, across + (lumps - 0.5f) * 0.25f));
            Color c = Mix(bed, loose, edge);
            // sled runner ruts, gently wandering
            float wob = (CedarNoise.FbmP(0.3f, v, 3, 34, 2) - 0.5f) * 0.06f;
            foreach (float r in new[] { 0.37f, 0.63f })
            {
                float d = Mathf.Abs(u - r - wob) * width;
                c *= 1f - 0.11f * Mathf.Exp(-(d * d) / 0.0012f) + 0.035f * Mathf.Exp(-((d - 0.06f) * (d - 0.06f)) / 0.0006f);
            }
            float depth = 0f;
            foreach (Print p in prints)
            {
                float dx = (u - p.x) * width, dy = (Mathf.Repeat(v - p.y + 0.5f, 1f) - 0.5f) * width;
                if (dx * dx + dy * dy > 0.09f) continue;
                depth = Mathf.Max(depth, BootDepth(dx, dy, p.ang, p.len));
            }
            c *= 1f - 0.1f * depth * (1f - edge);
            // scattered grit / needles in the packed centre
            int ix = Mathf.FloorToInt(u * 512), iy = Mathf.FloorToInt(v * 512);
            if (CedarNoise.Rand(ix, iy, 35) > 0.993f && edge < 0.6f) c *= 0.72f;
            c.a = 1f;
            return c;
        };
    }

    // ---------- slate rock with fractures and lichen (tile 6 m) ----------
    private static Color RockPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 5, 41, 5);
        Vector2 w = CedarNoise.WorleyP(u, v, 7, 42);
        Vector2 w2 = CedarNoise.WorleyP(u, v, 23, 43);
        float crack = Mathf.Clamp01(1f - (w.y - w.x) * 9f);
        float crack2 = Mathf.Clamp01(1f - (w2.y - w2.x) * 12f);
        float strata = Mathf.Sin((v * 9f + n * 1.6f) * Mathf.PI * 2f) * 0.5f + 0.5f;
        Color c = Mix(new Color(0.38f, 0.42f, 0.47f), new Color(0.62f, 0.65f, 0.69f), 0.2f + 0.65f * n + 0.15f * strata);
        c *= 0.96f + 0.08f * w.x;
        c *= 1f - 0.45f * crack - 0.2f * crack2;
        float lichen = Mathf.Clamp01((CedarNoise.FbmP(u, v, 16, 44, 3) - 0.66f) * 5f);
        c = Mix(c, new Color(0.56f, 0.55f, 0.43f), lichen * 0.35f);
        float snowFleck = Mathf.Clamp01((CedarNoise.FbmP(u, v, 32, 45, 2) - 0.72f) * 6f) * (0.4f + crack * 0.6f);
        c = Mix(c, new Color(0.9f, 0.93f, 0.97f), snowFleck * 0.6f);
        c.a = 1f;
        return c;
    }

    // ---------- river ice: pale wind-polished ice, dark clear patches, fine low-contrast fractures (tile 12 m)
    private static Color IcePixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 3, 51, 5);
        float clear = Mathf.Clamp01((CedarNoise.FbmP(u, v, 4, 56, 4) - 0.5f) * 3.2f);
        Vector2 w = CedarNoise.WorleyP(u, v, 6, 52);
        Vector2 w2 = CedarNoise.WorleyP(u, v, 19, 53);
        float warp = CedarNoise.FbmP(u, v, 12, 57, 2);
        float crack = Mathf.Clamp01(1f - (w.y - w.x) * 26f) * Mathf.Clamp01((warp - 0.35f) * 3f);
        float crack2 = Mathf.Clamp01(1f - (w2.y - w2.x) * 30f) * 0.5f * Mathf.Clamp01((warp - 0.5f) * 3f);
        Color pale = Mix(new Color(0.56f, 0.67f, 0.74f), new Color(0.72f, 0.80f, 0.86f), n);
        Color dark = Mix(new Color(0.26f, 0.38f, 0.45f), new Color(0.36f, 0.48f, 0.55f), n);
        Color c = Mix(pale, dark, clear * 0.85f);
        float frost = Mathf.Clamp01((CedarNoise.FbmP(u, v, 9, 54, 3) - 0.58f) * 3f);
        c = Mix(c, new Color(0.80f, 0.86f, 0.91f), frost * 0.45f);
        c = Mix(c, new Color(0.84f, 0.90f, 0.94f), Mathf.Max(crack, crack2) * 0.55f);
        int ix = Mathf.FloorToInt(u * 512), iy = Mathf.FloorToInt(v * 512);
        if (CedarNoise.Rand(ix, iy, 55) > 0.995f) c = Mix(c, Color.white, 0.35f * clear); // trapped bubbles in clear ice
        c.a = 1f;
        return c;
    }

    private static Color MacroPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 3, 61, 5);
        return new Color(n, n, n, 1f);
    }

    private static Color BlobPixel(float u, float v)
    {
        float dx = u * 2f - 1f, dy = v * 2f - 1f;
        float r = Mathf.Sqrt(dx * dx + dy * dy);
        float a = Mathf.Clamp01(1f - r);
        a = a * a * (3f - 2f * a);
        return new Color(1f, 1f, 1f, a);
    }

    private static Color NeedlePixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u, v, 16, 71, 3);
        float streak = CedarNoise.FbmP(u * 4f, v, 32, 72, 2);
        float k = 0.72f + 0.38f * n + 0.2f * (streak - 0.5f);
        return new Color(k, k, k, 1f);
    }

    private static Color BarkPixel(float u, float v)
    {
        float n = CedarNoise.FbmP(u * 6f, v, 8, 81, 4);
        Color c = Mix(new Color(0.22f, 0.17f, 0.13f), new Color(0.42f, 0.34f, 0.27f), n);
        c.a = 1f;
        return c;
    }
}
