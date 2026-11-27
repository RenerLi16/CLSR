using System;
using System.Collections.Generic;
using System.Linq;
using CLSR.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

// Shared-screen review only, with an optional local mock backend.
public sealed partial class ReviewStudyFlow : MonoBehaviour
{
    private enum Phase { Setup, Practice, PracticeResult, Observe, Handoff, Initial, Discuss,
        WaitForSupport, Intervention, Reconsider, FinalPlan, PreOutcome, Result, PostOutcome, Reasoning,
        ScenarioResult, Complete, BranchReport, TelescopeSelect, TelescopeView }

    private const string PracticeScene = "PrototypeMap";
    private const float MapDragThreshold = 6f;
    private const float PanelButtonHeight = 68f;
    private const float ZoomButtonSize = 64f;
    private const float ResetButtonWidth = 80f;
    private const float ZoomButtonGap = 6f;
    private const float ZoomBarMargin = 14f;
    private const float ZoomBarWidth = 2f * ZoomButtonSize + ResetButtonWidth + 2f * ZoomButtonGap;
    private static readonly string[] Conditions =
        { "Random", "No AI", "Direct Recommendation", "Constructive Dissent" };
    private static readonly string[] MapScenes = { "CedarValley", "BlackridgeStorm" };
    private static ReviewStudyFlow active;
    private static Session session;

    private sealed class Session
    {
        public int Condition;
        public bool SkipTimers;
        public bool UseMockBackend;
        public int ScenarioIndex = -1;
        public string[] Order;
        public readonly List<string> Scores = new();
        public double StartedAt;
        public bool WriteLog;
        public ResearchLog Log;
    }

    public static bool IsActive => active != null;
    public static float PanelWidth => Mathf.Min(880f, Screen.width * .52f);
    public static bool TryGetLiteMapRect(out Rect rect)
    {
        if (active != null && active.UsesLiteBoard)
        {
            rect = active.LiteMapRect;
            return true;
        }
        rect = default;
        return false;
    }
    private static Rect ZoomControlsRect =>
        new Rect(Screen.width - ZoomBarWidth - ZoomBarMargin,
            Screen.height - ZoomButtonSize - ZoomBarMargin, ZoomBarWidth, ZoomButtonSize);

    private bool CanSelectMapNode => phase == Phase.Observe || IsPlanningPhase ||
        phase == Phase.Result || phase == Phase.Intervention || phase == Phase.TelescopeSelect;

    private static bool IsPointerOverMap(Vector2 pointer)
    {
        if (TryGetLiteMapRect(out Rect liteRect))
            return liteRect.Contains(pointer) && (active == null || !active.IsPointerOverTacticalHud(pointer));
        return pointer.x > PanelWidth && pointer.x < Screen.width &&
            pointer.y >= 0f && pointer.y < Screen.height &&
            !ZoomControlsRect.Contains(pointer);
    }

    private RescueGameController game;
    private ScenarioController scenario;
    private GraphCameraFraming mapCamera;
    private RescueNode pressedNode;
    private Vector2 mapPressPosition, lastDragPosition;
    private bool mapPointerHeld, mapDragging;
    private Phase phase, form;
    private int player, chosenCondition;
    private bool skipTimers, showDestinations, useMockBackend, writeLog;
    private string writtenLogPath;
    private Vector2 scroll;
    private GUIStyle labelStyle, headingStyle, statusStyle, noticeStyle;
    private GUIStyle buttonStyle, toggleStyle, textAreaStyle;
    private float phaseEnds;
    private string message, error, openChoice;
    private readonly List<ExpeditionAssignment> draft = new();
    private List<ExpeditionAssignment> provisional, finalPlan;
    private readonly List<InitialResponse> initialResponses = new();
    private readonly Dictionary<string, string> answers = new();

