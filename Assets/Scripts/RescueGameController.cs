using System;
using System.Collections.Generic;
using UnityEngine;

public class RescueGameController : MonoBehaviour
{
    [Header("Required Components")]
    [SerializeField]
    private ScenarioController scenarioController;

    [Header("Current Game State")]
    [SerializeField]
    private int currentDay = 1;

    [SerializeField]
    private int groupsRemaining;

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

    public string LastDaySummary { get; private set; } =
        "Select destinations and assign rescue groups.";

    public int CurrentDay
    {
        get { return currentDay; }
    }

    public int GroupsRemaining
    {
        get { return groupsRemaining; }
    }

    public int TotalDays
    {
        get
        {
            return scenarioController.CurrentScenario.totalDays;
        }
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

        currentDay = 1;
        groupsRemaining = scenarioController.CurrentScenario.rescueGroupsPerDay;

        plannedAssignments.Clear();
        lastDayResults.Clear();
        rescuedNodeIds.Clear();
        totalRescued = 0;
        gameOver = false;
        LastDaySummary = "Select destinations and assign rescue groups.";

        PrintCurrentState();
    }

    public void AddGroupToSelectedNode()
    {
        if (gameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return;
        }

        if (NodeSelectionManager.Instance == null)
        {
            Debug.LogWarning("Node selection is unavailable.");
            return;
        }

        RescueNode selectedNode =
            NodeSelectionManager.Instance.SelectedNode;

        if (selectedNode == null)
        {
            Debug.LogWarning("Select a destination first.");
            return;
        }

        if (groupsRemaining <= 0)
        {
            Debug.LogWarning("No rescue groups remain today.");
            return;
        }

        ExpeditionAssignment assignment =
            FindAssignment(selectedNode.nodeId);

        if (assignment == null)
        {
            assignment = new ExpeditionAssignment
            {
                targetNodeId = selectedNode.nodeId,
                assignedGroups = 0
            };

            plannedAssignments.Add(assignment);
        }

        assignment.assignedGroups++;
        groupsRemaining--;

        PrintCurrentState();
    }

    public void RemoveGroupFromSelectedNode()
    {
        if (gameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return;
        }

        if (NodeSelectionManager.Instance == null)
        {
            Debug.LogWarning("Node selection is unavailable.");
            return;
        }

        RescueNode selectedNode =
            NodeSelectionManager.Instance.SelectedNode;

        if (selectedNode == null)
        {
            Debug.LogWarning("Select a destination first.");
            return;
        }

        ExpeditionAssignment assignment =
            FindAssignment(selectedNode.nodeId);

        if (assignment == null || assignment.assignedGroups <= 0)
        {
            Debug.LogWarning(
                "No groups are assigned to " + selectedNode.nodeId + "."
            );
            return;
        }

        assignment.assignedGroups--;
        groupsRemaining++;

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

        groupsRemaining =
            scenarioController.CurrentScenario.rescueGroupsPerDay;

        PrintCurrentState();
    }

    public void ConfirmPlan()
    {
        if (gameOver)
        {
            Debug.LogWarning("The search has already ended.");
            return;
        }

        if (plannedAssignments.Count == 0)
        {
            Debug.LogWarning(
                "Assign at least one rescue group before confirming."
            );
            return;
        }

        lastDayResults.Clear();
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
            groupsRemaining = 0;
            LastDaySummary +=
                " Search complete: " +
                totalRescued +
                " of " +
                TotalSurvivors +
                " survivors rescued.";
            return;
        }

        currentDay++;
        groupsRemaining =
            scenarioController.CurrentScenario.rescueGroupsPerDay;
    }

    public void RestartGame()
    {
        currentDay = 1;
        groupsRemaining =
            scenarioController.CurrentScenario.rescueGroupsPerDay;

        plannedAssignments.Clear();
        lastDayResults.Clear();
        rescuedNodeIds.Clear();
        totalRescued = 0;
        gameOver = false;
        LastDaySummary = "Select destinations and assign rescue groups.";

        scenarioController.ResetSceneNodes();

        if (NodeSelectionManager.Instance != null)
        {
            NodeSelectionManager.Instance.ClearSelection();
        }

        PrintCurrentState();
    }

    public int GetAssignedGroups(string nodeId)
    {
        ExpeditionAssignment assignment = FindAssignment(nodeId);

        if (assignment == null)
        {
            return 0;
        }

        return assignment.assignedGroups;
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

            if (scenarioController.TryGetSceneNode(definition.nodeId, out RescueNode sceneNode))
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
        }

        result.reachedTarget = true;
        return result;
    }

    private string BuildDaySummary()
    {
        List<string> summaries = new List<string>();

        foreach (ExpeditionResult result in lastDayResults)
        {
            if (result.invalidRoute)
            {
                summaries.Add("Route to " + result.targetNodeId + " was invalid.");
            }
            else if (!string.IsNullOrEmpty(result.stoppedAtNodeId))
            {
                summaries.Add(
                    "Team heading to " +
                    result.targetNodeId +
                    " stopped at " +
                    result.stoppedAtNodeId +
                    "; it requires " +
                    result.requiredGroups +
                    " groups."
                );
            }
            else if (result.rescuedSurvivors > 0)
            {
                summaries.Add(
                    "Team reached " +
                    result.targetNodeId +
                    " and rescued " +
                    result.rescuedSurvivors +
                    " survivors."
                );
            }
            else
            {
                summaries.Add(
                    "Team reached " +
                    result.targetNodeId +
                    ". No new survivors found."
                );
            }
        }

        return string.Join(" ", summaries);
    }

    private void PrintCurrentState()
    {
        Debug.Log(
            "Day " + currentDay +
            " | Groups remaining: " + groupsRemaining
        );
    }
}

[Serializable]
public class ExpeditionAssignment
{
    public string targetNodeId;
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
    public List<string> encounteredNodeIds = new List<string>();
    public List<string> reachedNodeIds = new List<string>();
}
