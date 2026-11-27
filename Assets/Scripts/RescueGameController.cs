using System;
using System.Collections.Generic;
using CLSR.Simulation;
using UnityEngine;

/// <summary>
/// Scene-facing wrapper around <see cref="RescueSimulation"/>. It keeps the
/// draft plan for the legacy prototype panel, submits plans to the simulation,
/// mirrors the result to the scene and formats the day summary. It contains no
/// rules of its own: legality and resolution live in the simulation.
/// </summary>
public class RescueGameController : MonoBehaviour
{
    private const string StartingSummary =
        "Select destinations and assign logistics points.";

    [Header("Required Components")]
    [SerializeField]
    private ScenarioController scenarioController;

    [SerializeField]
    private List<ExpeditionAssignment> plannedAssignments =
        new List<ExpeditionAssignment>();

    [SerializeField]
    private List<ExpeditionResult> lastDayResults =
        new List<ExpeditionResult>();

    /// <summary>Raised after every resolved day, before the summary is shown.</summary>
    public event Action<DayOutcome> DayResolved;

    public string LastDaySummary { get; private set; } = StartingSummary;

    public DayOutcome LastOutcome { get; private set; }

    private RescueSimulation Sim
    {
        get
        {
            if (scenarioController == null) scenarioController = GetComponent<ScenarioController>();
            return scenarioController.Simulation;
        }
    }

    public int CurrentDay => Sim.Day;

    public int TotalDays => Sim.Scenario.TotalDays;

    public int TotalLogisticsGranted => Sim.PointsGranted;

    /// <summary>Points still available to plan with (pool minus today's plan).</summary>
    public int LogisticsRemaining => Sim.Pool - PointsPlannedToday;

    public int PendingLogistics => Sim.PendingPoints;

    public int PointsPlannedToday
    {
        get
        {
            int total = 0;

            foreach (ExpeditionAssignment assignment in plannedAssignments)
            {
                total += assignment.assignedGroups;
            }

            return total;
        }
    }

    public int DailyDeploymentCap => Sim.Scenario.DailyCap;

    public int ExpeditionsPlannedToday => plannedAssignments.Count;

    public int MaxExpeditionsPerDay => Sim.Scenario.MaxExpeditions;

    public IReadOnlyList<ExpeditionAssignment> PlannedAssignments => plannedAssignments;

    public int TotalRescued => Sim.TotalRescued;

    /// <summary>
    /// The true scenario total. Hidden information: show it to participants
    /// only once the scenario has ended (use PublicView.FinalTotalSurvivors).
    /// </summary>
    public int TotalSurvivors => Sim.Scenario.TotalSurvivors;

    public bool GameOver => Sim.GameOver;

    private void Start()
    {
        if (scenarioController == null)
        {
            scenarioController = GetComponent<ScenarioController>();
        }

        if (
            scenarioController == null ||
            scenarioController.CurrentScenario == null ||
            !scenarioController.IsReady
        )
        {
            Debug.LogError("RescueGameController requires a valid ScenarioController.");
            enabled = false;
            return;
        }

        ResetState();
    }

    public void AddGroupToSelectedNode()
    {
        RescueNode selectedNode = GetSelectedNodeForPlanning();

        if (selectedNode == null)
        {
            return;
        }

        if (LogisticsRemaining <= 0)
        {
            Debug.LogWarning("No logistics points remain.");
            return;
        }

        if (PointsPlannedToday >= DailyDeploymentCap)
        {
            Debug.LogWarning(
                "Daily cap reached: at most " + DailyDeploymentCap +
                " logistics points can be deployed per day."
            );
            return;
        }

        ExpeditionAssignment assignment =
            FindAssignment(selectedNode.nodeId);

        if (assignment == null)
        {
            if (plannedAssignments.Count >= MaxExpeditionsPerDay)
            {
                Debug.LogWarning(
                    "At most " + MaxExpeditionsPerDay +
                    " expeditions can be launched per day."
                );
                return;
            }

            assignment = new ExpeditionAssignment
            {
                targetNodeId = selectedNode.nodeId,
                assignedGroups = 0
            };
            plannedAssignments.Add(assignment);
        }

        assignment.assignedGroups++;
        PrintCurrentState();
    }

