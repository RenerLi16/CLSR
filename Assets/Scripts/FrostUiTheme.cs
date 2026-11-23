using UnityEngine;

internal static class FrostUiTheme
{
    internal static readonly Color32 Coal = new(20, 25, 29, 255);
    internal static readonly Color32 Steel = new(83, 99, 109, 255);
    internal static readonly Color32 Enamel = new(234, 240, 239, 255);
    internal static readonly Color32 GlassRim = new(231, 244, 249, 249);
    internal static readonly Color32 GlassEtch = new(196, 223, 234, 224);
    internal static readonly Color32 Frost = new(166, 186, 195, 255);
    internal static readonly Color32 Amber = new(231, 174, 97, 255);
    internal static readonly Color32 Oxide = new(211, 117, 108, 255);
    internal static readonly Color32 Rescued = new(142, 190, 164, 255);
    internal static readonly Color32 RouteOrange = new(242, 166, 91, 255);
    internal static readonly Color32 RouteGreen = new(114, 216, 165, 255);

    // Native masks for the rescue game; no game art, fonts, or icons from Frostpunk.
    internal static bool ArrowInterior(int x, int y) =>
        x >= 0 && x < 160 && y >= 0 && y < 40 &&
        ((x < 129 && y >= 6 && y < 34) || (x >= 129 && Mathf.Abs(y - 19.5f) <= (160 - x) * .64f));

    internal static byte ArrowEtch(int x, int y)
    {
        if (!ArrowInterior(x, y)) return 0;
        if (!ArrowInterior(x - 2, y) || !ArrowInterior(x + 2, y) ||
            !ArrowInterior(x, y - 1) || !ArrowInterior(x, y + 1))
            return (x * 19 + y * 11) % 43 == 0 ? (byte)186 : (byte)255;

        bool endMark = (x >= 6 && x <= 22) || (x >= 106 && x <= 123);
        return endMark && (y == 10 || y == 29) && (x + y) % 5 != 0 ? (byte)155 : (byte)0;
    }

    internal static byte LogisticsMark(int x, int y)
    {
        // A stamped expedition token: an octagonal rim and a pole-mounted route pennant.
        int dx = Mathf.Abs(x * 2 - 31), dy = Mathf.Abs(y * 2 - 31);
        if (dx > 27 || dy > 27 || dx + dy > 40) return 0;
        if (dx > 23 || dy > 23 || dx + dy > 36) return 255;
        if (x is 11 or 12 && y >= 7 && y <= 24) return 255;
        if (y >= 16 && y <= 23 && x >= 13 && x <= 24 - (23 - y)) return 255;
        if (y is 7 or 8 && x >= 8 && x <= 16) return 255;
        return 0;
    }

    internal static byte EdgeShade(int x, int y)
    {
        float u = x / 127f, v = y / 127f; // Texture starts at the bottom-left.
        float edge = Mathf.Max(
            Mathf.Max(1f - u / .18f, 1f - (1f - u) / .12f),
            Mathf.Max(1f - v / .10f, 1f - (1f - v) / .24f));
        return (byte)Mathf.RoundToInt(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge)) * 90f);
    }
}
