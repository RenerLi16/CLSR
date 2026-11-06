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

}

[Serializable]
public class ExpeditionAssignment
{
    public string targetNodeId;
    public int assignedGroups;
}