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

    [Header("Briefing")]
    [Tooltip("Shown in the LAST SEARCH box before the first search.")]
    [TextArea(2, 4)]
    public string briefing;

    [Tooltip("Public reports shown above each branch. Reported counts may differ from the truth.")]
    public List<BranchReport> branches = new List<BranchReport>();

    [Tooltip("Fixed reports that change the map at the start of a later day.")]
    public List<IntelligenceReport> intelligenceReports = new List<IntelligenceReport>();

    [Header("Map Layout")]
    [Tooltip("Build nodes, trails, storage camps and branch signs at runtime from the layout positions below, instead of using objects placed in the scene.")]
    public bool buildMapFromLayout;

    [Tooltip("World units per layout unit.")]
    public float layoutScale = 0.03f;

    [Tooltip("Layout position (x right, y down) that maps to the world origin.")]
    public Vector2 layoutOrigin = new Vector2(500f, 555f);

    [Header("Hidden Map Data")]
    public List<RescueNodeDefinition> nodes =
        new List<RescueNodeDefinition>();

    /// <summary>Converts a layout position (x right, y down) to a point on the ground.</summary>
    public Vector3 LayoutToWorld(Vector2 layoutPosition, float height = 0f)
    {
        return new Vector3(
            (layoutPosition.x - layoutOrigin.x) * layoutScale,
            height,
            (layoutOrigin.y - layoutPosition.y) * layoutScale
        );
    }
}

[Serializable]
public class BranchReport
{
    public string branchName;

    [Tooltip("First node of the branch; used to show the true count in Designer truth view.")]
    public string rootNodeId;

    [Tooltip("Survivor count the public report claims for this branch.")]
    [Min(0)]
    public int reportedSurvivors;

    [Tooltip("Second line of the sign, e.g. \"Storage +3 · access estimate 3–4\".")]
    public string note;

    [Tooltip("Left edge of the sign, in layout units.")]
    public Vector2 signLayoutPosition;
}

[Serializable]
public class IntelligenceReport
{
    [Tooltip("The report arrives at the start of the day after this one.")]
    [Min(1)]
    public int afterDay = 2;

    public string nodeId;

    [Min(0)]
    public int newDifficulty;

    [TextArea(2, 4)]
    public string message;
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

    [Header("Layout (used when the map is built from layout)")]
    public Vector2 layoutPosition;

    [Tooltip("Draw the trail from the parent as a dashed, uncertain continuation.")]
    public bool uncertainRoute;

    [Tooltip("Where the storage camp marker stands, if this node has a logistics reward.")]
    public Vector2 storageLayoutPosition;

    [Header("Explanations")]
    [Tooltip("Player view: what players know about this node at the start.")]
    [TextArea(2, 4)]
    public string playerNote;

    [Tooltip("Designer truth: what is actually there.")]
    [TextArea(2, 4)]
    public string truthNote;
}