    private bool IsPlanningPhase => phase == Phase.Practice || phase == Phase.Initial ||
        phase == Phase.Discuss || phase == Phase.Reconsider || phase == Phase.FinalPlan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        session = null;
        SceneManager.sceneLoaded -= Attach;
        SceneManager.sceneLoaded += Attach;
    }

    private static void Attach(Scene scene, LoadSceneMode mode)
    {
        var controller = FindAnyObjectByType<RescueGameController>();
        if (controller != null)
            controller.gameObject.AddComponent<ReviewStudyFlow>();
    }

    private void Awake()
    {
        DesignerView.TruthVisible = false;
        game = GetComponent<RescueGameController>();
        scenario = GetComponent<ScenarioController>();
        phase = session == null ? Phase.Setup :
            session.ScenarioIndex < 0 ? Phase.Practice : Phase.Observe;
        if (game != null) game.DayResolved += LogDayResolved;
    }

    private void Start()
    {
        if (session != null && scenario != null && scenario.IsReady)
        {
            LogSceneStart();
            LogEvent("phase", "from", "", "to", phase.ToString(), "day", game.CurrentDay,
                "scenario", ScenarioLabel(scenario));
        }
    }

    private void OnEnable()
    {
        active = this;
        // Wheel zoom only applies over the map itself, never over HUD panels or forms.
        GraphCameraFraming.AllowWheelZoomAt = pointer => active == this && !IsTelescopeScreen && IsPointerOverMap(pointer);
    }

    private void OnDisable()
    {
        AbortSighting();
        if (active != this) return;
        active = null;
        GraphCameraFraming.AllowWheelZoomAt = null;
    }

    private void HandleMapPointer()
    {
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event input = Event.current;
        Vector2 pointer = input.mousePosition;
        if (!mapPointerHeld && GUIUtility.hotControl == control)
            GUIUtility.hotControl = 0;

        switch (input.GetTypeForControl(control))
        {
            case EventType.MouseDown when input.button == 0 && GUIUtility.hotControl == 0 &&
                IsPointerOverMap(pointer):
                GUIUtility.hotControl = control;
                mapPointerHeld = true;
                mapDragging = false;
                mapPressPosition = lastDragPosition = pointer;
                pressedNode = CanSelectMapNode ? GetNodeAt(pointer) : null;
                input.Use();
                break;
            case EventType.MouseDrag when GUIUtility.hotControl == control:
                MoveMapPointer(pointer);
                input.Use();
                break;
            case EventType.MouseUp when input.button == 0 && GUIUtility.hotControl == control:
                MoveMapPointer(pointer);
                if (!mapDragging && CanSelectMapNode && pressedNode != null &&
                    IsPointerOverMap(pointer) && GetNodeAt(pointer) == pressedNode)
                {
                    if (IsLitePlanning) OpenLiteDraft(pressedNode);
                    else NodeSelectionManager.Instance?.SelectNode(pressedNode);
                }
                GUIUtility.hotControl = 0;
                ClearMapGesture();
                input.Use();
                break;
        }
    }

    private void MoveMapPointer(Vector2 pointer)
    {
        mapDragging |= (pointer - mapPressPosition).sqrMagnitude >= MapDragThreshold * MapDragThreshold;
        if (!mapDragging)
            return;

        Vector2 delta = pointer - lastDragPosition;
        GetMapCamera()?.PanByScreenDelta(new Vector2(delta.x, -delta.y));
        lastDragPosition = pointer;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) return;
        ClearMapGesture();
        frostPopupPointer = null;
    }

    private void ClearMapGesture()
    {
        mapPointerHeld = false;
        mapDragging = false;
        pressedNode = null;
    }

    private static RescueNode GetNodeAt(Vector2 pointer)
    {
        Camera camera = Camera.main;
        if (camera == null)
            return null;

        int layers = camera.eventMask & camera.cullingMask & Physics.DefaultRaycastLayers;
        Ray ray = camera.ScreenPointToRay(new Vector2(pointer.x, Screen.height - pointer.y));
        return Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane - camera.nearClipPlane, layers)
            ? hit.collider.GetComponentInParent<RescueNode>() : null;
    }

    private void OnGUI()
    {
        if (scenario == null || !scenario.IsReady)
            return;

        EnsureTextStyles();
        TrackFrostPopupPointer(Event.current);
        // The telescope handoff and sighting own the whole screen: no map input reaches the map.
        if (IsTelescopeHandoff) { DrawTelescopeHandoff(); return; }
        if (phase == Phase.TelescopeView) { DrawTelescopeView(); return; }
        HandleMapPointer();

        if (UsesLiteBoard)
        {
            DrawLiteBoard();
            DrawSightingFade();
            return;
        }

        Color previous = GUI.color;
        GUI.color = new Color(.72f, .71f, .55f);
        GUI.DrawTexture(new Rect(0, 0, PanelWidth, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.backgroundColor = new Color(.61f, .54f, .43f);
        GUILayout.BeginArea(new Rect(8, 8, PanelWidth - 16, Screen.height - 16));
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.BeginVertical(GUILayout.Width(PanelWidth - 38));
        GUILayout.Label("CASCADE LAB / REVIEW BETA", headingStyle);
        GUILayout.Label("Demonstration only. Use fictional answers. No live AI or saved responses.", noticeStyle);
        if (phase != Phase.Setup && phase != Phase.Complete)
        {
            Label(scenario.CurrentScenario.scenarioName + (session.ScenarioIndex < 0
                ? " / practice" : $" / scenario {session.ScenarioIndex + 1} of 2"));
            if (phase != Phase.Result && phase != Phase.PostOutcome && phase != Phase.Reasoning &&
                phase != Phase.ScenarioResult && phase != Phase.PracticeResult)
                Status($"Simulated day {game.CurrentDay}/{game.TotalDays} | Pool: {game.LogisticsRemaining} LP");
        }
        GUILayout.Space(8);
        DrawPhase();
        if (!string.IsNullOrEmpty(error))
            Label(error);
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        DrawMapZoom();
        GUI.backgroundColor = Color.white;
        DrawFrostNodePopup();
        DrawSightingFade();
    }

    private GraphCameraFraming GetMapCamera()
    {
        if (mapCamera == null && Camera.main is Camera camera)
            mapCamera = camera.GetComponent<GraphCameraFraming>();
        return mapCamera;
    }

    private void DrawMapZoom()
    {
        GraphCameraFraming framing = GetMapCamera();
        Rect bar = ZoomControlsRect;
        if (framing == null || bar.xMin <= PanelWidth)
            return;

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && framing.CanZoomOut;
        if (GUI.Button(new Rect(bar.x, bar.y, ZoomButtonSize, bar.height), "−", buttonStyle))
            framing.ZoomOut();

        GUI.enabled = wasEnabled && !framing.IsDefaultView;
        if (GUI.Button(new Rect(bar.x + ZoomButtonSize + ZoomButtonGap, bar.y, ResetButtonWidth, bar.height),
            "1:1", buttonStyle))
            framing.ResetView();

        GUI.enabled = wasEnabled && framing.CanZoomIn;
        if (GUI.Button(new Rect(bar.xMax - ZoomButtonSize, bar.y, ZoomButtonSize, bar.height), "+", buttonStyle))
            framing.ZoomIn();
        GUI.enabled = wasEnabled;
    }

    private void EnsureTextStyles()
    {
        if (labelStyle != null)
            return;

        labelStyle = new GUIStyle(GUI.skin.label) {
            font = StudyFont, fontStyle = FontStyle.Normal, fontSize = 38, wordWrap = true,
            normal = { textColor = Color.black } };
        headingStyle = new GUIStyle(labelStyle) { fontSize = 50 };
        statusStyle = new GUIStyle(labelStyle) { fontSize = 42 };
        noticeStyle = new GUIStyle(labelStyle) { fontSize = 28 };
        buttonStyle = new GUIStyle(GUI.skin.button) {
            font = StudyFont, fontStyle = FontStyle.Normal, fontSize = 36, wordWrap = true,
            fixedHeight = 0, padding = new RectOffset(12, 12, 9, 9)
        };
        buttonStyle.hover.textColor = buttonStyle.normal.textColor;
        buttonStyle.onHover.textColor = buttonStyle.onNormal.textColor;
        toggleStyle = new GUIStyle(GUI.skin.toggle) {
            font = StudyFont, fontStyle = FontStyle.Normal, fontSize = 36, wordWrap = true,
            fixedHeight = 0, normal = { textColor = Color.black },
            onNormal = { textColor = Color.black }, hover = { textColor = Color.black },
            onHover = { textColor = Color.black }, active = { textColor = Color.black },
            onActive = { textColor = Color.black }, focused = { textColor = Color.black },
            onFocused = { textColor = Color.black }
        };
        textAreaStyle = new GUIStyle(GUI.skin.textArea) {
            font = StudyFont, fontStyle = FontStyle.Normal, fontSize = 38, wordWrap = true };
        textAreaStyle.hover.textColor = textAreaStyle.normal.textColor;
    }

    private void DrawPhase()
    {
        switch (phase)
        {
            case Phase.Setup:
                Heading("Review setup");
                Label("Three players work together to rescue as many people as possible. This walkthrough uses one practice dispatch and two draft scenarios. Game days are rounds, not separate visits.");
                Label("Select a preview condition, or use Random. It stays the same for both scenarios.");
                chosenCondition = GUILayout.SelectionGrid(chosenCondition, Conditions, 1, buttonStyle,
                    GUILayout.MinHeight((PanelButtonHeight + 4f) * Conditions.Length));
                skipTimers = GUILayout.Toggle(skipTimers, "Skip countdowns for reviewer walkthrough", toggleStyle,
                    GUILayout.MinHeight(PanelButtonHeight));
                bool localBackendAvailable = Application.isEditor ||
                    (Debug.isDebugBuild && Application.platform != RuntimePlatform.WebGLPlayer);
                if (localBackendAvailable)
                    useMockBackend = GUILayout.Toggle(useMockBackend, "Use local mock backend", toggleStyle,
                        GUILayout.MinHeight(PanelButtonHeight));
                if (LocalLogAvailable)
                    writeLog = GUILayout.Toggle(writeLog, "Write local event log (no answers or names)", toggleStyle,
                        GUILayout.MinHeight(PanelButtonHeight));
                if (Button("Start practice"))
                {
                    session = new Session {
                        Condition = chosenCondition == 0 ? UnityEngine.Random.Range(1, 4) : chosenCondition,
                        SkipTimers = skipTimers, UseMockBackend = localBackendAvailable && useMockBackend,
                        Order = (string[])MapScenes.Clone(),
                        StartedAt = Time.realtimeSinceStartupAsDouble,
                        WriteLog = LocalLogAvailable && writeLog,
                        Log = new ResearchLog("clsr-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                            Guid.NewGuid().ToString("N").Substring(0, 6))
                    };
                    if (UnityEngine.Random.Range(0, 2) == 1) Array.Reverse(session.Order);
                    writtenLogPath = null;
                    LogEvent("session_start", "condition", StudyProtocol.ConditionCode(session.Condition),
                        "condition_source", chosenCondition == 0 ? "random" : "reviewer_selected",
                        "scenario_order", string.Join(",", session.Order), "timers_skipped", session.SkipTimers,
                        "support_source", session.Condition == 1 ? "none" :
                            session.UseMockBackend ? "local_mock_backend" : "offline_sample",
                        "build", Application.version, "platform", Application.platform.ToString());
                    Load(PracticeScene, Phase.Practice);
                }
                break;
            case Phase.Observe:
                Heading("1. Observe the map");
                Label("Spend from a shared scenario pool; unused logistics points carry forward. A storage camp pays once, the next day. Reaching a junction reveals a separate survivor total for each next branch, but not its exact locations or difficulty. " + (scenario.Spec.CumulativeCosts ? "Each site deducts its DIFF from the points that team still carries." : "DIFF is the minimum points to pass."));
                Label(Summary());
                Label(PublicBranches());
                if (Button("Start private judgments")) StartPrivateJudgments();
                break;
            case Phase.TelescopeSelect:
                DrawTelescopeSelect();
                break;
            case Phase.Handoff:
                Heading($"Pass the screen to Player {player + 1}");
                Label("Other players, please look away until this screen is passed back. Use fictional responses for this demo. You may skip any question.");
                if (Button($"Player {player + 1}: Open my form")) Go(form);
                break;
            case Phase.Initial:
                Heading($"2. Player {player + 1}: Private judgment");
                Choice("branch", "Which branch of the trail network do you think your team should prioritize right now?",
                    BranchChoices().Append("Not sure").ToArray());
                Label("What rescue deployment plan would you propose for today?");
                DrawPlan();
                Choice("confidence", "How confident are you that your proposed deployment plan is a good choice?",
                    new[] { "Very low", "Low", "Moderate", "High", "Very high" });
                Choice("reason", "Which consideration most influenced your proposal?", Reasons);
                Written("explanation", "Briefly explain why you chose this deployment plan.", 600);
                if (Button("Finish private judgment")) FinishInitialJudgment();
                break;
            case Phase.WaitForSupport:
                Heading("4. Decision pause");
                Label("Waiting for the local mock backend…");
                break;
            case Phase.Intervention:
                Heading("4. Decision pause");
                Label(interventionLabel);
                Label(message);
                if (TimedButton("Continue to further discussion")) ContinueAfterIntervention();
                break;
            case Phase.PreOutcome:
                Heading($"Player {player + 1}: Before the outcome");
                Label("Final plan: " + Describe(finalPlan));
                DrawPreOutcome();
                if (Button("Finish private ratings")) FinishPreOutcome();
                break;
            case Phase.PostOutcome:
                Heading($"Player {player + 1}: After the outcome");
                DrawPostOutcome();
                if (Button("Finish individual evaluation")) FinishPostOutcome();
                break;
            case Phase.BranchReport:
                Heading("New intelligence report");
                foreach (string report in scenario.PendingBranchReportMessages())
                    Label(report);
                Label("These are revised estimates. Use the updated board when planning the next dispatch.");
                if (Button("Acknowledge and update the board")) AcknowledgeBranchReport();
                break;
            case Phase.Reasoning:
                Heading($"Player {player + 1}: Scenario reasoning");
                for (int i = 0; i < Reasoning.Length; i++)
                    Written("reasoning" + i, Reasoning[i], 1000);
                if (Button("Finish scenario reasoning")) FinishReasoning();
                break;
            case Phase.ScenarioResult:
                Heading("Scenario complete");
                Status(session.Scores.Last());
                if (Button(session.ScenarioIndex == 0 ? "Start the second scenario" : "Finish walkthrough"))
                {
                    if (session.ScenarioIndex == 0) NextScenario();
                    else Go(Phase.Complete);
                }
                break;
            case Phase.Complete:
                Heading("Walkthrough complete");
                foreach (string score in session.Scores) Status(score);
                if (session.WriteLog && writtenLogPath == null)
                {
                    LogEvent("session_complete");
                    writtenLogPath = WriteEventLog() ?? "";
                }
                Label("No responses were saved. Mock requests, if enabled, stayed on this computer.");
                if (!string.IsNullOrEmpty(writtenLogPath))
                    Label("Event log (phases, timing, plans, outcomes and displayed messages; no answers): " + writtenLogPath);
                if (Button("Return to setup"))
                {
                    session = null;
                    Load(MapScenes[0], Phase.Setup);
                }
                break;
        }
    }

    // ----- Button actions (shared by the panel UI and the development self-test)
    private void StartPrivateJudgments()
    {
        initialResponses.Clear();
        provisional = null;
        finalPlan = null;
        BeginForms(Phase.Initial);
    }

    private void FinishInitialJudgment()
    {
        initialResponses.Add(new InitialResponse(Value("branch"), Value("reason"), CopyPlan()));
        draft.Clear();
        if (!NextPlayer()) Timed(Phase.Discuss, StudyProtocol.InitialDiscussionSeconds);
    }

    private void ContinueAfterIntervention()
    {
        LogInterventionClosed();
        Timed(Phase.Reconsider, StudyProtocol.FurtherDiscussionSeconds);
    }

    private void FinishPreOutcome()
    {
        if (NextPlayer()) return;
        draft.Clear();
        draft.AddRange(finalPlan);
        if (Deploy()) Go(Phase.Result);
        else Go(Phase.FinalPlan, false);
    }

    private void FinishPostOutcome()
    {
        if (NextPlayer()) return;
        if (game.GameOver) BeginForms(Phase.Reasoning);
        else Go(scenario.HasPendingBranchReports ? Phase.BranchReport : Phase.Observe);
    }

    private void AcknowledgeBranchReport()
    {
        LogEvent("branch_report_acknowledged", "day", game.CurrentDay,
            "reports", string.Join(" | ", scenario.PendingBranchReportMessages()));
        scenario.AcknowledgeBranchReports();
        Go(Phase.Observe);
    }

    private void FinishReasoning()
    {
        if (NextPlayer()) return;
        session.Scores.Add($"{scenario.CurrentScenario.scenarioName}: {game.TotalRescued}/{game.TotalSurvivors}");
        LogEvent("scenario_complete", "scenario", ScenarioLabel(scenario),
            "scenario_index", session.ScenarioIndex, "rescued", game.TotalRescued,
            "total_survivors", game.TotalSurvivors);
        Go(Phase.ScenarioResult);
    }

    private void DrawPlan()
    {
        int used = draft.Sum(item => item.assignedGroups);
        Status($"Plan: {Describe(draft)}");
        Label($"{used}/{Mathf.Min(game.DailyDeploymentCap, game.LogisticsRemaining)} points today; " +
              $"{draft.Count}/{game.MaxExpeditionsPerDay} expeditions.");
        var selection = NodeSelectionManager.Instance;
        string id = selection?.SelectedNode?.nodeId;
        Label("Destination: " + (id ?? "Click a node on the map, or choose from list"));
        if (Button(showDestinations ? "Hide destination list" : "Choose from list"))
            showDestinations = !showDestinations;
        if (showDestinations)
        {
            foreach (var node in scenario.CurrentScenario.nodes)
            {
                if (node.nodeId == "Camp" || !Button(node.nodeId)) continue;
                if (scenario.TryGetSceneNode(node.nodeId, out var target) && selection != null &&
                    selection.SelectedNode != target) selection.SelectNode(target);
                showDestinations = false;
                return;
            }
        }
        if (id != null)
        {
            Label(PublicNodeText(id));
            var trip = draft.Find(item => item.targetNodeId == id);
            GUILayout.BeginHorizontal();
            if (Button("- Point") && trip != null && --trip.assignedGroups == 0) draft.Remove(trip);
            if (Button("+ Point"))
            {
                if (used >= Mathf.Min(game.DailyDeploymentCap, game.LogisticsRemaining))
                    error = "No further points can be assigned today.";
                else if (trip == null && draft.Count >= game.MaxExpeditionsPerDay)
                    error = "Expedition limit reached.";
                else
                {
                    error = null;
                    if (trip == null)
                        draft.Add(new ExpeditionAssignment { targetNodeId = id, assignedGroups = 1 });
                    else trip.assignedGroups++;
                }
            }
            GUILayout.EndHorizontal();
        }
        if (Button("Clear plan / send no expeditions")) { draft.Clear(); error = null; }
    }

    private bool Deploy()
    {
        if (!game.TrySetPlan(draft))
        {
            error = "This plan cannot be dispatched. Recheck destinations and available points.";
            return false;
        }
        game.ConfirmPlan();
        draft.Clear();
        liteRouteSlots.Clear();
        return true;
    }

    private static string Describe(List<ExpeditionAssignment> plan) =>
        plan == null || plan.Count == 0 ? "No expeditions" :
        string.Join("; ", plan.Select(r => $"{r.assignedGroups} LP to {r.targetNodeId}"));

    private List<ExpeditionAssignment> CopyPlan() => draft.Select(r => new ExpeditionAssignment
        { targetNodeId = r.targetNodeId, assignedGroups = r.assignedGroups }).ToList();

    private void Load(string scene, Phase sameScenePhase)
    {
        if (SceneManager.GetActiveScene().name != scene)
            SceneManager.LoadScene(scene);
        else
        {
            game.RestartGame();
            Go(sameScenePhase);
        }
    }

    private void NextScenario()
    {
        session.ScenarioIndex++;
        Load(session.Order[session.ScenarioIndex], Phase.Observe);
    }

    private void BeginForms(Phase target)
    {
        form = target;
        player = 0;
        ResetForm();
        if (UsesTelescope) GetMapCamera()?.ResetView();
        Go(Phase.Handoff);
    }

    private bool NextPlayer()
    {
        LogFormComplete();
        ResetForm();
        if (++player >= 3) return false;
        if (UsesTelescope) GetMapCamera()?.ResetView();
        Go(Phase.Handoff);
        return true;
    }

    private void ResetForm()
    {
        answers.Clear();
        NodeSelectionManager.Instance?.ClearSelection();
    }

    private void Go(Phase next, bool clearError = true)
    {
        CloseLiteDraft();
        LogPhaseChange(phase, next);
        phase = next;
        scroll = Vector2.zero;
        liteNoteScroll = Vector2.zero;
        showDestinations = false;
        openChoice = null;
        if (clearError) error = null;
        if (Event.current != null) GUI.FocusControl(null);
    }

    private void Timed(Phase next, int seconds)
    {
        phaseEnds = Time.unscaledTime + (session.SkipTimers ? 0 : seconds);
        Go(next);
    }

    private bool TimedButton(string text)
    {
        int remaining = Mathf.Max(0, Mathf.CeilToInt(phaseEnds - Time.unscaledTime));
        if (remaining == 0) return Button(text);
        Status($"{remaining} seconds remaining");
        return false;
    }

    private string PublicNodeText(string id)
    {
        var view = scenario.View();
        var node = view.Get(id);
        if (node == null) return "";
        string Show(int? value) => value.HasValue ? value.Value.ToString() : "?";
        var childReports = view.ChildrenOf(id)
            .Where(child => child.KnownBranchRemaining.HasValue)
            .Select(child => $"{child.Id}: {child.KnownBranchRemaining}").ToArray();
        string branchText = node.KnownBranchRemaining.HasValue && node.ParentId != ScenarioSpec.CampId
            ? $"\nRemaining in this branch: {node.KnownBranchRemaining}" : "";
        if (node.KnownRemainingHere.HasValue && childReports.Length > 0)
            branchText += "\nRemaining by next branch: " + string.Join(" | ", childReports);
        string note = scenario.GetDefinition(id)?.playerNote;
        return $"HERE {Show(node.KnownRemainingHere)} | BEYOND {Show(node.KnownRemainingBeyond)} | DIFF {Show(node.KnownDifficulty)}" +
            branchText + (string.IsNullOrEmpty(note) ? "" : "\n" + note);
    }

    private string DiscoveredFrontier()
    {
        var reports = scenario.View().Nodes
            .Where(n => !n.KnownRemainingHere.HasValue && n.KnownBranchRemaining.HasValue &&
                n.ParentId != ScenarioSpec.CampId)
            .Select(n => $"{n.Id}: {n.KnownBranchRemaining}").ToArray();
        return reports.Length == 0 ? "" :
            "Remaining survivors by discovered branch: " + string.Join(" | ", reports);
    }

    private string[] BranchChoices() => scenario.CurrentScenario.nodes
        .Where(n => n.parentNodeId == "Camp")
        .Select(n => scenario.CurrentScenario.branches.Find(r => r.rootNodeId == n.nodeId)?.branchName
            ?? n.nodeId).ToArray();

    private string PublicBranches()
    {
        var map = scenario.CurrentScenario;
        if (map.branches.Count != 0)
            return string.Join("\n", map.branches.Select(report =>
                $"{report.branchName}: {scenario.GetBranchEstimate(report)} " +
                (scenario.HasRevisedBranchEstimate(report) ? "revised estimate. " : "initial estimate. ") + report.note));

        // Maps without reports publish exact first-level totals (the practice map).
        return string.Join("\n", scenario.View().ChildrenOf(ScenarioSpec.CampId)
            .Where(node => node.KnownBranchRemaining.HasValue)
            .Select(node => $"{node.Id}: {node.KnownBranchRemaining} remaining in branch."));
    }

    private string Summary() => game.LastDaySummary.Replace("<b>", "").Replace("</b>", "");
    private void Heading(string text) => GUILayout.Label(text, headingStyle);
    private void Label(string text) => GUILayout.Label(text, labelStyle);
    private void Status(string text) => GUILayout.Label(text, statusStyle);
    private bool Button(string text) => GUILayout.Button(text, buttonStyle, GUILayout.MinHeight(PanelButtonHeight));
}
