using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NewRescueScenario",
    menuName = "Cascade Lab/Rescue Scenario"
)]
public class RescueScenarioData : ScriptableObject
{
    [Header("Scenario Settings")]
    public string scenarioName = "Mountain Search";
    public int totalDays = 4;
    public int rescueGroupsPerDay = 6;

    [Header("Hidden Map Data")]
    public List<RescueNodeDefinition> nodes =
        new List<RescueNodeDefinition>();
}

[Serializable]
public class RescueNodeDefinition
{
    public string nodeId;
    public string parentNodeId;

    [Min(0)]
    public int depth;

    [Tooltip("Number of rescue groups required to pass this location.")]
    [Min(0)]
    public int difficulty;

    [Tooltip("Number of survivors located directly at this node.")]
    [Min(0)]
    public int survivorCount;
}