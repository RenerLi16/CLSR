using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// Shared-screen review only. No account, persistent storage, or service calls.
public sealed partial class ReviewStudyFlow : MonoBehaviour
{
    private enum Phase { Setup, Practice, PracticeResult, Observe, Handoff, Initial, Discuss,
        Intervention, Reconsider, FinalPlan, PreOutcome, Result, PostOutcome, Reasoning,
        ScenarioResult, Complete }

    private const string PracticeScene = "PrototypeMap";
    private static readonly string[] Conditions =
        { "Random", "No AI", "Direct Recommendation", "Constructive Dissent" };
    private static readonly string[] MapScenes = { "CedarValley", "BlackridgeStorm" };
    private static ReviewStudyFlow active;
    private static Session session;

    private sealed class Session
    {
        public int Condition;
        public bool SkipTimers;
        public int ScenarioIndex = -1;
        public string[] Order;
        public readonly List<string> Scores = new();
    }

    public static bool IsActive => active != null;
    public static float PanelWidth => Mathf.Min(550, Screen.width * .47f);
    public static bool CanClickMap => active != null && Input.mousePosition.x > PanelWidth &&
        (active.phase == Phase.Observe || active.IsPlanningPhase ||
         active.phase == Phase.Result || active.phase == Phase.Intervention);

