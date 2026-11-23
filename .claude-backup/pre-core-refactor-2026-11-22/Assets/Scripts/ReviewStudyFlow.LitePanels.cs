using UnityEngine;

public sealed partial class ReviewStudyFlow
{
    private const int FrostFrameSize = 96;
    private const int FrostFrameCorner = 24;
    private static readonly Color32 TooltipGlass = new(18, 29, 38, 237);
    private static readonly Color32 DispatchGlass = new(14, 37, 53, 240);
    private static readonly Color32 DispatchFrost = new(52, 78, 91, 230);
    private GUIStyle frostPopupHeading, frostPanelCentered, frostPanelText, frostPanelValue;
    private Texture2D tooltipFrame, dispatchFrame, tooltipPointer;
    private Vector2? frostPopupPointer, frostLastInputPointer;

    private void Update()
    {
        Vector2 point = Input.mousePosition;
        point.y = Screen.height - point.y;
        if (frostLastInputPointer != point)
        {
            frostLastInputPointer = point;
            frostPopupPointer = point;
        }
        UpdateTacticalNotifications();
    }

    private void TrackFrostPopupPointer(Event input)
    {
        // IMGUI mouse events share the node-click coordinate space in scaled Game views.
        if (input.type is EventType.MouseEnterWindow or EventType.MouseMove or EventType.MouseDown
            or EventType.MouseUp or EventType.MouseDrag or EventType.ScrollWheel)
            frostPopupPointer = input.mousePosition;
        else if (input.type == EventType.MouseLeaveWindow)
            frostPopupPointer = null;
    }

    private void EnsureFrostPanelStyles()
    {
        float s = LiteScale;
        frostPanelText = new GUIStyle(liteText) {
            fontSize = Mathf.Max(13, Mathf.RoundToInt(17 * s)),
            alignment = TextAnchor.MiddleLeft, wordWrap = false };
        frostPanelValue = new GUIStyle(frostPanelText) {
            alignment = TextAnchor.MiddleRight };
        frostPanelCentered = new GUIStyle(frostPanelText) {
            alignment = TextAnchor.MiddleCenter, wordWrap = true };
        frostPopupHeading = new GUIStyle(frostPanelCentered) {
            fontSize = Mathf.Max(14, Mathf.RoundToInt(20 * s)) };
        EnsureDispatchPanelStyles(s);
        if (tooltipFrame == null) tooltipFrame = CreateFrostFrame(dispatch: false);
        if (dispatchFrame == null) dispatchFrame = CreateFrostFrame(dispatch: true);
        if (tooltipPointer == null)
            tooltipPointer = LiteMask(32, 12, (x, y) =>
                Mathf.Abs(x - 15.5f) < (y + 1) * 1.33f ? (byte)237 : (byte)0);
    }

