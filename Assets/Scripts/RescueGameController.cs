using System;
using System.Collections.Generic;
using UnityEngine;

public class RescueGameController : MonoBehaviour
{
    private const string StartingSummary =
        "Select destinations and assign logistics points.";

    [Header("Required Components")]
    [SerializeField]
    private ScenarioController scenarioController;

    [Header("Current Game State")]
    [SerializeField]
    private int currentDay = 1;

    [Tooltip("Logistics points not yet deployed. Deployed points are spent permanently.")]
    [SerializeField]
    private int logisticsPool;

    [Tooltip("Storage camp points earned today; they arrive at the start of the next day.")]
    [SerializeField]
    private int pendingLogistics;

    [SerializeField]
    private List<ExpeditionAssignment> plannedAssignments =
        new List<ExpeditionAssignment>();

    [SerializeField]
    private List<ExpeditionResult> lastDayResults =
        new List<ExpeditionResult>();

    [SerializeField]
    private int totalRescued;

    [SerializeField]
    private bool gameOver;

    private readonly HashSet<string> rescuedNodeIds =
        new HashSet<string>();

    private readonly HashSet<string> claimedCacheIds =
        new HashSet<string>();

    public string LastDaySummary { get; private set; } = StartingSummary;

    public int CurrentDay
    {
        get { return currentDay; }
    }

    public int TotalDays
    {
        get { return scenarioController.CurrentScenario.totalDays; }
    }

    /// <summary>Points still available to plan with (pool minus today's plan).</summary>
    public int LogisticsRemaining
    {
        get { return logisticsPool - PointsPlannedToday; }
    }

    public int PendingLogistics
    {
        get { return pendingLogistics; }
    }

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

    public int DailyDeploymentCap
    {
        get { return scenarioController.CurrentScenario.dailyDeploymentCap; }
    }

    public int ExpeditionsPlannedToday
    {
        get { return plannedAssignments.Count; }
    }

    public int MaxExpeditionsPerDay
    {
        get { return scenarioController.CurrentScenario.maxExpeditionsPerDay; }
    }

    public IReadOnlyList<ExpeditionAssignment> PlannedAssignments
    {
        get { return plannedAssignments; }
    }

    public int TotalRescued
    {
        get { return totalRescued; }
    }

    public int TotalSurvivors
    {
        get { return scenarioController.CountTotalSurvivors(); }
    }

    public bool GameOver
    {
        get { return gameOver; }
    }

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
        if (gameOver)
        {
            return;
        }

