using System.Collections.Generic;
using System.Linq;
using CLSR.Presentation;
using CLSR.Simulation;
using UnityEngine;

public sealed partial class ReviewStudyFlow
{
    private static readonly Color TacticalPanel = new(0.047f, 0.059f, 0.051f, 0.82f);
    private static readonly Color TacticalPanelStrong = new(0.035f, 0.043f, 0.039f, 0.94f);
    private static readonly Color TacticalBorder = new(0.64f, 0.60f, 0.44f, 0.34f);
    private static readonly Color TacticalDivider = new(0.80f, 0.78f, 0.66f, 0.18f);
    private static readonly Color TacticalText = new(0.86f, 0.85f, 0.80f, 1f);
    private static readonly Color TacticalMuted = new(0.63f, 0.62f, 0.56f, 1f);
    private static readonly Color TacticalDim = new(0.46f, 0.48f, 0.43f, 1f);
    private static readonly Color TacticalGold = new(0.78f, 0.65f, 0.42f, 1f);
    private static readonly Color TacticalGoldBright = new(0.88f, 0.74f, 0.48f, 1f);
    private static readonly Color TacticalSuccess = new(0.54f, 0.66f, 0.56f, 1f);
    private static readonly Color TacticalWarning = new(0.78f, 0.58f, 0.35f, 1f);
    private static readonly Color TacticalDanger = new(0.74f, 0.36f, 0.29f, 1f);

    private GUIStyle tacticalLabel, tacticalSmall, tacticalTiny, tacticalHeading, tacticalValue,
        tacticalCentered, tacticalTimer, tacticalTooltip;
    private float tacticalStyleScale;
    private bool tacticalMenuOpen;
    private bool tacticalRoutesVisible = true;
    private bool tacticalNoticeInitialized;
    private Phase tacticalPreviousPhase;
    private string tacticalPreviousSummary;
    private readonly Queue<string> tacticalNoticeQueue = new();
    private string tacticalNotice;
    private float tacticalNoticeStarted;
    private float tacticalNoticeUntil;

    // Region geometry lives in the engine-free TacticalLayout so it can be
    // checked for overlaps at common resolutions without the editor.
    private TacticalLayout tacticalLayout;
    private Transform tacticalCamp;
    private TacticalLayout Layout
    {
        get
        {
            if (tacticalLayout == null || tacticalLayout.Width != Screen.width || tacticalLayout.Height != Screen.height)
                tacticalLayout = new TacticalLayout(Screen.width, Screen.height);
            return tacticalLayout;
        }
    }

    private static Rect ToRect(RectF r) => new(r.X, r.Y, r.Width, r.Height);
    private float TacticalScale => Layout.Scale;
    private Rect TacticalMenuButtonRect => ToRect(Layout.MenuButton);
    private Rect TacticalObjectivesRect => ToRect(Layout.Objectives);
    private Rect TacticalMinimapRect => ToRect(Layout.Minimap);
    private Rect TacticalCameraStripRect => ToRect(Layout.CameraStrip);
    private Rect TacticalInterventionRect => ToRect(Layout.Intervention);
    private Rect TacticalToolbarRect => ToRect(Layout.Toolbar);
    private Rect TacticalCommandRect => ToRect(Layout.Command);
    private Rect TacticalPlanRect => ToRect(Layout.PlanStrip);
    private Rect TacticalInspectorRect => ToRect(Layout.Inspector);

    private bool IsPointerOverTacticalHud(Vector2 pointer) =>
        UseTacticalHud && UsesLiteBoard && Layout.IsOverHud(pointer.x, pointer.y,
            phase == Phase.Reconsider && !string.IsNullOrWhiteSpace(message), tacticalMenuOpen);

    private void UpdateTacticalNotifications()
    {
        if (!UseTacticalHud || !UsesLiteBoard || game == null) return;
        if (!tacticalNoticeInitialized)
        {
            tacticalNoticeInitialized = true;
            tacticalPreviousPhase = phase;
            tacticalPreviousSummary = game.LastDaySummary;
            QueueTacticalNotice(TacticalPhaseName());
        }
        else
        {
            if (tacticalPreviousPhase != phase)
            {
                tacticalPreviousPhase = phase;
                QueueTacticalNotice(TacticalPhaseName());
            }
            if (tacticalPreviousSummary != game.LastDaySummary)
            {
                tacticalPreviousSummary = game.LastDaySummary;
                QueueTacticalNotice(game.LastDaySummary);
            }
        }

        if (string.IsNullOrEmpty(tacticalNotice) && tacticalNoticeQueue.Count > 0)
        {
            tacticalNotice = tacticalNoticeQueue.Dequeue();
            tacticalNoticeStarted = Time.unscaledTime;
            tacticalNoticeUntil = Time.unscaledTime + 4.5f;
        }
        else if (!string.IsNullOrEmpty(tacticalNotice) && Time.unscaledTime >= tacticalNoticeUntil)
        {
            tacticalNotice = null;
        }
    }

