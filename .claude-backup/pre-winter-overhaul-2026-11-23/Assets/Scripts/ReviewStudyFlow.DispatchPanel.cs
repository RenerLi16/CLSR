using UnityEngine;

public sealed partial class ReviewStudyFlow
{
    private enum DispatchPlate { Shell, Tab, Plaque }
    private Texture2D dispatchShell, dispatchTab, dispatchPlaque;
    private GUIStyle frostDispatchTitle, frostDispatchCaption, frostDispatchCount;

    private void EnsureDispatchPanelStyles(float scale)
    {
        frostDispatchTitle = new GUIStyle(frostPopupHeading) {
            fontSize = Mathf.Max(16, Mathf.RoundToInt(28 * scale)) };
        frostDispatchCaption = new GUIStyle(frostPopupHeading) {
            fontSize = Mathf.Max(10, Mathf.RoundToInt(13 * scale)) };
        frostDispatchCount = new GUIStyle(frostPopupHeading) {
            fontSize = Mathf.Max(16, Mathf.RoundToInt(30 * scale)) };
    }

    private void DrawDispatchSection(Rect panel, float y)
    {
        if (Event.current.type != EventType.Repaint) return;
        float s = LiteScale;
        const string title = "LOGISTICS";
        float x = panel.xMax - 28 * s - frostDispatchCaption.CalcSize(new GUIContent(title)).x;
        Color rail = FrostUiTheme.GlassEtch;
        FillLite(new Rect(panel.x + 9 * s, y, x - panel.x - 16 * s, Mathf.Max(1, s)), rail);
        FillLite(new Rect(panel.xMax - 20 * s, y, 11 * s, Mathf.Max(1, s)), rail);
        DrawFrostTitle(Rect.MinMaxRect(x, y - 13 * s, panel.xMax - 26 * s, y + 13 * s),
            title, frostDispatchCaption, outline: true);
    }

    private bool DispatchInstrumentButton(Rect rect, string label, bool allowed = true, bool primary = false)
    {
        bool previous = GUI.enabled;
        bool active = previous && allowed;
        GUI.enabled = active;
        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        GUI.enabled = previous;
        if (Event.current.type != EventType.Repaint) return clicked;

        Color edge = !active ? new Color32(119, 143, 157, 105) :
            primary ? FrostUiTheme.GlassRim : FrostUiTheme.GlassEtch;
        Color interior = primary ? new Color32(53, 53, 53, 100) : new Color32(13, 13, 13, 65);
        // Recessed instrument buttons: single line, shoulders smaller than the plaque.
        float cut = 4 * LiteScale;
        FillLite(new Rect(rect.x + cut, rect.y, rect.width - 2 * cut, 1), edge);
        FillLite(new Rect(rect.x + cut, rect.yMax - 1, rect.width - 2 * cut, 1), edge);
        FillLite(new Rect(rect.x, rect.y + cut, 1, rect.height - 2 * cut), edge);
        FillLite(new Rect(rect.xMax - 1, rect.y + cut, 1, rect.height - 2 * cut), edge);
        DrawLiteLine(new Vector2(rect.x, rect.y + cut), new Vector2(rect.x + cut, rect.y), edge, 1);
        DrawLiteLine(new Vector2(rect.xMax - cut, rect.y), new Vector2(rect.xMax, rect.y + cut), edge, 1);
        DrawLiteLine(new Vector2(rect.x, rect.yMax - cut), new Vector2(rect.x + cut, rect.yMax), edge, 1);
        DrawLiteLine(new Vector2(rect.xMax - cut, rect.yMax), new Vector2(rect.xMax, rect.yMax - cut), edge, 1);
        FillLite(new Rect(rect.x + 2, rect.y + cut, rect.width - 4, rect.height - 2 * cut), interior);
        LabelLite(rect, label, frostPanelCentered, active ? FrostUiTheme.Enamel : FrostUiTheme.Frost);
        return clicked;
    }

