using UnityEngine;

// Deterministic value / cellular noise for the Cedar Valley landscape builder.
// The periodic variants wrap so generated textures tile without seams.
internal static class CedarNoise
{
    private static int Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed * 374761393 + x * 668265263 + y * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    public static float Rand(int x, int y, int seed) => (Hash(x, y, seed) & 0xffffff) / 16777215f;

    private static int Wrap(int v, int p) => ((v % p) + p) % p;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    public static float Value(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = Smooth(x - xi), fy = Smooth(y - yi);
        float a = Rand(xi, yi, seed), b = Rand(xi + 1, yi, seed);
        float c = Rand(xi, yi + 1, seed), d = Rand(xi + 1, yi + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    public static float Fbm(float x, float y, int seed, int octaves = 4)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Value(x, y, seed + i * 101) * amp;
            norm += amp;
            amp *= 0.5f;
            x = x * 2.03f + 17.1f;
            y = y * 2.03f + 9.7f;
        }
        return sum / norm;
    }

    public static float Ridged(float x, float z, int seed)
    {
        float Ridge(float value)
        {
            float crest = 1f - Mathf.Abs(2f * value - 1f);
            return crest * crest;
        }
        return .68f * Ridge(Value(x, z, seed)) +
               .24f * Ridge(Value(x * 2.1f + 13f, z * 2.1f, seed + 1)) +
               .08f * Ridge(Value(x * 4.3f, z * 4.3f + 9f, seed + 2));
    }

    public static float ValueP(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = Smooth(x - xi), fy = Smooth(y - yi);
        int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
        int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
        float a = Rand(x0, y0, seed), b = Rand(x1, y0, seed);
        float c = Rand(x0, y1, seed), d = Rand(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>Tileable fBm over u,v in [0,1); base frequency = period cells.</summary>
    public static float FbmP(float u, float v, int period, int seed, int octaves = 4)
    {
        float sum = 0f, amp = 1f, norm = 0f;
        int p = period;
        for (int i = 0; i < octaves; i++)
        {
            sum += ValueP(u * p, v * p, p, seed + i * 101) * amp;
            norm += amp;
            amp *= 0.5f;
            p *= 2;
        }
        return sum / norm;
    }

    /// <summary>Tileable cellular noise. Returns (F1, F2) in cell units for u,v in [0,1).</summary>
    public static Vector2 WorleyP(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float f1 = 9f, f2 = 9f;
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            int cx = xi + ox, cy = yi + oy;
            int wx = Wrap(cx, period), wy = Wrap(cy, period);
            float px = cx + Rand(wx, wy, seed);
            float py = cy + Rand(wx, wy, seed + 7);
            float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) f2 = d;
        }
        return new Vector2(f1, f2);
    }

    public static float Gauss(float t) => Mathf.Exp(-t * t);
}