    private void QueueTacticalNotice(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string compact = value.Replace('\n', ' ').Trim();
        if (compact.Length > 132) compact = compact[..129] + "…";
        if (compact != tacticalNotice && !tacticalNoticeQueue.Contains(compact))
            tacticalNoticeQueue.Enqueue(compact);
    }

    private string TacticalPhaseName() => phase switch {
        Phase.Practice => "PRACTICE PLANNING ACTIVE",
        Phase.PracticeResult => "PRACTICE SEARCH COMPLETE",
        Phase.Discuss => "TEAM PLANNING ACTIVE",
        Phase.Reconsider => "RECONSIDERATION WINDOW ACTIVE",
        Phase.FinalPlan => "FINAL PLAN AUTHORIZATION",
        Phase.Result => "SEARCH OUTCOME RECEIVED",
        _ => "MISSION STATUS UPDATED"
    };

    private void DrawTacticalHud()
    {
        EnsureLiteStyles();
        EnsureTacticalStyles();
        HandleTacticalKeyboard();

        DrawLiteRoutes();
        DrawTacticalObjectives();
        DrawTacticalTimer();
        DrawTacticalNotice();
        DrawTacticalIntervention();
        DrawTacticalMinimap();
        DrawTacticalCameraStrip();
        DrawTacticalToolbar();
        DrawTacticalInspector();
        DrawTacticalPlanStrip();
        DrawTacticalCommandGrid();
        DrawTacticalMenu();
        DrawFrostNodePopup();
        DrawTacticalTooltip();
    }

    private void HandleTacticalKeyboard()
    {
        Event input = Event.current;
        if (input.type != EventType.KeyDown || input.keyCode != KeyCode.Escape) return;
        if (tacticalMenuOpen) tacticalMenuOpen = false;
        else if (liteDestination != null) CloseLiteDraft();
        else tacticalMenuOpen = true;
        input.Use();
    }

