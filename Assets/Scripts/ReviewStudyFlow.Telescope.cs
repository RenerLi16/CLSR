using System;
using CLSR.Presentation;
using UnityEngine;

// Cedar Valley telescope observation: one private sighting per player before their initial
// judgment, every day. Scenes without a TelescopeLookout (practice, Blackridge) keep the
// existing flow. Sightings and newly seen crossing costs stay on this screen only: nothing is
// written to the scenario, PublicView or the AI request.
public sealed partial class ReviewStudyFlow
{
    private TelescopeLookout lookout;
    private bool lookoutSearched;
    private TelescopeSighting sighting;
    private bool dialHeld;
    private float dialLastY;
    private Texture2D dialRimShade;

    private TelescopeLookout Lookout
    {
        get
        {
            if (!lookoutSearched) { lookout = TelescopeLookout.Find(); lookoutSearched = true; }
            return lookout;
        }
    }

    /// <summary>Private judgments start with a telescope sighting (Cedar Valley scenario days).</summary>
    private bool UsesTelescope => session != null && session.ScenarioIndex >= 0 && Lookout != null;
    private bool IsTelescopeHandoff => phase == Phase.Handoff && form == Phase.Initial && UsesTelescope;
    private bool IsTelescopeScreen => IsTelescopeHandoff || phase == Phase.TelescopeView;

    // ------------------------------------------------------------------ handoff
    private void DrawTelescopeHandoff()
    {
        EnsureLiteStyles();
        EnsureTelescopeStyles();
        // Neutral, opaque: nothing of the map or a previous turn shows behind it.
        FillLite(new Rect(0, 0, Screen.width, Screen.height), FrostUiTheme.Coal);
        float s = LiteScale;
        float w = Mathf.Min(760 * s, Screen.width - 40), h = 220 * s;
        Rect box = new((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        DrawFrostFrame(box, dispatchFrame, 18 * s);
        LabelLite(new Rect(box.x + 30 * s, box.y + 26 * s, box.width - 60 * s, 90 * s),
            $"Pass the device to Player {player + 1}. Other players, please look away.", telescopeHeading);
        Rect ready = new(box.center.x - 110 * s, box.yMax - 78 * s, 220 * s, 50 * s);
        if (DispatchInstrumentButton(ready, "Ready", true, primary: true))
            Go(Phase.TelescopeSelect);
    }

    // ------------------------------------------------------------------ site selection
    private void DrawTelescopeSelect()
    {
        Heading($"Player {player + 1}: Telescope");
        Label("Choose one site to watch from the camp lookout. You get one 10-second look and the only control is focus. Your choice and what you see stay private.");
        var selection = NodeSelectionManager.Instance;
        RescueNode node = selection?.SelectedNode;
        string id = node != null && node.nodeId != ScenarioSpec_CampId ? node.nodeId : null;
        Status("Site: " + (id ?? "click a site on the map, or choose from the list"));
        if (Button(showDestinations ? "Hide site list" : "Choose from list"))
            showDestinations = !showDestinations;
        if (showDestinations)
        {
            foreach (var definition in scenario.CurrentScenario.nodes)
            {
                if (definition.nodeId == ScenarioSpec_CampId || Lookout.Site(definition.nodeId) == null) continue;
                if (!Button(definition.nodeId)) continue;
                if (scenario.TryGetSceneNode(definition.nodeId, out var target) && selection != null &&
                    selection.SelectedNode != target) selection.SelectNode(target);
                showDestinations = false;
                return;
            }
        }
        if (id == null) return;
        Label(PublicNodeText(id)); // already-public information only
        TelescopeSite site = Lookout.Site(id);
        if (site == null)
        {
            Label("The telescope cannot see this site from camp.");
            return;
        }
        GUILayout.Space(6);
        if (Button($"Observe this site ({id})")) BeginSighting(site);
    }

    private const string ScenarioSpec_CampId = CLSR.Simulation.ScenarioSpec.CampId;

    private void BeginSighting(TelescopeSite site)
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        // The observation is locked in now. It reads the current simulation state but changes nothing.
        int here = scenario.CountRemainingHere(site.nodeId);
        int cost = scenario.Simulation.CurrentDifficulty(site.nodeId);
        NodeSelectionManager.Instance?.ClearSelection();
        LogEvent("telescope_observation", "day", game.CurrentDay, "player_slot", player + 1, "site", site.nodeId);
        sighting = TelescopeSighting.On(camera);
        sighting.Restored -= OnSightingRestored;
        sighting.Restored += OnSightingRestored;
        sighting.Begin(Lookout, site, here, cost);
        dialHeld = false;
        Go(Phase.TelescopeView);
    }

    private void OnSightingRestored()
    {
        if (phase != Phase.TelescopeView) return;
        dialHeld = false;
        GetMapCamera()?.ResetView();
        Go(Phase.Initial); // straight into this player's private initial judgment
    }

    // ------------------------------------------------------------------ sighting view
    private void DrawTelescopeView()
    {
        EnsureLiteStyles();
        EnsureTelescopeStyles();
        float s = LiteScale, p = LitePad;
        if (sighting == null || !sighting.IsActive)
        {
            FillLite(new Rect(0, 0, Screen.width, Screen.height), FrostUiTheme.Coal);
            return;
        }

        bool viewing = sighting.IsViewing;
        Rect dial = DialRect;
        HandleDialInput(dial);

        // private field information for the observer only
        Rect plate = new(p, p, Mathf.Min(330 * s, Screen.width * .4f), 118 * s);
        DrawFrostFrame(plate, tooltipFrame, 20 * s);
        LabelLite(new Rect(plate.x + 18 * s, plate.y + 10 * s, plate.width - 36 * s, 26 * s),
            $"PLAYER {player + 1} · SITE {sighting.NodeId}", liteCaption);
        if (viewing || sighting.Current == TelescopeSighting.Stage.Settling || sighting.Current == TelescopeSighting.Stage.FadingOut)
        {
            LabelLite(new Rect(plate.x + 18 * s, plate.y + 40 * s, plate.width - 110 * s, 34 * s),
                "Local crossing cost", liteSupplyLabel);
            LabelLite(new Rect(plate.xMax - 90 * s, plate.y + 26 * s, 72 * s, 62 * s),
                sighting.CrossingCost.ToString(), telescopeCost, FrostUiTheme.Amber);
            LabelLite(new Rect(plate.x + 18 * s, plate.y + 82 * s, plate.width - 36 * s, 26 * s),
                "Only you can see this.", liteSmall);
        }
        else
            LabelLite(new Rect(plate.x + 18 * s, plate.y + 44 * s, plate.width - 36 * s, 34 * s),
                "Aiming the telescope…", liteSupplyLabel);

        // the clock starts only once the zoom has settled
        Rect clock = new(Screen.width * .5f - 90 * s, p, 180 * s, 70 * s);
        if (viewing)
        {
            int secs = Mathf.CeilToInt(sighting.SecondsLeft);
            TintLite(new Rect(clock.x, clock.y, clock.width, 50 * s), $"0:{secs:00}", telescopeClock,
                secs <= 3 ? FrostUiTheme.Oxide : FrostUiTheme.Enamel);
            float frac = sighting.SecondsLeft / TelescopeOptics.ViewSeconds;
            FillLite(new Rect(clock.x, clock.y + 56 * s, clock.width, 4 * s), FrostUiTheme.Steel);
            FillLite(new Rect(clock.x, clock.y + 56 * s, clock.width * frac, 4 * s), FrostUiTheme.Enamel);
            LabelLite(new Rect(dial.x - 260 * s, Screen.height - p - 30 * s, 240 * s, 26 * s),
                "Drag the wheel or scroll to focus", liteSmall, FrostUiTheme.Frost);
        }

        DrawFocusDial(dial, viewing);

        if (sighting.FadeAlpha > 0f)
            FillLite(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, sighting.FadeAlpha));
    }

