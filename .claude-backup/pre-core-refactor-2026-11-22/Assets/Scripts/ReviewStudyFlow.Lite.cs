using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class ReviewStudyFlow
{
    private readonly Dictionary<string, int> liteRouteSlots = new();
    private RescueNode liteDestination;
    private int liteDraftPoints = 1;
    private GUIStyle liteText, liteSmall, liteCaption, liteHeading, liteNumber, liteTimer,
        liteSupplyLabel, liteDayLabel, liteMeterCaption, liteArrowLabel;
    private static Font studyFont;
    private float liteStyleScale;
    private Texture2D liteArrowMask, liteArrowEtch, liteLogisticsMask, liteEdgeShade;

    private bool IsLitePlanning => phase == Phase.Practice || phase == Phase.Discuss ||
        phase == Phase.Reconsider || phase == Phase.FinalPlan;
    private bool IsLiteResult => phase == Phase.PracticeResult || phase == Phase.Result;
    private bool UsesLiteBoard => IsLitePlanning || IsLiteResult;
    private float LiteScale => Mathf.Clamp(Mathf.Min(Screen.width / 1060f, Screen.height / 620f), .55f, 1.35f);
    private float LitePad => 14f * LiteScale;
    private float LiteTop => 139f * LiteScale;
    private float LiteBottom => 65f * LiteScale;
    private float LiteRail => 85f * LiteScale;
    private float LiteSideWidth => (IsLiteResult ? 326f : 312f) * LiteScale;
    private float LiteMessageHeight => phase == Phase.Reconsider ? 94f * LiteScale : 0f;
    // The tactical HUD is an overlay. The camera therefore fits the authored map
    // to the entire viewport rather than reserving dashboard columns around it.
    private Rect LiteMapRect => new(0f, 0f, Screen.width, Screen.height);

    private void OpenLiteDraft(RescueNode node)
    {
        if (node == null || node.nodeId == "Camp") return;
        var selection = NodeSelectionManager.Instance;
        if (selection != null && selection.SelectedNode != node) selection.SelectNode(node);
        liteDestination = node;
        liteDraftPoints = draft.Find(leg => leg.targetNodeId == node.nodeId)?.assignedGroups ?? 1;
        error = null;
    }

    private void CloseLiteDraft()
    {
        if (liteDestination != null) NodeSelectionManager.Instance?.ClearSelection();
        liteDestination = null;
        liteDraftPoints = 1;
    }

    private int LiteDraftMax(ExpeditionAssignment current)
    {
        if (current == null && draft.Count >= game.MaxExpeditionsPerDay) return 0;
        return Mathf.Max(0, Mathf.Min(game.DailyDeploymentCap, game.LogisticsRemaining) -
            draft.Sum(leg => leg.assignedGroups) + (current?.assignedGroups ?? 0));
    }

    private void SaveLiteDraft(string id, ExpeditionAssignment current, int max)
    {
        if (liteDraftPoints < 1 || liteDraftPoints > max) return;
        if (current == null)
        {
            int slot = liteRouteSlots.ContainsValue(0) ? 1 : 0;
            liteRouteSlots[id] = slot;
            draft.Add(new ExpeditionAssignment { targetNodeId = id, assignedGroups = liteDraftPoints });
        }
        else current.assignedGroups = liteDraftPoints;
        CloseLiteDraft();
    }

    private void DrawLiteBoard()
    {
        DrawTacticalHud();
    }

    private void DrawLiteHeader()
    {
        float s = LiteScale, p = LitePad;
        float right = Screen.width - p;
        Rect resources = new(p, p, Mathf.Min(540 * s, Screen.width - 2 * p - 168 * s), LiteTop - p);
        int reserved = IsLitePlanning ? draft.Sum(leg => leg.assignedGroups) : 0;
        int pool = game.LogisticsRemaining;
        int today = game.GameOver ? 0 : Mathf.Max(0, Mathf.Min(game.DailyDeploymentCap, pool) - reserved);
        int day = IsLiteResult && !game.GameOver ? game.CurrentDay - 1 : game.CurrentDay;
        TintLite(new Rect(resources.x, resources.y - 8 * s, 103 * s, 96 * s),
            today.ToString(), liteNumber, FrostUiTheme.Enamel);
        LabelLite(new Rect(resources.x + 105 * s, resources.y + 8 * s, resources.width - 105 * s, 36 * s),
            game.GameOver ? "Search complete" : IsLiteResult ? "LP ready next day" : "LP remaining today", liteSupplyLabel);
        LabelLite(new Rect(resources.x + 105 * s, resources.y + 47 * s, resources.width - 105 * s, 31 * s),
            $"Day {day}/{game.TotalDays}  ·  {pool} LP in pool", liteDayLabel);

        int granted = game.TotalLogisticsGranted;
        if (granted > 0)
        {
            float step = Mathf.Min(34 * s, resources.width / granted);
            float size = Mathf.Min(28 * s, step * .86f);
            for (int i = 0; i < granted; i++)
            {
                Color color = i < pool - reserved ? FrostUiTheme.Enamel :
                    i < pool ? FrostUiTheme.Amber : FrostUiTheme.Steel;
                Rect glyph = new(resources.x + i * step, resources.y + 84 * s, size, size);
                TintLiteTexture(new Rect(glyph.x + 1, glyph.y + 1, size, size), liteLogisticsMask, FrostUiTheme.Coal);
                TintLiteTexture(glyph, liteLogisticsMask, color);
            }
        }

        string stage = phase switch {
            Phase.Practice => "Practice",
            Phase.Discuss => "Discuss",
            Phase.Reconsider => "Reconsider",
            Phase.FinalPlan => "Final plan",
            _ => "Outcome"
        };
        int seconds = LiteSecondsRemaining;
        string time = phase == Phase.Discuss || phase == Phase.Reconsider ? $"{seconds / 60}:{seconds % 60:00}" : "";
        Rect timer = new(right - 158 * s, p, 158 * s, 66 * s);
        LabelLite(new Rect(timer.x + 9 * s, timer.y + 5 * s, timer.width - 13 * s, 23 * s),
            stage.ToUpperInvariant(), liteCaption);
        if (time.Length > 0)
            TintLite(new Rect(timer.x + 9 * s, timer.y + 23 * s, timer.width - 13 * s, 40 * s),
                time, liteTimer, seconds is > 0 and <= 15 ? FrostUiTheme.Oxide : FrostUiTheme.Enamel);
        else
            LabelLite(new Rect(timer.x + 8 * s, timer.y + 24 * s, timer.width - 12 * s, 35 * s),
                IsLiteResult ? "Results are in" : "Choose a route", liteSmall);
        DrawFrostRule(timer.x + 9 * s, timer.yMax + 2 * s, timer.width - 18 * s, divisions: 8);

        if (phase == Phase.Reconsider)
        {
            Rect note = new(p, LiteTop, Screen.width - 2 * p, LiteMessageHeight - 7 * s);
            DrawLiteReadableNote(new Rect(note.x + 9 * s, note.y + 5 * s, note.width - 18 * s, note.height - 10 * s),
                message ?? "");
        }
    }

    private void DrawLiteReadableNote(Rect rect, string value)
    {
        if (liteText.CalcHeight(new GUIContent(value), rect.width) <= rect.height)
            LabelLite(rect, value, liteText);
        else
        {
            Rect content = new(0, 0, rect.width - 21, liteText.CalcHeight(new GUIContent(value), rect.width - 21));
            liteNoteScroll = GUI.BeginScrollView(rect, liteNoteScroll, content);
            LabelLite(content, value, liteText);
            GUI.EndScrollView();
        }
    }

    private Vector2 liteNoteScroll;

    private void DrawLiteRescueMeter()
    {
        float s = LiteScale, p = LitePad;
        var branches = scenario.CurrentScenario.branches;
        // The exact total was already public after the first dispatch. Prior to
        // that, use only the displayed reports on maps whose counts are reports.
        bool reported = branches.Count > 0 && game.CurrentDay == 1 && !IsLiteResult;
        int saved = game.TotalRescued;
        int count = reported ? branches.Sum(b => b.reportedSurvivors) : game.TotalSurvivors;
        int left = Mathf.Max(0, count - saved);
        float y = LiteMapRect.y + 4 * s;
        float height = Mathf.Min(235 * s, LiteMapRect.height - 78 * s);
        if (height <= 0) return;
        Rect frame = new(p, y, LiteRail - 6 * s, height + 76 * s);
        LabelLite(new Rect(frame.x + 7 * s, y + 7 * s, frame.width - 10 * s, 25 * s),
            reported ? $"~{left} LEFT" : $"{left} LEFT", liteMeterCaption);
        Rect bar = new(frame.x + 10 * s, y + 38 * s, 32 * s, height);
        float redHeight = count > 0 ? height * left / (left + saved) : 0;
        if (count == 0) FillLite(bar, FrostUiTheme.Steel);
        if (redHeight > 0) FillLite(new Rect(bar.x, bar.y, bar.width, redHeight), FrostUiTheme.Oxide);
        if (saved > 0) FillLite(new Rect(bar.x, bar.y + redHeight, bar.width, height - redHeight), FrostUiTheme.Rescued);
        DrawFrostMeterTicks(bar, count);
        if (redHeight >= 28 * s)
            LabelLite(new Rect(bar.x + 3 * s, bar.y + (redHeight - 23 * s) * .5f,
                bar.width - 3 * s, 23 * s), left.ToString(), liteText, FrostUiTheme.Coal, outline: false);
        if (saved > 0 && height - redHeight >= 28 * s)
            LabelLite(new Rect(bar.x + 3 * s, bar.y + redHeight + (height - redHeight - 23 * s) * .5f,
                bar.width - 3 * s, 23 * s), saved.ToString(), liteText, FrostUiTheme.Coal, outline: false);
        LabelLite(new Rect(frame.x + 7 * s, bar.yMax + 4 * s, frame.width - 10 * s, 23 * s),
            $"{saved} SAVED", liteMeterCaption);
    }

    private Rect LiteSideRect => Rect.MinMaxRect(LitePad + LiteRail, LiteMapRect.y + 4 * LiteScale,
        LitePad + LiteRail + LiteSideWidth, LiteMapRect.yMax - 3 * LiteScale);

    private void DrawLiteDispatchEditor()
    {
        float s = LiteScale;
        string id = liteDestination.nodeId;
        var current = draft.Find(leg => leg.targetNodeId == id);
        int max = LiteDraftMax(current);
        Rect side = LiteSideRect;
        Rect panel = new(side.x, side.y, side.width, Mathf.Min(side.height, 500 * s));
        if (panel.height <= 0) return;
        // Compress the vertical layout when the intervention takes up map space.
        float v = Mathf.Min(s, panel.height / 390f);
        Rect Row(float inset, float y, float height) =>
            new(panel.x + inset * s, panel.y + y * v, panel.width - 2 * inset * s, height * v);
        DrawDispatchPlate(panel, ref dispatchShell, DispatchPlate.Shell);

        Rect tab = new(panel.center.x - 85 * s, panel.y - 7 * v, 170 * s, 24 * v);
        DrawDispatchPlate(tab, ref dispatchTab, DispatchPlate.Tab);
        DrawFrostTitle(new Rect(tab.x + 12 * s, tab.y, tab.width - 24 * s, tab.height),
            current == null ? "PLAN A DISPATCH" : "EDIT DISPATCH", frostDispatchCaption, outline: true);
        DrawFrostTitle(Row(35, 24, 47), id.ToUpperInvariant(), frostDispatchTitle, outline: true);
        if (FrostPanelButton(new Rect(panel.xMax - 43 * s, panel.y + 13 * v, 30 * s, 30 * s), "×", framed: false))
        {
            CloseLiteDraft();
            return;
        }

        Rect plaque = Row(7, 96, 73);
        DrawDispatchPlate(plaque, ref dispatchPlaque, DispatchPlate.Plaque);
        float stampSize = 33 * v;
        Rect marker = new(plaque.center.x - stampSize * .5f, plaque.y + 10 * v, stampSize, stampSize);
        TintLiteTexture(marker, liteLogisticsMask, current == null ? FrostUiTheme.Enamel : LiteRouteColor(LiteSlot(id)));
        LabelLite(Row(39, 142, 21), "DESTINATION", frostDispatchCaption, FrostUiTheme.Frost);

        DrawDispatchSection(panel, panel.y + 207 * v);
        Rect minus = new(panel.center.x - 105 * s, panel.y + 239 * v, 45 * s, 43 * v);
        Rect plus = new(panel.center.x + 60 * s, minus.y, minus.width, minus.height);
        Rect amount = new(minus.xMax + 3 * s, minus.y, plus.x - minus.xMax - 6 * s, minus.height);
        if (max == 0)
            LabelLite(amount, draft.Count >= game.MaxExpeditionsPerDay ? "Dispatch limit reached" : "No LP available",
                frostPanelCentered, FrostUiTheme.Frost);
        else
        {
            LabelLite(new Rect(amount.x, amount.y - 6 * v, amount.width, 37 * v), liteDraftPoints.ToString(),
                frostDispatchCount);
            LabelLite(new Rect(amount.x, amount.y + 29 * v, amount.width, 22 * v), "LP to send",
                frostDispatchCaption, FrostUiTheme.Frost);
        }
        if (DispatchInstrumentButton(minus, "−1", liteDraftPoints > 1 && max > 0)) liteDraftPoints--;
        if (DispatchInstrumentButton(plus, "+1", liteDraftPoints < max)) liteDraftPoints++;

        float footer = panel.yMax - (current == null ? 64 : 92) * v;
        Rect add = new(panel.x + 27 * s, footer, panel.width - 54 * s, 39 * v);
        if (DispatchInstrumentButton(add, current == null ? "Add dispatch" : "Save dispatch",
            max > 0 && liteDraftPoints <= max, primary: true))
        {
            SaveLiteDraft(id, current, max);
            return;
        }
        if (current != null && DispatchInstrumentButton(new Rect(add.x, add.yMax + 4 * v, add.width, 30 * v),
            "Remove dispatch"))
        {
            draft.Remove(current);
            liteRouteSlots.Remove(id);
            CloseLiteDraft();
        }
    }

    private void DrawLiteOutcome()
    {
        Rect r = LiteSideRect;
        float pad = 9 * LiteScale;
        r = new Rect(r.x + pad, r.y + pad, r.width - 2 * pad, r.height - 2 * pad);
        string frontier = DiscoveredFrontier();
        DrawLiteReadableNote(r, Summary() + (frontier.Length > 0 ? "\n\n" + frontier : ""));
    }

    private int LiteSecondsRemaining => Mathf.Max(0, Mathf.CeilToInt(phaseEnds - Time.unscaledTime));

    private void DrawLiteControls()
    {
        float s = LiteScale, p = LitePad;
        Rect arrow = new(Screen.width - p - 195 * s, Screen.height - LiteBottom + 4 * s, 195 * s, Mathf.Max(44f, 49 * s));
        Rect zoom = new(p, arrow.y, 140 * s, arrow.height);
        var camera = GetMapCamera();
        if (camera != null)
        {
            if (FrostPanelButton(new Rect(zoom.x, zoom.y, 43 * s, zoom.height), "+", camera.CanZoomIn)) camera.ZoomIn();
            if (FrostPanelButton(new Rect(zoom.x + 48 * s, zoom.y, 44 * s, zoom.height), "1:1", !camera.IsDefaultView)) camera.ResetView();
            if (FrostPanelButton(new Rect(zoom.x + 97 * s, zoom.y, 43 * s, zoom.height), "−", camera.CanZoomOut)) camera.ZoomOut();
        }

        string action = phase switch {
            Phase.Practice => "Run practice",
            Phase.PracticeResult => "Start scenario",
            Phase.Discuss => "Lock plan",
            Phase.FinalPlan => "Lock final plan",
            _ => "Continue"
        };
        bool ready = (phase != Phase.Discuss && phase != Phase.Reconsider) || LiteSecondsRemaining == 0;
        TintLiteTexture(arrow, liteArrowMask, FrostUiTheme.Coal);
        TintLiteTexture(arrow, liteArrowEtch, ready ? FrostUiTheme.Amber : FrostUiTheme.Steel);
        LabelLite(new Rect(arrow.x + 4 * s, arrow.y, arrow.width - 26 * s, arrow.height),
            action, liteArrowLabel,
            ready ? FrostUiTheme.Enamel : FrostUiTheme.Frost, outline: false);
        if (LiteArrowButton(arrow, ready)) AdvanceLiteBoard();

        // Keep the zoom corner clear. Validation and the prior plan still need to be visible.
        string status = error ?? (phase == Phase.FinalPlan ? "Provisional: " + Describe(provisional) : null);
        if (!string.IsNullOrEmpty(status))
        {
            Rect notice = Rect.MinMaxRect(zoom.xMax + 14 * s, arrow.y, arrow.x - 14 * s, arrow.yMax);
            LabelLite(notice, status, liteSmall, error != null ? FrostUiTheme.Oxide : FrostUiTheme.Frost);
        }
    }

    private void AdvanceLiteBoard()
    {
        CloseLiteDraft();
        liteNoteScroll = Vector2.zero;
        switch (phase)
        {
            case Phase.Practice:
                if (Deploy()) Go(Phase.PracticeResult);
                break;
            case Phase.PracticeResult: NextScenario(); break;
            case Phase.Discuss:
                provisional = CopyPlan();
                StartIntervention();
                break;
            case Phase.Reconsider: Go(Phase.FinalPlan); break;
            case Phase.FinalPlan:
                finalPlan = CopyPlan();
                BeginForms(Phase.PreOutcome);
                break;
            case Phase.Result: BeginForms(Phase.PostOutcome); break;
        }
    }

    private static bool LiteArrowButton(Rect rect, bool ready)
    {
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && ready;
        bool pressed = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        GUI.enabled = enabled;
        return pressed;
    }

    private void EnsureLiteStyles()
    {
        int size = Mathf.Max(18, Mathf.RoundToInt(21f * LiteScale));
        if (liteText != null && liteStyleScale == LiteScale) return;
        liteStyleScale = LiteScale;
        liteText = new GUIStyle(GUI.skin.label) { font = StudyFont, fontStyle = FontStyle.Normal,
            fontSize = size, wordWrap = true, richText = false,
            normal = { textColor = FrostUiTheme.Enamel }, padding = new RectOffset(0, 0, 0, 0) };
        liteSmall = new GUIStyle(liteText) { fontSize = Mathf.Max(14, Mathf.RoundToInt(size * .82f)),
            normal = { textColor = FrostUiTheme.Frost } };
        liteCaption = new GUIStyle(liteSmall);
        liteHeading = new GUIStyle(liteText) { fontSize = Mathf.RoundToInt(size * 1.19f) };
        liteNumber = new GUIStyle(liteHeading) { fontSize = Mathf.RoundToInt(78f * LiteScale),
            alignment = TextAnchor.MiddleLeft, wordWrap = false };
        liteSupplyLabel = new GUIStyle(liteText) { fontSize = Mathf.RoundToInt(28f * LiteScale) };
        liteDayLabel = new GUIStyle(liteText) { fontSize = Mathf.RoundToInt(22f * LiteScale) };
        liteMeterCaption = new GUIStyle(liteCaption) { fontSize = Mathf.Max(12, Mathf.RoundToInt(14f * LiteScale)) };
        liteTimer = new GUIStyle(liteHeading) { fontSize = Mathf.RoundToInt(size * 1.9f) };
        liteArrowLabel = new GUIStyle(liteText) { alignment = TextAnchor.MiddleCenter };
        if (liteArrowMask == null)
            liteArrowMask = LiteMask(160, 40, (x, y) => FrostUiTheme.ArrowInterior(x, y) ? (byte)255 : (byte)0);
        if (liteArrowEtch == null) liteArrowEtch = LiteMask(160, 40, FrostUiTheme.ArrowEtch);
        if (liteLogisticsMask == null) liteLogisticsMask = LiteMask(32, 32, FrostUiTheme.LogisticsMark);
        if (liteEdgeShade == null) liteEdgeShade = LiteMask(128, 128, FrostUiTheme.EdgeShade);
        EnsureFrostPanelStyles();
    }

    internal static Font StudyFont => studyFont ??= Resources.Load<Font>("CLSRFonts/JosefinSlab-SemiBold") ??
        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    private static Texture2D LiteMask(int width, int height, System.Func<int, int, byte> opacity)
    {
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new Color32(255, 255, 255, opacity(x, y));
        result.SetPixels32(pixels);
        result.Apply(false, true);
        return result;
    }

    private static void FillLite(Rect rect, Color color) => TintLiteTexture(rect, Texture2D.whiteTexture, color);
    private void DrawFrostRule(float x, float y, float width, int divisions)
    {
        if (Event.current.type != EventType.Repaint || width <= 0) return;
        float s = LiteScale;
        float edge = Mathf.Min(width * .20f, 12 * s);
        float gap = Mathf.Min(width * .05f, 4 * s);
        FillLite(new Rect(x, y, edge, 1), FrostUiTheme.Steel);
        FillLite(new Rect(x + edge + gap, y, width - 2 * (edge + gap), 1), FrostUiTheme.Steel);
        FillLite(new Rect(x + width - edge, y, edge, 1), FrostUiTheme.Steel);
        if (divisions <= 0) return;
        for (int i = 0; i <= divisions; i++)
            FillLite(new Rect(x + width * i / divisions, y + 2, 1, (i % 4 == 0 ? 6 : 3) * s),
                i % 4 == 0 ? FrostUiTheme.Frost : FrostUiTheme.Steel);
    }

    private void DrawFrostMeterTicks(Rect bar, int total)
    {
        if (Event.current.type != EventType.Repaint || total <= 0) return;
        int divisions = Mathf.Min(total, 12);
        float s = LiteScale;
        for (int i = 0; i <= divisions; i++)
        {
            bool terminal = i == 0 || i == divisions;
            FillLite(new Rect(bar.xMax + 3 * s, bar.y + bar.height * i / divisions, (terminal ? 10 : 5) * s, 1),
                terminal ? FrostUiTheme.Frost : FrostUiTheme.Steel);
        }
    }

    private static void TintLiteTexture(Rect rect, Texture texture, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, texture);
        GUI.color = previous;
    }
    private static void TintLite(Rect rect, string text, GUIStyle style, Color color) =>
        LabelLite(rect, text, style, color);

    private static void LabelLite(Rect rect, string text, GUIStyle style, Color? color = null, bool outline = true)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
        // Draw every label in the normal state, even when its button is hovered or pressed.
        GUIContent content = new(text);
        Color previous = style.normal.textColor;
        if (outline)
        {
            style.normal.textColor = FrostUiTheme.Coal;
            float offset = style.fontSize >= 40 ? 2f : 1f;
            style.Draw(new Rect(rect.x - offset, rect.y, rect.width, rect.height), content, false, false, false, false);
            style.Draw(new Rect(rect.x + offset, rect.y, rect.width, rect.height), content, false, false, false, false);
            style.Draw(new Rect(rect.x, rect.y - offset, rect.width, rect.height), content, false, false, false, false);
            style.Draw(new Rect(rect.x, rect.y + offset, rect.width, rect.height), content, false, false, false, false);
        }
        style.normal.textColor = color ?? previous;
        style.Draw(rect, content, false, false, false, false);
        style.normal.textColor = previous;
    }
}