    private void DrawDispatchPlate(Rect box, ref Texture2D texture, DispatchPlate plate)
    {
        if (Event.current.type != EventType.Repaint) return;
        int width = Mathf.Max(1, Mathf.RoundToInt(box.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(box.height));
        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null) Destroy(texture);
            texture = CreateDispatchPlate(width, height, plate);
        }
        TintLiteTexture(box, texture, Color.white);
    }

    private static Texture2D CreateDispatchPlate(int width, int height, DispatchPlate plate)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[width * height];
        float unit = width / (plate == DispatchPlate.Tab ? 170f : plate == DispatchPlate.Plaque ? 298f : 312f);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float side = (Mathf.Min(x, width - x - 1) + .5f) / unit;
            float top = (height - y - .5f) / unit;
            float bottom = (y + .5f) / unit;
            float edge;
            Color32 paint;
            if (plate == DispatchPlate.Shell)
            {
                // Rounded crown, straight rails and two scalloped steps at the base.
                float topCorner = side < 19 && top < 19
                    ? 19 - Mathf.Sqrt((19 - side) * (19 - side) + (19 - top) * (19 - top))
                    : Mathf.Min(side, top);
                float foot = 9 * EaseInward(bottom, 16, 28) + 11 * EaseInward(bottom, 1, 14);
                edge = Mathf.Min(Mathf.Min(topCorner, bottom), side - foot);
                float h = top * unit / height;
                float u = x / (float)width;
                float gleam = Mathf.Clamp01(.68f * (1 - u) + .32f * (1 - h));
                // Smoked gray glass: neutral tint, with the map still showing through.
                paint = Color32.Lerp(new Color32(13, 13, 13, 182), new Color32(48, 48, 48, 156), gleam);
                paint.a = (byte)Mathf.RoundToInt(paint.a * (1 - .18f * EaseInward(h, .19f, .36f)));
                if (edge < 4.0f) paint = new Color32(18, 18, 18, 151);
                paint = EnamelRail(paint, edge, 5.2f, 1.45f, FrostUiTheme.GlassRim, unit);
            }
            else if (plate == DispatchPlate.Plaque)
            {
                float shoulder = 7 * EaseInward(side, 19, 26) + 8 * EaseInward(side, 5, 15);
                edge = Mathf.Min(side, Mathf.Min(top, bottom) - shoulder);
                paint = Color32.Lerp(new Color32(12, 12, 12, 111), new Color32(43, 43, 43, 88),
                    Mathf.Clamp01((y + .5f) / height));
                if (edge < 2.8f) paint = new Color32(19, 19, 19, 105);
                paint = EnamelRail(paint, edge, 3.3f, 1.25f, FrostUiTheme.GlassRim, unit);
                paint = EnamelRail(paint, edge, 6.0f, 1.05f, FrostUiTheme.GlassEtch, unit);
            }
            else
            {
                edge = Mathf.Min(Mathf.Min(top, bottom), side - Mathf.Max(2, 12 - top * .7f));
                paint = Color32.Lerp(new Color32(53, 53, 53, 178), new Color32(22, 22, 22, 164),
                    (y + .5f) / height);
                if (bottom < 2.2f && edge > .75f) paint = new Color32(181, 181, 181, 231);
                paint = EnamelRail(paint, edge, 2.25f, .9f, FrostUiTheme.GlassEtch, unit);
            }
            paint.a = (byte)Mathf.RoundToInt(paint.a * Mathf.Clamp01(edge * unit + .5f));
            pixels[y * width + x] = paint;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static float EaseInward(float distance, float full, float end) =>
        Mathf.SmoothStep(1, 0, Mathf.InverseLerp(full, end, distance));

    private static Color32 EnamelRail(Color32 baseColor, float distance, float center, float width, Color32 rail, float unit)
    {
        float strength = Mathf.Clamp01((width * .5f - Mathf.Abs(distance - center)) * unit + .5f);
        return Color32.Lerp(baseColor, rail, strength);
    }
}
