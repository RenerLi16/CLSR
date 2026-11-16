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
    public int totalDays = 3;

    [Header("Logistics")]
    [Tooltip("Logistics points available for the whole scenario. Deployed points are spent permanently.")]
    [Min(0)]
    public int totalLogisticsPoints = 9;

    [Tooltip("Most logistics points that can be deployed in a single day.")]
    [Min(1)]
    public int dailyDeploymentCap = 4;

    [Tooltip("Most expeditions (separate destinations) that can be launched in a single day.")]
    [Min(1)]
    public int maxExpeditionsPerDay = 2;

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

    [Tooltip("Number of logistics points an expedition needs to pass this location.")]
    [Min(0)]
    public int difficulty;

    [Tooltip("Number of survivors located directly at this node.")]
    [Min(0)]
    public int survivorCount;

    [Tooltip("Storage camp reward: logistics points granted the day after a team first passes this node.")]
    [Min(0)]
    public int logisticsReward;
}