        plannedAssignments.Clear();
        PrintCurrentState();
    }

    // The study screens draft plans without touching the simulation. Only the
    // final team plan reaches the engine, after all pre-outcome forms are closed.
    public bool TrySetPlan(IReadOnlyList<ExpeditionAssignment> plan)
    {
        if (gameOver || plan.Count > MaxExpeditionsPerDay)
            return false;

        int spent = 0;
        var seen = new HashSet<string>();
        foreach (var trip in plan)
        {
            if (trip.assignedGroups <= 0 || trip.assignedGroups > DailyDeploymentCap ||
                !seen.Add(trip.targetNodeId) ||
                scenarioController.GetPathFromCamp(trip.targetNodeId).Count == 0)
                return false;
            spent += trip.assignedGroups;
        }

        if (spent > DailyDeploymentCap || spent > logisticsPool)
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
        if (gameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return;
        }

        lastDayResults.Clear();
        logisticsPool -= PointsPlannedToday;

        foreach (ExpeditionAssignment assignment in plannedAssignments)
        {
            lastDayResults.Add(ResolveAssignment(assignment));
        }

        LastDaySummary = BuildDaySummary();
        Debug.Log("Day " + currentDay + " result: " + LastDaySummary);

        plannedAssignments.Clear();

        if (currentDay >= TotalDays)
        {
            gameOver = true;
            LastDaySummary +=
                " Search complete: " +
                totalRescued +
                " of " +
                TotalSurvivors +
                " survivors rescued.";
            return;
        }

        int finishedDay = currentDay;
        currentDay++;

        foreach (string report in scenarioController.ApplyIntelligenceReports(finishedDay))
        {
            LastDaySummary += "\n<b>Intelligence report:</b> " + report;
        }

        if (pendingLogistics > 0)
        {
            logisticsPool += pendingLogistics;
            LastDaySummary +=
                "\n+" + pendingLogistics +
                " logistics " + (pendingLogistics == 1 ? "point" : "points") +
                " arrived from storage camps.";
            pendingLogistics = 0;
        }

        PrintCurrentState();
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

    public bool IsStorageCampClaimed(string nodeId)
    {
        return claimedCacheIds.Contains(nodeId);
    }

    public int GetAssignedGroups(string nodeId)
    {
        ExpeditionAssignment assignment = FindAssignment(nodeId);
        return assignment == null ? 0 : assignment.assignedGroups;
    }

    private void ResetState()
    {
        currentDay = 1;
        logisticsPool = scenarioController.CurrentScenario.totalLogisticsPoints;
        pendingLogistics = 0;
        plannedAssignments.Clear();
        lastDayResults.Clear();
        rescuedNodeIds.Clear();
        claimedCacheIds.Clear();
        totalRescued = 0;
        gameOver = false;
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
        if (gameOver)
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

    private ExpeditionResult ResolveAssignment(ExpeditionAssignment assignment)
    {
        ExpeditionResult result = new ExpeditionResult
        {
            targetNodeId = assignment.targetNodeId,
            assignedGroups = assignment.assignedGroups
        };

        List<RescueNodeDefinition> path =
            scenarioController.GetPathFromCamp(assignment.targetNodeId);

        if (path.Count == 0)
        {
            result.invalidRoute = true;
            return result;
        }

        foreach (RescueNodeDefinition definition in path)
        {
            bool canPass = assignment.assignedGroups >= definition.difficulty;

            scenarioController.TryGetSceneNode(definition.nodeId, out RescueNode sceneNode);

            if (sceneNode != null)
            {
                sceneNode.RevealEncounter(canPass);
            }

            result.encounteredNodeIds.Add(definition.nodeId);

            if (!canPass)
            {
                result.stoppedAtNodeId = definition.nodeId;
                result.requiredGroups = definition.difficulty;
                return result;
            }

            result.reachedNodeIds.Add(definition.nodeId);

            if (
                definition.survivorCount > 0 &&
                rescuedNodeIds.Add(definition.nodeId)
            )
            {
                result.rescuedSurvivors += definition.survivorCount;
                totalRescued += definition.survivorCount;

                if (sceneNode != null)
                {
                    sceneNode.MarkSurvivorsRescued();
                }
            }

            if (
                definition.logisticsReward > 0 &&
                claimedCacheIds.Add(definition.nodeId)
            )
            {
                result.cacheNodeIds.Add(definition.nodeId);
                result.logisticsEarned += definition.logisticsReward;
                pendingLogistics += definition.logisticsReward;
            }
        }

        result.reachedTarget = true;
        return result;
    }

    private string BuildDaySummary()
    {
        if (lastDayResults.Count == 0)
        {
            return "No expeditions were launched.";
        }

        List<string> summaries = new List<string>();

        foreach (ExpeditionResult result in lastDayResults)
        {
            string team =
                "Team of " + result.assignedGroups +
                (result.assignedGroups == 1 ? " point" : " points");

            if (result.invalidRoute)
            {
                summaries.Add("Route to " + result.targetNodeId + " was invalid.");
            }
            else if (!string.IsNullOrEmpty(result.stoppedAtNodeId))
            {
                summaries.Add(
                    team + " heading to " +
                    result.targetNodeId +
                    " stopped at " +
                    result.stoppedAtNodeId +
                    "; it needs " +
                    result.requiredGroups +
                    " points to pass." +
                    (result.rescuedSurvivors > 0
                        ? " Rescued " + result.rescuedSurvivors + " on the way."
                        : "")
                );
            }
            else if (result.rescuedSurvivors > 0)
            {
                summaries.Add(
                    team + " reached " +
                    result.targetNodeId +
                    " and rescued " +
                    result.rescuedSurvivors +
                    (result.rescuedSurvivors == 1 ? " survivor." : " survivors.")
                );
            }
            else
            {
                summaries.Add(
                    team + " reached " +
                    result.targetNodeId +
                    ". No new survivors found."
                );
            }

            if (result.logisticsEarned > 0)
            {
                summaries.Add(
                    "Storage camp secured: +" + result.logisticsEarned +
                    " logistics " + (result.logisticsEarned == 1 ? "point" : "points") +
                    " tomorrow."
                );
            }
        }

        return string.Join(" ", summaries);
    }

    private void PrintCurrentState()
    {
        Debug.Log(
            "Day " + currentDay +
            " | Logistics left: " + LogisticsRemaining +
            " | Planned today: " + PointsPlannedToday + "/" + DailyDeploymentCap +
            " | Expeditions: " + ExpeditionsPlannedToday + "/" + MaxExpeditionsPerDay +
            (pendingLogistics > 0 ? " | Arriving tomorrow: " + pendingLogistics : "")
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
}