    private Rect DialRect
    {
        get
        {
            float s = LiteScale;
            float h = Mathf.Min(Screen.height * .62f, 560 * s);
            float w = 50 * s;
            return new Rect(Screen.width - LitePad - w - 10 * s, (Screen.height - h) * .5f, w, h);
        }
    }

    private void HandleDialInput(Rect dial)
    {
        Event input = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);
        float radius = dial.height * .5f;
        switch (input.GetTypeForControl(control))
        {
            case EventType.MouseDown when input.button == 0 && dial.Contains(input.mousePosition):
                GUIUtility.hotControl = control;
                dialHeld = true;
                dialLastY = input.mousePosition.y;
                input.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == control:
                float dy = dialLastY - input.mousePosition.y; // IMGUI y grows downward: up is positive
                dialLastY = input.mousePosition.y;
                sighting.TurnFocus(TelescopeOptics.FocusForDrag(dy, radius, sighting.FocusNear, sighting.FocusFar));
                input.Use();
                break;
            case EventType.MouseUp when GUIUtility.hotControl == control:
                GUIUtility.hotControl = 0;
                dialHeld = false;
                input.Use();
                break;
            case EventType.ScrollWheel:
                // scroll up (negative IMGUI delta) turns toward far; deltas are clamped so
                // browsers that report large steps behave like one notch
                float notches = Mathf.Clamp(-input.delta.y, -3f, 3f);
                sighting.TurnFocus(TelescopeOptics.FocusForScroll(notches, sighting.FocusNear, sighting.FocusFar));
                input.Use();
                break;
            case EventType.MouseDown:
                input.Use(); // nothing else on screen takes clicks during a sighting
                break;
        }
    }

    // An edge-on focus wheel: a narrow vertical strip of a ridged rim seen side-on. Ridge
    // spacing and shading follow the cylinder (compressed and darker toward top and bottom),
    // and the ridges travel with the wheel's current (smoothed) angle.
    private void DrawFocusDial(Rect dial, bool usable)
    {
        if (Event.current.type != EventType.Repaint) return;
        float s = LiteScale;
        Rect housing = new(dial.x - 9 * s, dial.y - 30 * s, dial.width + 18 * s, dial.height + 60 * s);
        DrawFrostFrame(housing, dispatchFrame, 12 * s);
        LabelLite(new Rect(housing.x, housing.y + 6 * s, housing.width, 20 * s), "FAR", frostDispatchCaption, FrostUiTheme.Frost);
        LabelLite(new Rect(housing.x, housing.yMax - 26 * s, housing.width, 20 * s), "NEAR", frostDispatchCaption, FrostUiTheme.Frost);

        if (dialRimShade == null)
            dialRimShade = LiteMask(4, 128, (x, y) =>
                (byte)Mathf.RoundToInt(255f * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (y + .5f) / 128f)))));
        // rim body: dark metal, brighter where it faces the viewer
        FillLite(dial, new Color(.06f, .07f, .08f, 1f));
        TintLiteTexture(dial, dialRimShade, new Color(.30f, .33f, .36f, 1f));

        const int ridges = 60;
        float radius = dial.height * .5f, cy = dial.center.y;
        float angle = sighting.WheelAngle;
        float arc = 2f * Mathf.PI / ridges;
        for (int k = 0; k < ridges; k++)
        {
            float theta = Mathf.Repeat(k * arc + angle + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            float c = Mathf.Cos(theta);
            if (c <= .04f) continue; // back of the wheel
            float y = cy - radius * Mathf.Sin(theta);
            float thick = Mathf.Max(1f, radius * arc * .42f * c);
            float lit = .35f + .65f * c;
            Color ridge = usable ? new Color(.78f * lit, .82f * lit, .85f * lit, 1f) : new Color(.5f * lit, .53f * lit, .56f * lit, 1f);
            FillLite(new Rect(dial.x + 2 * s, y - thick * .5f, dial.width - 4 * s, thick), ridge);
            FillLite(new Rect(dial.x + 2 * s, y + thick * .5f, dial.width - 4 * s, Mathf.Max(1f, thick * .35f)),
                new Color(0f, 0f, 0f, .45f * c));
        }
        // fixed index notch on the housing
        FillLite(new Rect(dial.x - 7 * s, cy - 1.5f * s, 6 * s, 3 * s), FrostUiTheme.Amber);
        // travel gauge beside the rim: where the focus sits between near and far
        float frac = Mathf.InverseLerp(sighting.FocusNear, sighting.FocusFar, sighting.FocusCurrent);
        FillLite(new Rect(dial.xMax + 3 * s, dial.y, 2 * s, dial.height), FrostUiTheme.Steel);
        FillLite(new Rect(dial.xMax + 1 * s, dial.yMax - frac * dial.height - 2 * s, 6 * s, 4 * s), FrostUiTheme.Enamel);
    }

    private GUIStyle telescopeHeading, telescopeCost, telescopeClock;

    private void EnsureTelescopeStyles()
    {
        if (telescopeHeading != null && Mathf.Approximately(telescopeStyleScale, LiteScale)) return;
        telescopeStyleScale = LiteScale;
        telescopeHeading = new GUIStyle(liteText) { fontSize = Mathf.RoundToInt(30f * LiteScale), alignment = TextAnchor.MiddleCenter };
        telescopeCost = new GUIStyle(liteNumber) { fontSize = Mathf.RoundToInt(56f * LiteScale), alignment = TextAnchor.MiddleRight };
        telescopeClock = new GUIStyle(liteTimer) { alignment = TextAnchor.MiddleCenter };
    }

    private float telescopeStyleScale;

    /// <summary>The short fade back from the telescope, over whatever screen follows it.</summary>
    private void DrawSightingFade()
    {
        if (sighting == null || sighting.FadeAlpha <= 0f || Event.current.type != EventType.Repaint) return;
        FillLite(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, sighting.FadeAlpha));
    }

    private void AbortSighting()
    {
        if (sighting != null && sighting.IsActive) sighting.Abort();
        dialHeld = false;
    }
}