    private void DrawFrostNodePopup()
    {
        if (Event.current.type != EventType.Repaint || mapDragging ||
            frostPopupPointer is not Vector2 pointer || !IsPointerOverMap(pointer) ||
            Camera.main is not Camera camera)
            return;

        RescueNode hoveredNode = GetNodeAt(pointer);
        if (hoveredNode == null || hoveredNode.nodeId == "Camp") return;

        string id = hoveredNode.nodeId;
        if (scenario.GetDefinition(id) is not RescueNodeDefinition definition) return;
        EnsureLiteStyles();
        float s = LiteScale;
        Rect bounds = UsesLiteBoard ? LiteMapRect :
            Rect.MinMaxRect(PanelWidth + LitePad, LitePad, Screen.width - LitePad, Screen.height - LiteBottom);
        float width = Mathf.Min(254 * s, bounds.width);
        float height = 143 * s;
        if (width < 155 * s || bounds.height < height) return;

        // Anchor in screen space, flip near the top edge, and keep the plate off the node.
        Vector3 projected = camera.WorldToScreenPoint(hoveredNode.transform.position);
        if (projected.z <= 0f) return;
        Vector2 anchor = new(projected.x, Screen.height - projected.y);
        float gap = 12 * s;
        bool above = anchor.y - gap - height >= bounds.yMin;
        Rect box = new(Mathf.Clamp(anchor.x - width * .5f, bounds.xMin, bounds.xMax - width),
            above ? anchor.y - gap - height : Mathf.Min(anchor.y + gap, bounds.yMax - height),
            width, height);
        // In the rare case where the map band is too short to flip vertically,
        // move beside the node instead; the tooltip must not cover the target.
        bool beside = anchor.y >= box.yMin - 3 * s && anchor.y <= box.yMax + 3 * s;
        if (beside)
        {
            if (anchor.x + gap + width <= bounds.xMax) box.x = anchor.x + gap;
            else if (anchor.x - gap - width >= bounds.xMin) box.x = anchor.x - gap - width;
            else return;
        }
        if (!beside)
        {
            float pointerX = Mathf.Clamp(anchor.x, box.x + 22 * s, box.xMax - 22 * s);
            Rect tip = new(pointerX - 8 * s, above ? box.yMax - 1 : box.yMin - 8 * s + 1, 16 * s, 8 * s);
            Color previous = GUI.color;
            GUI.color = new Color32(TooltipGlass.r, TooltipGlass.g, TooltipGlass.b, 255);
            GUI.DrawTextureWithTexCoords(tip, tooltipPointer, above ? new Rect(0, 0, 1, 1) : new Rect(0, 1, 1, -1));
            GUI.color = previous;
        }
        DrawFrostFrame(box, tooltipFrame, 24 * s);
        DrawFrostTitle(new Rect(box.x + 14 * s, box.y + 7 * s, width - 28 * s, 28 * s),
            id.ToUpperInvariant(), frostPopupHeading);
        DrawFrostRule(box.x + 17 * s, box.y + 39 * s, width - 34 * s, divisions: 0);

        bool countsKnown = scenario.AreSurvivorCountsKnown(id);
        int knownBranchCount = 0;
        bool frontier = !countsKnown && scenario.TryGetDiscoveredBranchCount(id, out knownBranchCount);
        int? here = countsKnown ? scenario.CountRemainingHere(id) : null;
        int? beyond = countsKnown ? scenario.CountRemainingBeyond(id) :
            frontier ? knownBranchCount : null;
        int? difficulty = scenario.IsDifficultyKnown(id) ? definition.difficulty : null;
        DrawFrostStat(box, 48 * s, "Survivors here", here);
        DrawFrostStat(box, 73 * s, frontier ? "Branch total" : "Survivors beyond", beyond);
        DrawFrostStat(box, 98 * s, "Minimum LP to pass", difficulty);
    }

    private static void DrawFrostTitle(Rect rect, string title, GUIStyle style, bool outline = false)
    {
        if (Event.current.type != EventType.Repaint) return;
        int regularSize = style.fontSize;
        bool wrap = style.wordWrap;
        style.wordWrap = false;
        float textWidth = style.CalcSize(new GUIContent(title)).x;
        if (textWidth > rect.width)
            style.fontSize = Mathf.Max(1, Mathf.FloorToInt(regularSize * rect.width / textWidth));
        LabelLite(rect, title, style, outline: outline);
        style.fontSize = regularSize;
        style.wordWrap = wrap;
    }

    private void DrawFrostStat(Rect box, float rowY, string name, int? value)
    {
        float s = LiteScale;
        Color color = value.HasValue ? FrostUiTheme.Enamel : FrostUiTheme.Frost;
        float x = box.x + 17 * s, y = box.y + rowY;
        LabelLite(new Rect(x, y, box.width - 67 * s, 25 * s), name, frostPanelText, color, outline: false);
        LabelLite(new Rect(box.xMax - 50 * s, y, 32 * s, 25 * s), value?.ToString() ?? "?",
            frostPanelValue, value.HasValue ? FrostUiTheme.Rescued : FrostUiTheme.Frost, outline: false);
    }

