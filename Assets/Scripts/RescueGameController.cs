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

}

[Serializable]
public class ExpeditionAssignment
{
    public string targetNodeId;
    public int assignedGroups;
}