    private void EnsureTacticalStyles()
    {
        float s = TacticalScale;
        if (tacticalLabel != null && Mathf.Approximately(tacticalStyleScale, s)) return;
        tacticalStyleScale = s;
        int regular = Mathf.Clamp(Mathf.RoundToInt(12f * s), 10, 22);
        tacticalLabel = new GUIStyle(GUI.skin.label) {
            font = StudyFont, fontSize = regular, wordWrap = false,
            alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0),
            normal = { textColor = TacticalText }
        };
        tacticalSmall = new GUIStyle(tacticalLabel) { fontSize = Mathf.Max(9, Mathf.RoundToInt(10f * s)) };
        tacticalTiny = new GUIStyle(tacticalLabel) { fontSize = Mathf.Max(8, Mathf.RoundToInt(9f * s)) };
        tacticalHeading = new GUIStyle(tacticalLabel) {
            fontSize = Mathf.Max(10, Mathf.RoundToInt(11f * s)), fontStyle = FontStyle.Bold
        };
        tacticalValue = new GUIStyle(tacticalLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
        tacticalCentered = new GUIStyle(tacticalSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        tacticalTimer = new GUIStyle(tacticalHeading) {
            fontSize = Mathf.Max(13, Mathf.RoundToInt(16f * s)), alignment = TextAnchor.MiddleCenter
        };
        tacticalTooltip = new GUIStyle(tacticalSmall) {
            alignment = TextAnchor.MiddleLeft, wordWrap = false,
            padding = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), 0, 0)
        };
    }

    private void DrawTacticalObjectives()
    {
        Rect menu = TacticalMenuButtonRect;
        if (TacticalButton(menu, "MENU", "Mission and view controls", true, tacticalMenuOpen))
            tacticalMenuOpen = !tacticalMenuOpen;

        Rect panel = TacticalObjectivesRect;
        DrawTacticalPanel(panel, TacticalPanel);
        float s = TacticalScale, pad = 8f * s;
        TacticalTextAt(new Rect(panel.x + pad, panel.y + 3f * s, panel.width - 2 * pad, 18f * s),
            "MISSION STATUS", tacticalHeading, TacticalGold);
        DrawTacticalRule(panel.x + pad, panel.y + 21f * s, panel.width - 2 * pad);
        int knownTotal = TacticalKnownSurvivorTotal();
        DrawTacticalMetric(panel, 27f, "RESCUED", knownTotal > 0 ? $"{game.TotalRescued} / {knownTotal}" : game.TotalRescued.ToString());
        int available = Mathf.Max(0, game.LogisticsRemaining - draft.Sum(item => item.assignedGroups));
        DrawTacticalMetric(panel, 47f, "AVAILABLE LP", available.ToString());
        DrawTacticalMetric(panel, 67f, "DISPATCHES", $"{draft.Count} / {game.MaxExpeditionsPerDay}");
        DrawTacticalMetric(panel, 87f, "OPERATION DAY", $"{game.CurrentDay} / {game.TotalDays}");
    }

    // The true survivor total is hidden information until the scenario ends.
    // Branch reports are estimates (Cedar Valley's overcount by one), so even
    // an exact total after Day 1 would let players infer unrevealed counts.
    private int TacticalKnownSurvivorTotal()
    {
        PublicView view = scenario.View();
        if (view.FinalTotalSurvivors.HasValue) return view.FinalTotalSurvivors.Value;
        // Known exactly only when every first-level branch total is public (the practice map).
        int remaining = 0;
        foreach (PublicNode root in view.ChildrenOf(ScenarioSpec.CampId))
        {
            if (!root.KnownBranchRemaining.HasValue) return 0;
            remaining += root.KnownBranchRemaining.Value;
        }
        return remaining + view.TotalRescued;
    }

    private void DrawTacticalMetric(Rect panel, float baselineY, string label, string value)
    {
        float s = TacticalScale, pad = 8f * s;
        Rect row = new(panel.x + pad, panel.y + baselineY * s, panel.width - 2 * pad, 17f * s);
        TacticalTextAt(new Rect(row.x, row.y, row.width * .64f, row.height), label, tacticalTiny, TacticalMuted);
        TacticalTextAt(new Rect(row.x + row.width * .60f, row.y, row.width * .40f, row.height), value,
            tacticalValue, TacticalText);
        DrawTacticalRule(row.x, row.yMax, row.width, TacticalDivider);
    }

    private void DrawTacticalTimer()
    {
        float s = TacticalScale;
        Rect panel = new(Screen.width * .5f - 49f * s, 0, 98f * s, 31f * s);
        DrawTacticalPanel(panel, TacticalPanelStrong);
        bool timed = phase is Phase.Discuss or Phase.Reconsider;
        int seconds = LiteSecondsRemaining;
        string value = timed ? $"{seconds / 60:00}:{seconds % 60:00}" : $"DAY {game.CurrentDay:00}";
        Color timerColor = timed && seconds <= 15 ? TacticalDanger : TacticalWarning;
        TacticalTextAt(new Rect(panel.x, panel.y, panel.width, 18f * s), value, tacticalTimer, timerColor);
        TacticalTextAt(new Rect(panel.x, panel.y + 17f * s, panel.width, 12f * s), TacticalStageLabel(),
            tacticalCentered, TacticalText);
    }

    private string TacticalStageLabel() => phase switch {
        Phase.Practice => "PRACTICE",
        Phase.PracticeResult => "RESULT",
        Phase.Discuss => "DISCUSS",
        Phase.Reconsider => "RECONSIDER",
        Phase.FinalPlan => "FINAL PLAN",
        _ => "OUTCOME"
    };

    private void DrawTacticalNotice()
    {
        if (string.IsNullOrEmpty(tacticalNotice)) return;
        float s = TacticalScale;
        float alpha = 1f;
        float age = Time.unscaledTime - tacticalNoticeStarted;
        float left = tacticalNoticeUntil - Time.unscaledTime;
        if (age < .18f) alpha = Mathf.Clamp01(age / .18f);
        else if (left < .25f) alpha = Mathf.Clamp01(left / .25f);
        Rect banner = new(Screen.width * .5f - Mathf.Min(175f * s, Screen.width * .28f), 57f * s,
            Mathf.Min(350f * s, Screen.width * .56f), 21f * s);
        DrawTacticalPanel(banner, new Color(TacticalPanelStrong.r, TacticalPanelStrong.g, TacticalPanelStrong.b,
            TacticalPanelStrong.a * alpha));
        TacticalTextAt(new Rect(banner.x + 7f * s, banner.y, banner.width - 14f * s, banner.height),
            tacticalNotice, tacticalCentered, new Color(TacticalWarning.r, TacticalWarning.g, TacticalWarning.b, alpha));
    }

    private void DrawTacticalIntervention()
    {
        if (phase != Phase.Reconsider || string.IsNullOrWhiteSpace(message)) return;
        Rect panel = TacticalInterventionRect;
        float s = TacticalScale, pad = 7f * s;
        DrawTacticalPanel(panel, TacticalPanelStrong);
        TacticalTextAt(new Rect(panel.x + pad, panel.y + 3f * s, panel.width - 2 * pad, 15f * s),
            string.IsNullOrWhiteSpace(interventionLabel) ? "DECISION SUPPORT" : interventionLabel.ToUpperInvariant(),
            tacticalHeading, TacticalGold);
        DrawTacticalRule(panel.x + pad, panel.y + 19f * s, panel.width - 2 * pad);
        Rect textRect = new(panel.x + pad, panel.y + 23f * s, panel.width - 2 * pad, panel.height - 27f * s);
        GUIStyle wrapped = new(tacticalSmall) { wordWrap = true };
        float contentHeight = wrapped.CalcHeight(new GUIContent(message), textRect.width - 18f * s);
        if (contentHeight <= textRect.height)
            TacticalTextAt(textRect, message, wrapped, TacticalText);
        else
        {
            Rect content = new(0, 0, textRect.width - 18f * s, contentHeight);
            liteNoteScroll = GUI.BeginScrollView(textRect, liteNoteScroll, content);
            TacticalTextAt(content, message, wrapped, TacticalText);
            GUI.EndScrollView();
        }
    }

    private void DrawTacticalMinimap()
    {
        Rect panel = TacticalMinimapRect;
        DrawTacticalPanel(panel, TacticalPanelStrong);
        float s = TacticalScale;
        Rect map = new(panel.x + 5f * s, panel.y + 5f * s, panel.width - 10f * s, panel.height - 10f * s);
        if (!TryTacticalMapBounds(out Camera camera, out float minX, out float maxX, out float minY, out float maxY))
        {
            TacticalTextAt(map, "MAP DATA UNAVAILABLE", tacticalCentered, TacticalDim);
            return;
        }

        Vector2 ToMap(Vector3 world)
        {
            Vector3 local = camera.transform.InverseTransformPoint(world);
            return new Vector2(Mathf.Lerp(map.x, map.xMax, Mathf.InverseLerp(minX, maxX, local.x)),
                Mathf.Lerp(map.yMax, map.y, Mathf.InverseLerp(minY, maxY, local.y)));
        }

        if (Event.current.type == EventType.Repaint)
        {
            foreach (RescueNodeDefinition definition in scenario.CurrentScenario.nodes)
            {
                if (!scenario.TryGetSceneNode(definition.nodeId, out RescueNode node) ||
                    !TryGetTacticalScenePoint(definition.parentNodeId, out Vector3 parent)) continue;
                DrawLiteLine(ToMap(parent), ToMap(node.transform.position), TacticalDivider, Mathf.Max(1f, s));
            }

            RescueNode selected = TacticalSelectedNode();
            PublicView view = scenario.View();
            foreach (PublicNode known in view.Nodes)
            {
                if (!scenario.TryGetSceneNode(known.Id, out RescueNode node)) continue;
                Vector2 p = ToMap(node.transform.position);
                Color color = node == selected ? TacticalGoldBright :
                    known.Discovery == NodeDiscovery.Reached ? TacticalSuccess :
                    known.Discovery == NodeDiscovery.Blocked ? TacticalWarning : TacticalMuted;
                float size = node == selected ? 5f * s : 3f * s;
                FillLite(new Rect(p.x - size * .5f, p.y - size * .5f, size, size), color);
            }

            Rect viewRect;
            if (camera.orthographic)
            {
                float halfY = camera.orthographicSize;
                float halfX = halfY * camera.aspect;
                viewRect = Rect.MinMaxRect(
                    Mathf.Lerp(map.x, map.xMax, Mathf.InverseLerp(minX, maxX, -halfX)),
                    Mathf.Lerp(map.yMax, map.y, Mathf.InverseLerp(minY, maxY, halfY)),
                    Mathf.Lerp(map.x, map.xMax, Mathf.InverseLerp(minX, maxX, halfX)),
                    Mathf.Lerp(map.yMax, map.y, Mathf.InverseLerp(minY, maxY, -halfY)));
            }
            else
            {
                // perspective: the ground footprint of the screen corners, in the minimap's plane
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (Vector2 v in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
                {
                    Ray r = camera.ViewportPointToRay(new Vector3(v.x, v.y, 0f));
                    float t = r.direction.y < -1e-3f ? (5f - r.origin.y) / r.direction.y : 3000f;
                    Vector2 m = ToMap(r.origin + r.direction * Mathf.Min(t, 3000f));
                    x0 = Mathf.Min(x0, m.x); x1 = Mathf.Max(x1, m.x); y0 = Mathf.Min(y0, m.y); y1 = Mathf.Max(y1, m.y);
                }
                viewRect = Rect.MinMaxRect(x0, y0, x1, y1);
            }
            viewRect = ClampRect(viewRect, map);
            DrawTacticalOutline(viewRect, TacticalGold, Mathf.Max(1f, s));
        }

        Event input = Event.current;
        if ((input.type == EventType.MouseDown || input.type == EventType.MouseDrag) && input.button == 0 &&
            map.Contains(input.mousePosition))
        {
            float nx = Mathf.InverseLerp(map.x, map.xMax, input.mousePosition.x);
            float ny = 1f - Mathf.InverseLerp(map.y, map.yMax, input.mousePosition.y);
            Vector3 target = camera.transform.position + camera.transform.right * Mathf.Lerp(minX, maxX, nx) +
                camera.transform.up * Mathf.Lerp(minY, maxY, ny);
            GetMapCamera()?.CenterOnWorldPoint(target);
            input.Use();
        }
    }

    private bool TryGetTacticalScenePoint(string nodeId, out Vector3 point)
    {
        if (scenario.TryGetSceneNode(nodeId, out RescueNode node))
        {
            point = node.transform.position;
            return true;
        }
        if (nodeId == ScenarioSpec.CampId && tacticalCamp == null)
            tacticalCamp = GameObject.Find("N00_Camp")?.transform;
        if (nodeId == ScenarioSpec.CampId && tacticalCamp != null)
        {
            point = tacticalCamp.position;
            return true;
        }
        point = default;
        return false;
    }

    private bool TryTacticalMapBounds(out Camera camera, out float minX, out float maxX, out float minY, out float maxY)
    {
        camera = Camera.main;
        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;
        if (camera == null) return false;
        foreach (RescueNodeDefinition definition in scenario.CurrentScenario.nodes)
        {
            if (!scenario.TryGetSceneNode(definition.nodeId, out RescueNode node)) continue;
            Vector3 p = camera.transform.InverseTransformPoint(node.transform.position);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        // Base camp is part of the network even though it is not a selectable node.
        if (TryGetTacticalScenePoint(ScenarioSpec.CampId, out Vector3 campWorld))
        {
            Vector3 p = camera.transform.InverseTransformPoint(campWorld);
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        if (!float.IsFinite(minX) || Mathf.Approximately(minX, maxX) || Mathf.Approximately(minY, maxY)) return false;
        float padX = (maxX - minX) * .08f, padY = (maxY - minY) * .08f;
        minX -= padX; maxX += padX; minY -= padY; maxY += padY;
        return true;
    }

    private void DrawTacticalCameraStrip()
    {
        Rect strip = TacticalCameraStripRect;
        DrawTacticalPanel(strip, TacticalPanelStrong);
        GraphCameraFraming camera = GetMapCamera();
        float gap = 1f * TacticalScale, width = (strip.width - 4f * gap) / 3f;
        bool available = camera != null;
        if (TacticalButton(new Rect(strip.x + gap, strip.y + gap, width, strip.height - 2 * gap), "−", "Zoom out",
            available && camera.CanZoomOut)) camera.ZoomOut();
        if (TacticalButton(new Rect(strip.x + 2 * gap + width, strip.y + gap, width, strip.height - 2 * gap),
            "FIT", "Fit the complete map", available && !camera.IsDefaultView)) camera.ResetView();
        if (TacticalButton(new Rect(strip.x + 3 * gap + 2 * width, strip.y + gap, width, strip.height - 2 * gap),
            "+", "Zoom in", available && camera.CanZoomIn)) camera.ZoomIn();
    }

    private void DrawTacticalToolbar()
    {
        Rect bar = TacticalToolbarRect;
        float cell = bar.height / 4f;
        RescueNode selected = TacticalSelectedNode();
        // Selection happens on the map; this control only closes a selection.
        if (TacticalButton(new Rect(bar.x, bar.y, bar.width, cell), selected != null ? "CLOSE" : "SELECT",
            selected != null ? "Clear the selected location" : "Click a location on the map to select it",
            selected != null, selected != null))
            ClearTacticalSelection();
        if (TacticalButton(new Rect(bar.x, bar.y + cell, bar.width, cell), "EDIT", "Edit the selected dispatch",
            IsLitePlanning && selected != null, liteDestination != null))
        {
            if (liteDestination == null) OpenLiteDraft(selected);
            else CloseLiteDraft();
        }
        if (TacticalButton(new Rect(bar.x, bar.y + 2 * cell, bar.width, cell), "ROUTE", "Planned route overlay",
            draft.Count > 0, draft.Count > 0 && tacticalRoutesVisible)) tacticalRoutesVisible = !tacticalRoutesVisible;
        if (TacticalButton(new Rect(bar.x, bar.y + 3 * cell, bar.width, cell), "CLEAR", "Clear today's plan",
            IsLitePlanning && draft.Count > 0))
        {
            draft.Clear(); liteRouteSlots.Clear(); CloseLiteDraft(); error = null;
        }
    }

    private RescueNode TacticalSelectedNode() => liteDestination != null ? liteDestination :
        NodeSelectionManager.Instance?.SelectedNode;

    private void DrawTacticalInspector()
    {
        Rect panel = TacticalInspectorRect;
        DrawTacticalPanel(panel, TacticalPanelStrong);
        float s = TacticalScale, pad = 9f * s;
        RescueNode node = TacticalSelectedNode();
        if (node == null)
        {
            TacticalTextAt(new Rect(panel.x + pad, panel.y + pad, panel.width - 2 * pad, 18f * s),
                IsLiteResult ? "AFTER ACTION REPORT" : "SELECT A LOCATION", tacticalHeading, TacticalGold);
            string detail = IsLiteResult ? game.LastDaySummary : "Click a map node to inspect its authorized field data.";
            GUIStyle wrapped = new(tacticalSmall) { wordWrap = true };
            TacticalTextAt(new Rect(panel.x + pad, panel.y + 28f * s, panel.width - 2 * pad,
                panel.height - 34f * s), detail, wrapped, TacticalMuted);
            return;
        }

        float usable = panel.width - 2 * pad;
        float a = usable * .34f, b = usable * .35f;
        Rect identity = new(panel.x + pad, panel.y + pad, a - pad, panel.height - 2 * pad);
        Rect stats = new(identity.xMax + pad, identity.y, b - pad, identity.height);
        Rect ops = new(stats.xMax + pad, identity.y, panel.xMax - pad - stats.xMax - pad, identity.height);
        DrawTacticalRule(identity.xMax + pad * .5f, identity.y, 1f, TacticalDivider, identity.height);
        DrawTacticalRule(stats.xMax + pad * .5f, identity.y, 1f, TacticalDivider, identity.height);

        TacticalTextAt(new Rect(identity.x, identity.y, identity.width, 18f * s), node.nodeId.ToUpperInvariant(),
            tacticalHeading, TacticalGoldBright);
        TacticalTextAt(new Rect(identity.x, identity.y + 18f * s, identity.width, 15f * s),
            "SEARCH LOCATION", tacticalTiny, TacticalMuted);
        PublicNode known = scenario.GetPublicNode(node.nodeId);
        NodeDiscovery discovery = known?.Discovery ?? NodeDiscovery.Unexplored;
        bool rescuedHere = known != null && known.SurvivorsRescuedHere;
        string status = rescuedHere ? "RESCUE COMPLETE" : discovery == NodeDiscovery.Reached ? "ACCESS SECURED" :
            discovery == NodeDiscovery.Blocked ? "ROUTE BLOCKED" : "UNEXPLORED";
        Color statusColor = rescuedHere || discovery == NodeDiscovery.Reached ? TacticalSuccess :
            discovery == NodeDiscovery.Blocked ? TacticalWarning : TacticalText;
        TacticalTextAt(new Rect(identity.x, identity.y + 39f * s, identity.width, 17f * s), status,
            tacticalSmall, statusColor);
        RescueNodeDefinition definition = scenario.GetDefinition(node.nodeId);
        string note = definition?.playerNote;
        if (!string.IsNullOrWhiteSpace(note))
        {
            GUIStyle wrapped = new(tacticalTiny) { wordWrap = true };
            TacticalTextAt(new Rect(identity.x, identity.y + 57f * s, identity.width,
                identity.height - 57f * s), note, wrapped, TacticalMuted);
        }

        TacticalTextAt(new Rect(stats.x, stats.y, stats.width, 16f * s), "FIELD INTELLIGENCE", tacticalHeading, TacticalMuted);
        DrawTacticalInspectorMetric(stats, 20f, scenario.Spec.CumulativeCosts ? "Crossing cost" : "MINIMUM LP", Known(known?.KnownDifficulty));
        DrawTacticalInspectorMetric(stats, 39f, "SURVIVORS HERE", Known(known?.KnownRemainingHere));
        bool frontier = known != null && !known.KnownRemainingBeyond.HasValue && known.KnownBranchRemaining.HasValue;
        DrawTacticalInspectorMetric(stats, 58f, frontier ? "BRANCH TOTAL" : "SURVIVORS BEYOND",
            Known(known?.KnownRemainingBeyond ?? known?.KnownBranchRemaining));

        ExpeditionAssignment assignment = draft.Find(item => item.targetNodeId == node.nodeId);
        TacticalTextAt(new Rect(ops.x, ops.y, ops.width, 16f * s), "OPERATIONS", tacticalHeading, TacticalMuted);
        DrawTacticalInspectorMetric(ops, 20f, "PLANNED LP", (assignment?.assignedGroups ?? 0).ToString());
        DrawTacticalInspectorMetric(ops, 39f, "EDITING LP", liteDestination != null ? liteDraftPoints.ToString() : "—");
        DrawTacticalInspectorMetric(ops, 58f, "ROUTE DEPTH", node.depth.ToString());
    }

    private static string Known(int? value) => value.HasValue ? value.Value.ToString() : "?";

    private void DrawTacticalInspectorMetric(Rect column, float y, string label, string value)
    {
        float s = TacticalScale;
        Rect row = new(column.x, column.y + y * s, column.width, 16f * s);
        TacticalTextAt(new Rect(row.x, row.y, row.width * .72f, row.height), label, tacticalTiny, TacticalMuted);
        TacticalTextAt(new Rect(row.x + row.width * .70f, row.y, row.width * .30f, row.height), value,
            tacticalValue, value == "?" ? TacticalDim : TacticalText);
        DrawTacticalRule(row.x, row.yMax, row.width, TacticalDivider);
    }

    // Immediate feedback on the draft: legs, points left today, and validation.
    private void DrawTacticalPlanStrip()
    {
        Rect panel = TacticalPlanRect;
        DrawTacticalPanel(panel, TacticalPanelStrong);
        float s = TacticalScale, pad = 6f * s;
        Rect line1 = new(panel.x + pad, panel.y + 3f * s, panel.width - 2 * pad, 18f * s);
        Rect line2 = new(panel.x + pad, panel.y + 22f * s, panel.width - 2 * pad, panel.height - 25f * s);
        GUIStyle wrapped = new(tacticalTiny) { wordWrap = true, alignment = TextAnchor.UpperLeft };

        if (IsLiteResult)
        {
            TacticalTextAt(line1, "DISPATCH RESOLVED", tacticalHeading, TacticalGold);
            TacticalTextAt(line2, "Review the outcome, then continue.", wrapped, TacticalMuted);
            return;
        }

        int budget = Mathf.Min(game.DailyDeploymentCap, game.LogisticsRemaining);
        int used = draft.Sum(leg => leg.assignedGroups);
        TacticalTextAt(line1, $"PLAN  {used}/{budget} LP · {draft.Count}/{game.MaxExpeditionsPerDay} TEAMS",
            tacticalHeading, used > budget ? TacticalDanger : TacticalGold);
        string detail;
        Color color = TacticalMuted;
        if (!string.IsNullOrEmpty(error))
        {
            detail = error;
            color = TacticalDanger;
        }
        else if (liteDestination != null)
        {
            ExpeditionAssignment editing = draft.Find(leg => leg.targetNodeId == liteDestination.nodeId);
            int max = LiteDraftMax(editing);
            detail = max == 0
                ? (draft.Count >= game.MaxExpeditionsPerDay ? "Team limit reached for today." : "No LP left today.")
                : $"{liteDestination.nodeId}: {liteDraftPoints} LP (up to {max}). Press OK to {(editing == null ? "add" : "save")}.";
        }
        else if (draft.Count == 0)
            detail = phase == Phase.FinalPlan && provisional != null
                ? "Provisional: " + Describe(provisional)
                : "No teams planned. Click a location to plan a dispatch.";
        else
            detail = Describe(draft);
        TacticalTextAt(line2, detail, wrapped, color);
    }

    private void DrawTacticalCommandGrid()
    {
        Rect grid = TacticalCommandRect;
        DrawTacticalPanel(grid, TacticalPanelStrong);
        float cellW = grid.width / 3f, cellH = grid.height / 3f;
        RescueNode selected = TacticalSelectedNode();
        ExpeditionAssignment assignment = selected == null ? null : draft.Find(item => item.targetNodeId == selected.nodeId);
        int max = liteDestination == null ? 0 : LiteDraftMax(assignment);
        bool ready = (phase != Phase.Discuss && phase != Phase.Reconsider) || LiteSecondsRemaining == 0;

        Rect Cell(int index) => new(grid.x + index % 3 * cellW, grid.y + index / 3 * cellH, cellW, cellH);
        if (TacticalButton(Cell(0), "OK", "Add or save this dispatch", IsLitePlanning && liteDestination != null &&
            max > 0 && liteDraftPoints >= 1 && liteDraftPoints <= max)) SaveLiteDraft(liteDestination.nodeId, assignment, max);
        if (TacticalButton(Cell(1), "+", "Add one logistics point", IsLitePlanning && liteDestination != null && liteDraftPoints < max))
            liteDraftPoints++;
        if (TacticalButton(Cell(2), "−", "Remove one logistics point", IsLitePlanning && liteDestination != null && liteDraftPoints > 1))
            liteDraftPoints--;
        if (TacticalButton(Cell(3), "CTR", "Center camera on selection", selected != null && Camera.main != null))
            GetMapCamera()?.CenterOnWorldPoint(selected.transform.position);
        if (TacticalButton(Cell(4), "FIT", "Fit the complete map", GetMapCamera() is GraphCameraFraming framing && !framing.IsDefaultView))
            GetMapCamera()?.ResetView();
        if (TacticalButton(Cell(5), "X", "Clear selection", selected != null)) ClearTacticalSelection();
        if (TacticalButton(Cell(6), "DEL", "Remove selected dispatch", IsLitePlanning && assignment != null))
        {
            draft.Remove(assignment); liteRouteSlots.Remove(assignment.targetNodeId); CloseLiteDraft();
        }
        if (TacticalButton(Cell(7), "CLR", "Clear all dispatches", IsLitePlanning && draft.Count > 0))
        {
            draft.Clear(); liteRouteSlots.Clear(); CloseLiteDraft(); error = null;
        }
        string advance = IsLiteResult ? "NEXT" : "GO";
        if (TacticalButton(Cell(8), advance, IsLiteResult ? "Continue" : "Authorize the current plan", ready, false, true))
            AdvanceLiteBoard();
    }

    private void DrawTacticalMenu()
    {
        if (!tacticalMenuOpen) return;
        float s = TacticalScale;
        Rect panel = new(0, TacticalMenuButtonRect.yMax, 178f * s, 78f * s);
        DrawTacticalPanel(panel, TacticalPanelStrong);
        float pad = 5f * s, row = 31f * s;
        if (TacticalButton(new Rect(panel.x + pad, panel.y + pad, panel.width - 2 * pad, row),
            "RESUME", "Close menu")) tacticalMenuOpen = false;
        if (TacticalButton(new Rect(panel.x + pad, panel.y + pad + row + 3f * s, panel.width - 2 * pad, row),
            "RESET CAMERA", "Restore full-map view", GetMapCamera() != null)) GetMapCamera()?.ResetView();
    }

    private void ClearTacticalSelection()
    {
        if (liteDestination != null) CloseLiteDraft();
        else NodeSelectionManager.Instance?.ClearSelection();
    }

    private bool TacticalButton(Rect rect, string label, string tooltip, bool allowed = true, bool active = false,
        bool primary = false)
    {
        bool previous = GUI.enabled;
        GUI.enabled = previous && allowed;
        bool pressed = GUI.Button(rect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
        GUI.enabled = previous;
        if (Event.current.type != EventType.Repaint) return pressed;
        bool hover = rect.Contains(Event.current.mousePosition) && allowed;
        Color fill = active ? new Color(TacticalGold.r, TacticalGold.g, TacticalGold.b, .20f) :
            hover ? new Color(TacticalGold.r, TacticalGold.g, TacticalGold.b, .12f) : TacticalPanel;
        if (primary && allowed) fill = new Color(TacticalGold.r, TacticalGold.g, TacticalGold.b, .23f);
        FillLite(rect, fill);
        DrawTacticalOutline(rect, active || primary ? TacticalGold : TacticalBorder, 1f);
        TacticalTextAt(rect, label, tacticalCentered, !allowed ? TacticalDim : hover ? TacticalGoldBright :
            active || primary ? TacticalGold : TacticalText);
        return pressed;
    }

    private void DrawTacticalTooltip()
    {
        if (string.IsNullOrEmpty(GUI.tooltip) || Event.current.type != EventType.Repaint) return;
        float s = TacticalScale;
        Vector2 size = tacticalTooltip.CalcSize(new GUIContent(GUI.tooltip));
        Rect rect = new(Event.current.mousePosition.x + 12f * s, Event.current.mousePosition.y + 12f * s,
            size.x + 12f * s, Mathf.Max(22f * s, size.y + 4f * s));
        rect.x = Mathf.Min(rect.x, Screen.width - rect.width - 3f * s);
        rect.y = Mathf.Min(rect.y, Screen.height - rect.height - 3f * s);
        DrawTacticalPanel(rect, TacticalPanelStrong);
        TacticalTextAt(rect, GUI.tooltip, tacticalTooltip, TacticalText);
    }

    private static Rect ClampRect(Rect value, Rect bounds)
    {
        float xMin = Mathf.Clamp(value.xMin, bounds.xMin, bounds.xMax);
        float xMax = Mathf.Clamp(value.xMax, bounds.xMin, bounds.xMax);
        float yMin = Mathf.Clamp(value.yMin, bounds.yMin, bounds.yMax);
        float yMax = Mathf.Clamp(value.yMax, bounds.yMin, bounds.yMax);
        return Rect.MinMaxRect(Mathf.Min(xMin, xMax), Mathf.Min(yMin, yMax),
            Mathf.Max(xMin, xMax), Mathf.Max(yMin, yMax));
    }

    private static void DrawTacticalPanel(Rect rect, Color fill)
    {
        if (Event.current.type != EventType.Repaint) return;
        FillLite(rect, fill);
        DrawTacticalOutline(rect, TacticalBorder, 1f);
    }

    private static void DrawTacticalOutline(Rect rect, Color color, float width)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
        FillLite(new Rect(rect.x, rect.y, rect.width, width), color);
        FillLite(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
        FillLite(new Rect(rect.x, rect.y, width, rect.height), color);
        FillLite(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
    }

    private static void DrawTacticalRule(float x, float y, float width, Color? color = null, float height = 1f)
    {
        if (Event.current.type == EventType.Repaint)
            FillLite(new Rect(x, y, width, height), color ?? TacticalDivider);
    }

    private static void TacticalTextAt(Rect rect, string value, GUIStyle style, Color color)
    {
        if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return;
        Color previous = style.normal.textColor;
        style.normal.textColor = color;
        style.Draw(rect, new GUIContent(value ?? string.Empty), false, false, false, false);
        style.normal.textColor = previous;
    }
}