    public void RemoveGroupFromSelectedNode()
    {
        RescueNode selectedNode = GetSelectedNodeForPlanning();

        if (selectedNode == null)
        {
            return;
        }

        ExpeditionAssignment assignment =
            FindAssignment(selectedNode.nodeId);

        if (assignment == null || assignment.assignedGroups <= 0)
        {
            Debug.LogWarning(
                "No logistics points are assigned to " + selectedNode.nodeId + "."
            );
            return;
        }

        assignment.assignedGroups--;

        if (assignment.assignedGroups == 0)
        {
            plannedAssignments.Remove(assignment);
        }

        PrintCurrentState();
    }

    public void ClearPlan()
    {
        if (GameOver)
        {
            return;
        }

        plannedAssignments.Clear();
        PrintCurrentState();
    }

    public static List<Leg> ToLegs(IEnumerable<ExpeditionAssignment> plan)
    {
        var legs = new List<Leg>();
        if (plan == null) return legs;
        foreach (ExpeditionAssignment trip in plan)
            legs.Add(new Leg(trip.targetNodeId, trip.assignedGroups));
        return legs;
    }

    /// <summary>Checks a plan against the simulation's rules without changing anything.</summary>
    public PlanError ValidatePlan(IEnumerable<ExpeditionAssignment> plan) => Sim.Validate(ToLegs(plan));

    // The study screens draft plans without touching the simulation. Only the
    // final team plan reaches the engine, after all pre-outcome forms are closed.
    public bool TrySetPlan(IReadOnlyList<ExpeditionAssignment> plan)
    {
        if (ValidatePlan(plan) != PlanError.None)
            return false;

        plannedAssignments.Clear();
        foreach (var trip in plan)
            plannedAssignments.Add(new ExpeditionAssignment
            {
                targetNodeId = trip.targetNodeId,
                assignedGroups = trip.assignedGroups
            });
        return true;
    }

    /// <summary>
    /// Launches today's expeditions (an empty plan simply ends the day),
    /// spends the deployed points and advances to the next day.
    /// </summary>
    public void ConfirmPlan()
    {
        if (Sim.HasPendingBranchReports)
            return;

        if (GameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return;
        }

        DayOutcome outcome = Sim.Submit(ToLegs(plannedAssignments));
        if (outcome == null)
        {
            Debug.LogWarning("Plan rejected: " + Sim.Validate(ToLegs(plannedAssignments)));
            return;
        }

        plannedAssignments.Clear();
        LastOutcome = outcome;
        lastDayResults.Clear();
        foreach (ExpeditionOutcome expedition in outcome.Expeditions)
            lastDayResults.Add(ExpeditionResult.From(expedition));

        scenarioController.SyncSceneFromSimulation();
        LastDaySummary = BuildDaySummary(outcome, scenarioController.Spec.CumulativeCosts);
        Debug.Log("Day " + outcome.Day + " result: " + LastDaySummary);

        if (outcome.GameOver)
        {
            LastDaySummary +=
                " Search complete: " + TotalRescued + " of " + TotalSurvivors + " survivors rescued.";
        }
        else
        {
            foreach (string report in outcome.RouteReports)
                LastDaySummary += "\n<b>Intelligence report:</b> " + report;

            if (outcome.PointsArrived > 0)
            {
                LastDaySummary +=
                    "\n+" + outcome.PointsArrived +
                    " logistics " + (outcome.PointsArrived == 1 ? "point" : "points") +
                    " arrived from storage camps.";
            }
        }

        PrintCurrentState();
        DayResolved?.Invoke(outcome);
    }

    public void RestartGame()
    {
        ResetState();
        scenarioController.ResetSceneNodes();

        if (NodeSelectionManager.Instance != null)
        {
            NodeSelectionManager.Instance.ClearSelection();
        }
    }

    public bool IsStorageCampClaimed(string nodeId) => Sim.IsCacheClaimed(nodeId);

    public int GetAssignedGroups(string nodeId)
    {
        ExpeditionAssignment assignment = FindAssignment(nodeId);
        return assignment == null ? 0 : assignment.assignedGroups;
    }