    private RescueGameController game;
    private ScenarioController scenario;
    private Phase phase, form;
    private int player, chosenCondition;
    private bool skipTimers, showDestinations;
    private Vector2 scroll;
    private GUIStyle labelStyle, headingStyle;
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
        active = this;
        DesignerView.TruthVisible = false;
        game = GetComponent<RescueGameController>();
        scenario = GetComponent<ScenarioController>();
        phase = session == null ? Phase.Setup :
            session.ScenarioIndex < 0 ? Phase.Practice : Phase.Observe;
    }

    private void OnGUI()
    {
        if (scenario == null || !scenario.IsReady)
            return;

        labelStyle ??= new GUIStyle(GUI.skin.label) {
            fontSize = 15, wordWrap = true, normal = { textColor = Color.black } };
        headingStyle ??= new GUIStyle(labelStyle) { fontSize = 21, fontStyle = FontStyle.Bold };

        Color previous = GUI.color;
        GUI.color = new Color(.72f, .71f, .55f);
        GUI.DrawTexture(new Rect(0, 0, PanelWidth, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.backgroundColor = new Color(.61f, .54f, .43f);
        GUILayout.BeginArea(new Rect(8, 8, PanelWidth - 16, Screen.height - 16));
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.BeginVertical(GUILayout.Width(PanelWidth - 38));
        GUILayout.Label("CASCADE LAB / REVIEW BETA", headingStyle);
        Label("Demonstration only. Sample messages; no live AI. No responses are saved or sent.");
        if (phase != Phase.Setup && phase != Phase.Complete)
        {
            Label(scenario.CurrentScenario.scenarioName + (session.ScenarioIndex < 0
                ? " / practice" : $" / scenario {session.ScenarioIndex + 1} of 2"));
            if (phase != Phase.Result && phase != Phase.PostOutcome && phase != Phase.Reasoning &&
                phase != Phase.ScenarioResult && phase != Phase.PracticeResult)
                Label($"Simulated day {game.CurrentDay}/{game.TotalDays} | Pool: {game.LogisticsRemaining} LP");
        }
        GUILayout.Space(8);
        DrawPhase();
        if (!string.IsNullOrEmpty(error))
            Label(error);
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUI.backgroundColor = Color.white;
    }

    private void DrawPhase()
    {
        switch (phase)
        {
            case Phase.Setup:
                Heading("Review setup");
                Label("Three players work together to rescue as many people as possible. This walkthrough uses one practice dispatch and two draft scenarios. Game days are rounds, not separate visits.");
                Label("Select a preview condition, or use Random. It stays the same for both scenarios.");
                chosenCondition = GUILayout.SelectionGrid(chosenCondition, Conditions, 1);
                skipTimers = GUILayout.Toggle(skipTimers, "Skip countdowns for reviewer walkthrough");
                if (Button("Start practice"))
                {
                    session = new Session {
                        Condition = chosenCondition == 0 ? UnityEngine.Random.Range(1, 4) : chosenCondition,
                        SkipTimers = skipTimers, Order = (string[])MapScenes.Clone()
                    };
                    if (UnityEngine.Random.Range(0, 2) == 1) Array.Reverse(session.Order);
                    Load(PracticeScene, Phase.Practice);
                }
                break;
            case Phase.Practice:
                Heading("Practice dispatch");
                Label("Pick a destination and assign logistics points (LP). A trip follows one path from base camp. A site’s DIFF is a minimum team size, not an additional toll. Reached sites are searched along the way. A blocked trip reveals the threshold at its stopping point.");
                DrawPlan();
                if (Button("Run practice dispatch") && Deploy()) Go(Phase.PracticeResult);
                break;
            case Phase.PracticeResult:
                Heading("Practice result");
                Label(Summary());
                if (Button("Start the first scenario")) NextScenario();
                break;
            case Phase.Observe:
                Heading("1. Observe the map");
                Label("Spend from a shared scenario pool; unused logistics points carry forward. A storage camp pays once, the next day. Reaching a junction reveals a separate survivor total for each next branch, but not its exact locations or difficulty. DIFF is the minimum points to pass.");
                Label(Summary());
                Label(PublicBranches());
                if (Button("Start private judgments"))
                {
                    initialResponses.Clear();
                    provisional = null;
                    finalPlan = null;
                    BeginForms(Phase.Initial);
                }
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
                if (Button("Finish private judgment"))
                {
                    initialResponses.Add(new InitialResponse(Value("branch"), Value("reason"), CopyPlan()));
                    draft.Clear();
                    if (!NextPlayer()) Timed(Phase.Discuss, 60);
                }
                break;
            case Phase.Discuss:
                Heading("3. Initial discussion");
                Label("Discuss what each option could accomplish. Record one provisional plan. This does not dispatch any expeditions.");
                DrawPlan();
                if (TimedButton("Lock provisional plan"))
                {
                    provisional = CopyPlan();
                    message = BuildSampleMessage();
                    Timed(Phase.Intervention, 15);
                }
                break;
            case Phase.Intervention:
                Heading("4. Decision pause");
                Label(session.Condition == 1 ? "Neutral message" : "Mock AI message (local example, no live model)");
                Label(message);
                if (TimedButton("Continue to further discussion")) Timed(Phase.Reconsider, 45);
                break;
            case Phase.Reconsider:
                Heading("5. Further discussion");
                Label(message);
                Label("Keep or revise the provisional plan. A message never changes the plan or launches a dispatch for you.");
                DrawPlan();
                if (TimedButton("Continue to final plan")) Go(Phase.FinalPlan);
                break;
            case Phase.FinalPlan:
                Heading("6. Final team plan");
                Label("Provisional: " + Describe(provisional));
                DrawPlan();
                if (Button("Lock final plan and open private ratings"))
                {
                    finalPlan = CopyPlan();
                    BeginForms(Phase.PreOutcome);
                }
                break;
            case Phase.PreOutcome:
                Heading($"Player {player + 1}: Before the outcome");
                Label("Final plan: " + Describe(finalPlan));
                DrawPreOutcome();
                if (Button("Finish private ratings") && !NextPlayer())
                {
                    draft.Clear();
                    draft.AddRange(finalPlan);
                    if (Deploy()) Go(Phase.Result);
                    else Go(Phase.FinalPlan, false);
                }
                break;
            case Phase.Result:
                Heading("7. Dispatch outcome");
                Label(Summary());
                Label(DiscoveredFrontier());
                Label($"Total rescued: {game.TotalRescued}/{game.TotalSurvivors}");
                if (Button("Open private post-outcome forms")) BeginForms(Phase.PostOutcome);
                break;
            case Phase.PostOutcome:
                Heading($"Player {player + 1}: After the outcome");
                DrawPostOutcome();
                if (Button("Finish individual evaluation") && !NextPlayer())
                {
                    if (game.GameOver) BeginForms(Phase.Reasoning);
                    else Go(Phase.Observe);
                }
                break;
            case Phase.Reasoning:
                Heading($"Player {player + 1}: Scenario reasoning");
                for (int i = 0; i < Reasoning.Length; i++)
                    Written("reasoning" + i, Reasoning[i], 1000);
                if (Button("Finish scenario reasoning") && !NextPlayer())
                {
                    session.Scores.Add($"{scenario.CurrentScenario.scenarioName}: {game.TotalRescued}/{game.TotalSurvivors}");
                    Go(Phase.ScenarioResult);
                }
                break;
            case Phase.ScenarioResult:
                Heading("Scenario complete");
                Label(session.Scores.Last());
                if (Button(session.ScenarioIndex == 0 ? "Start the second scenario" : "Finish walkthrough"))
                {
                    if (session.ScenarioIndex == 0) NextScenario();
                    else Go(Phase.Complete);
                }
                break;
            case Phase.Complete:
                Heading("Walkthrough complete");
                foreach (string score in session.Scores) Label(score);
                Label("Nothing was uploaded or saved.");
                if (Button("Return to setup"))
                {
                    session = null;
                    Load(MapScenes[0], Phase.Setup);
                }
                break;
        }
    }

    private void DrawPlan()
    {
        int used = draft.Sum(item => item.assignedGroups);
        Label($"Plan: {Describe(draft)}");
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
            Label(PublicNode(id));
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
        Go(Phase.Handoff);
    }

    private bool NextPlayer()
    {
        ResetForm();
        if (++player >= 3) return false;
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
        phase = next;
        scroll = Vector2.zero;
        showDestinations = false;
        openChoice = null;
        if (clearError) error = null;
        GUI.FocusControl(null);
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
        Label($"{remaining} seconds remaining");
        return false;
    }

    private string PublicNode(string id)
    {
        var node = scenario.GetDefinition(id);
        string diff = scenario.IsDifficultyKnown(id) ? node.difficulty.ToString() : "?";
        bool known = scenario.AreSurvivorCountsKnown(id);
        string here = known ? scenario.CountRemainingHere(id).ToString() : "?";
        string beyond = known ? scenario.CountRemainingBeyond(id).ToString() : "?";
        var childReports = scenario.CurrentScenario.nodes
            .Where(n => n.parentNodeId == id)
            .Select(n => scenario.TryGetDiscoveredBranchCount(n.nodeId, out int count)
                ? $"{n.nodeId}: {count}" : null)
            .Where(report => report != null).ToArray();
        string branchText = scenario.TryGetDiscoveredBranchCount(id, out int branchCount)
            ? $"\nRemaining in this branch: {branchCount}" : "";
        if (childReports.Length > 0)
            branchText += "\nRemaining by next branch: " + string.Join(" | ", childReports);
        return $"HERE {here} | BEYOND {beyond} | DIFF {diff}" + branchText +
            (string.IsNullOrEmpty(node.playerNote) ? "" : "\n" + node.playerNote);
    }

    private string DiscoveredFrontier()
    {
        var reports = scenario.CurrentScenario.nodes
            .Where(n => !scenario.AreSurvivorCountsKnown(n.nodeId))
            .Select(n => scenario.TryGetDiscoveredBranchCount(n.nodeId, out int count)
                ? $"{n.nodeId}: {count}" : null)
            .Where(report => report != null).ToArray();
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
                $"{report.branchName}: {report.reportedSurvivors} initially reported. {report.note}"));

        return string.Join("\n", map.nodes.Where(node => node.parentNodeId == "Camp").Select(node =>
            $"{node.nodeId}: {scenario.CountRemainingBranchSurvivors(node.nodeId)} remaining in branch."));
    }

    private string Summary() => game.LastDaySummary.Replace("<b>", "").Replace("</b>", "");
    private void Heading(string text) => GUILayout.Label(text, headingStyle);
    private void Label(string text) => GUILayout.Label(text, labelStyle);
    private static bool Button(string text) => GUILayout.Button(text, GUILayout.MinHeight(28));
}
