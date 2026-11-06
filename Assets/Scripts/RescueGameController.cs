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

    public int CurrentDay
    {
        get { return currentDay; }
    }

    public int GroupsRemaining
    {
        get { return groupsRemaining; }
    }

    public IReadOnlyList<ExpeditionAssignment> PlannedAssignments
    {
        get { return plannedAssignments; }
    }

    private void Start()
    {
        if (scenarioController == null)
        {
            scenarioController = GetComponent<ScenarioController>();
        }

        if (scenarioController == null || scenarioController.CurrentScenario == null)
        {
            Debug.LogError("RescueGameController requires a valid ScenarioController.");
            enabled = false;
            return;
        }

        currentDay = 1;
        groupsRemaining = scenarioController.CurrentScenario.rescueGroupsPerDay;

        plannedAssignments.Clear();

        PrintCurrentState();
    }

    public void AddGroupToSelectedNode()
    {
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

}

[Serializable]
public class ExpeditionAssignment
{
    public string targetNodeId;
    public int assignedGroups;
}