    private void ResetState()
    {
        Sim.Reset();
        plannedAssignments.Clear();
        lastDayResults.Clear();
        LastOutcome = null;
        LastDaySummary = StartingSummary;

        string briefing = scenarioController.CurrentScenario.briefing;

        if (!string.IsNullOrWhiteSpace(briefing))
        {
            LastDaySummary += "\n" + briefing;
        }

        PrintCurrentState();
    }

    private RescueNode GetSelectedNodeForPlanning()
    {
        if (GameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return null;
        }

        if (NodeSelectionManager.Instance == null)
        {
            Debug.LogWarning("Node selection is unavailable.");
            return null;
        }

        RescueNode selectedNode = NodeSelectionManager.Instance.SelectedNode;

        if (selectedNode == null)
        {
            Debug.LogWarning("Select a destination first.");
        }

        return selectedNode;
    }

    private ExpeditionAssignment FindAssignment(string nodeId)
    {
        return plannedAssignments.Find(
            assignment => assignment.targetNodeId == nodeId
        );
    }

    public static string BuildDaySummary(DayOutcome outcome, bool cumulativeCosts = false)
    {
        if (outcome.Expeditions.Count == 0)
        {
            return "No expeditions were launched.";
        }

        List<string> summaries = new List<string>();

        foreach (ExpeditionOutcome result in outcome.Expeditions)
        {
            string team =
                "Team of " + result.Points +
                (result.Points == 1 ? " point" : " points");

            if (!string.IsNullOrEmpty(result.StoppedAt))
            {
                summaries.Add(
                    team + " heading to " +
                    result.Target +
                    " stopped at " +
                    result.StoppedAt +
                    (cumulativeCosts ? "; short by " : "; it needs ") +
                    result.RequiredPoints +
                    " points to pass." +
                    (result.Rescued > 0
                        ? " Rescued " + result.Rescued + " on the way."
                        : "")
                );
            }
            else if (result.Rescued > 0)
            {
                summaries.Add(
                    team + " reached " +
                    result.Target +
                    " and rescued " +
                    result.Rescued +
                    (result.Rescued == 1 ? " survivor." : " survivors.")
                );
            }
            else
            {
                summaries.Add(
                    team + " reached " +
                    result.Target +
                    ". No new survivors found."
                );
            }

            if (result.PointsEarned > 0)
            {
                summaries.Add(
                    "Storage camp secured: +" + result.PointsEarned +
                    " logistics " + (result.PointsEarned == 1 ? "point" : "points") +
                    " tomorrow."
                );
            }
        }

        return string.Join(" ", summaries);
    }

    private void PrintCurrentState()
    {
        Debug.Log(
            "Day " + CurrentDay +
            " | Logistics left: " + LogisticsRemaining +
            " | Planned today: " + PointsPlannedToday + "/" + DailyDeploymentCap +
            " | Expeditions: " + ExpeditionsPlannedToday + "/" + MaxExpeditionsPerDay +
            (PendingLogistics > 0 ? " | Arriving tomorrow: " + PendingLogistics : "")
        );
    }
}

[Serializable]
public class ExpeditionAssignment
{
    public string targetNodeId;

    [Tooltip("Logistics points deployed on this expedition.")]
    public int assignedGroups;
}

[Serializable]
public class ExpeditionResult
{
    public string targetNodeId;
    public int assignedGroups;
    public bool reachedTarget;
    public bool invalidRoute;
    public string stoppedAtNodeId;
    public int requiredGroups;
    public int rescuedSurvivors;
    public int logisticsEarned;
    public List<string> encounteredNodeIds = new List<string>();
    public List<string> reachedNodeIds = new List<string>();
    public List<string> cacheNodeIds = new List<string>();

    public static ExpeditionResult From(ExpeditionOutcome outcome)
    {
        var result = new ExpeditionResult
        {
            targetNodeId = outcome.Target,
            assignedGroups = outcome.Points,
            reachedTarget = outcome.ReachedTarget,
            stoppedAtNodeId = outcome.StoppedAt,
            requiredGroups = outcome.RequiredPoints,
            rescuedSurvivors = outcome.Rescued,
            logisticsEarned = outcome.PointsEarned,
        };
        result.encounteredNodeIds.AddRange(outcome.Encountered);
        result.reachedNodeIds.AddRange(outcome.Reached);
        result.cacheNodeIds.AddRange(outcome.Caches);
        return result;
    }
}