    private bool FrostPanelButton(Rect rect, string text, bool allowed = true, bool framed = true)
    {
        bool enabled = GUI.enabled;
        bool active = enabled && allowed;
        GUI.enabled = active;
        bool pressed = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        GUI.enabled = enabled;
        if (framed) DrawFrostFrame(rect, dispatchFrame, 12 * LiteScale,
            active ? Color.white : new Color(1f, 1f, 1f, .60f));
        LabelLite(rect, text, frostPanelCentered, active ? FrostUiTheme.Enamel : FrostUiTheme.Frost, outline: !framed);
        return pressed;
    }

    private static Texture2D CreateFrostFrame(bool dispatch)
    {
        var texture = new Texture2D(FrostFrameSize, FrostFrameSize, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[FrostFrameSize * FrostFrameSize];
        for (int y = 0; y < FrostFrameSize; y++)
        for (int x = 0; x < FrostFrameSize; x++)
        {
            float cx = Mathf.Min(x, FrostFrameSize - 1 - x);
            float cy = Mathf.Min(y, FrostFrameSize - 1 - y);
            float edge = Mathf.Min(cx, cy);
            Color32 color = TooltipGlass;
            if (dispatch)
            {
                // A stepped shoulder with a tapered corner and two enamel rails.
                float shoulder = Mathf.Lerp(5, 0, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(7, 19, Mathf.Max(cx, cy))));
                float inset = Mathf.Min(edge - shoulder, (cx + cy - 16) * .707f);
                if (inset < 0) continue;
                float top = Mathf.Clamp01((y - (FrostFrameSize - FrostFrameCorner)) / (float)FrostFrameCorner);
                color = Color32.Lerp(DispatchGlass, DispatchFrost, top * .85f);
                if (inset >= 4 && inset < 5.5f) color = new Color32(188, 207, 214, 224);
                if (inset >= 8 && inset < 9.2f) color = new Color32(121, 150, 165, 185);
            }
            else
            {
                // Inset, scalloped corners with a small circular fastener at each corner.
                float radius = Mathf.Sqrt((cx - 9) * (cx - 9) + (cy - 9) * (cy - 9));
                float inner = Mathf.Min(edge - 9, radius - 7.5f);
                if (inner >= 0 && inner < 1.65f) color = FrostUiTheme.GlassRim;
                if (radius >= 2.3f && radius <= 3.9f) color = FrostUiTheme.GlassEtch;
            }
            pixels[y * FrostFrameSize + x] = color;
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static void DrawFrostFrame(Rect box, Texture2D texture, float cornerSize, Color? tint = null)
    {
        if (Event.current.type != EventType.Repaint || box.width <= 0 || box.height <= 0) return;
        float corner = Mathf.Min(cornerSize, (Mathf.Min(box.width, box.height) - 1) * .5f);
        const float uvCorner = (float)FrostFrameCorner / FrostFrameSize;
        Color previous = GUI.color;
        GUI.color = tint ?? Color.white;
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
        {
            float Slice(int part, float length) => part == 0 ? 0 : part == 1 ? corner :
                part == 2 ? length - corner : length;
            float x = Mathf.Round(box.x + Slice(col, box.width));
            float y = Mathf.Round(box.y + Slice(row, box.height));
            float right = Mathf.Round(box.x + Slice(col + 1, box.width));
            float bottom = Mathf.Round(box.y + Slice(row + 1, box.height));
            Rect target = Rect.MinMaxRect(x, y, right, bottom);
            Rect source = new(col == 0 ? 0 : col == 1 ? uvCorner : 1 - uvCorner,
                row == 0 ? 1 - uvCorner : row == 1 ? uvCorner : 0,
                col == 1 ? 1 - 2 * uvCorner : uvCorner, row == 1 ? 1 - 2 * uvCorner : uvCorner);
            GUI.DrawTextureWithTexCoords(target, texture, source);
        }
        GUI.color = previous;
    }
}
