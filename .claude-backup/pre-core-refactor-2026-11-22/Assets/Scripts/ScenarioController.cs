using System.Collections.Generic;
using UnityEngine;

public class ScenarioController : MonoBehaviour
{
    [Header("Scenario")]
    [SerializeField]
    private RescueScenarioData scenario;

    private readonly Dictionary<string, RescueNode> sceneNodes =
        new Dictionary<string, RescueNode>();
    private readonly List<string> releasedRouteReports = new List<string>();
    private readonly Dictionary<string, int> revisedBranchEstimates = new Dictionary<string, int>();
    private readonly List<IntelligenceReport> pendingBranchReports = new List<IntelligenceReport>();

    public IReadOnlyList<string> ReleasedRouteReports => releasedRouteReports;
    public bool HasPendingBranchReports => pendingBranchReports.Count > 0;
    public bool UsesRevisedEstimates => scenario.intelligenceReports.Exists(report => report.revisedEstimates.Count > 0);

    public bool HasRevisedBranchEstimate(BranchReport branch) =>
        revisedBranchEstimates.ContainsKey(branch.rootNodeId);

    public int GetBranchEstimate(BranchReport branch) =>
        revisedBranchEstimates.TryGetValue(branch.rootNodeId, out int revised)
            ? revised : branch.reportedSurvivors;

    // The scenario asset as authored. During play the controller works on a
    // copy, so intelligence reports never overwrite the asset itself.
    private RescueScenarioData sourceScenario;

    public RescueScenarioData CurrentScenario
    {
        get { return scenario; }
    }

    public bool IsReady { get; private set; }

    private void Awake()
    {
        LoadScenario();
    }

    private void LoadScenario()
    {
        IsReady = false;

        if (scenario == null)
        {
            Debug.LogError("No rescue scenario has been assigned.");
            return;
        }

        if (!ValidateScenario())
        {
            Debug.LogError("Scenario validation failed. Gameplay initialization stopped.");
            return;
        }

        sourceScenario = scenario;
        scenario = Instantiate(sourceScenario);
        scenario.name = sourceScenario.name + " (runtime)";

        sceneNodes.Clear();

        RescueNode[] foundNodes = FindObjectsByType<RescueNode>();

        foreach (RescueNode node in foundNodes)
        {
            if (sceneNodes.ContainsKey(node.nodeId))
            {
                Debug.LogError("Duplicate node ID: " + node.nodeId);
                continue;
            }

            sceneNodes.Add(node.nodeId, node);
        }

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            // Camp is not an ordinary selectable rescue node.
            if (definition.nodeId == "Camp")
            {
                continue;
            }

            if (sceneNodes.TryGetValue(
                definition.nodeId,
                out RescueNode sceneNode))
            {
                sceneNode.LoadDefinition(definition);
            }
            else
            {
                Debug.LogWarning(
                    "No scene node found for ID: " + definition.nodeId
                );
            }
        }

        Debug.Log("Loaded scenario: " + scenario.scenarioName);

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            if (definition.parentNodeId == "Camp")
            {
                Debug.Log(
                    definition.nodeId +
                    " branch survivors: " +
                    CountBranchSurvivors(definition.nodeId)
                );
            }
        }

        IsReady = true;
    }

    public int CountBranchSurvivors(string rootNodeId)
    {
        return CountNodeAndChildren(rootNodeId);
    }

    public int CountRemainingBranchSurvivors(string rootNodeId)
    {
        RescueNodeDefinition definition = GetDefinition(rootNodeId);

        if (definition == null)
        {
            return 0;
        }

        int remainingAtNode = definition.survivorCount;

        if (
            sceneNodes.TryGetValue(rootNodeId, out RescueNode sceneNode) &&
            sceneNode.survivorsRescued
        )
        {
            remainingAtNode = 0;
        }

        foreach (RescueNodeDefinition possibleChild in scenario.nodes)
        {
            if (possibleChild.parentNodeId == rootNodeId)
            {
                remainingAtNode +=
                    CountRemainingBranchSurvivors(possibleChild.nodeId);
            }
        }

        return remainingAtNode;
    }

    // ----- What the player is allowed to know -----
    //
    // DIFF  is revealed once any expedition reaches the node, whether it got
    //       past or was blocked there. A blocked team reveals nothing else.
    // HERE and BEYOND are revealed only once a team has passed the node.
    // Passing a junction also reveals a separate aggregate for each immediate
    // child branch; it does not reveal an unreached child's HERE or DIFF.

    public bool IsDifficultyKnown(string nodeId)
    {
        return
            sceneNodes.TryGetValue(nodeId, out RescueNode sceneNode) &&
            sceneNode.explored;
    }

    public bool AreSurvivorCountsKnown(string nodeId)
    {
        return
            sceneNodes.TryGetValue(nodeId, out RescueNode sceneNode) &&
            sceneNode.reached;
    }

    public bool TryGetDiscoveredBranchCount(string nodeId, out int remaining)
    {
        remaining = 0;
        RescueNodeDefinition definition = GetDefinition(nodeId);
        if (definition == null || string.IsNullOrEmpty(definition.parentNodeId) ||
            !AreSurvivorCountsKnown(definition.parentNodeId))
            return false;

        remaining = CountRemainingBranchSurvivors(nodeId);
        return true;
    }

    /// <summary>Survivors still waiting at this node itself.</summary>
    public int CountRemainingHere(string nodeId)
    {
        RescueNodeDefinition definition = GetDefinition(nodeId);

        if (definition == null)
        {
            return 0;
        }

        if (
            sceneNodes.TryGetValue(nodeId, out RescueNode sceneNode) &&
            sceneNode.survivorsRescued
        )
        {
            return 0;
        }

        return definition.survivorCount;
    }

    /// <summary>Survivors still waiting further along the branch past this node.</summary>
    public int CountRemainingBeyond(string nodeId)
    {
        int total = 0;

        foreach (RescueNodeDefinition possibleChild in scenario.nodes)
        {
            if (possibleChild.parentNodeId == nodeId)
            {
                total += CountRemainingBranchSurvivors(possibleChild.nodeId);
            }
        }

        return total;
    }

    public int CountTotalSurvivors()
    {
        int total = 0;

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            total += definition.survivorCount;
        }

        return total;
    }

    public RescueNodeDefinition GetDefinition(string nodeId)
    {
        return scenario.nodes.Find(node => node.nodeId == nodeId);
    }

    public bool TryGetSceneNode(string nodeId, out RescueNode sceneNode)
    {
        return sceneNodes.TryGetValue(nodeId, out sceneNode);
    }

    public List<RescueNodeDefinition> GetPathFromCamp(string targetNodeId)
    {
        List<RescueNodeDefinition> reversePath =
            new List<RescueNodeDefinition>();
        HashSet<string> visited = new HashSet<string>();

        RescueNodeDefinition current = GetDefinition(targetNodeId);

        while (current != null && current.nodeId != "Camp")
        {
            if (!visited.Add(current.nodeId))
            {
                Debug.LogError("Scenario contains a parent cycle at " + current.nodeId + ".");
                return new List<RescueNodeDefinition>();
            }

            reversePath.Add(current);
            current = GetDefinition(current.parentNodeId);
        }

        if (current == null)
        {
            Debug.LogError("Node " + targetNodeId + " does not connect back to Camp.");
            return new List<RescueNodeDefinition>();
        }

        reversePath.Reverse();
        return reversePath;
    }

    public void ResetSceneNodes()
    {
        releasedRouteReports.Clear();
        revisedBranchEstimates.Clear();
        pendingBranchReports.Clear();
        // Undo any intelligence-report changes.
        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            RescueNodeDefinition original =
                sourceScenario.nodes.Find(node => node.nodeId == definition.nodeId);

            if (original != null)
            {
                definition.difficulty = original.difficulty;
            }

            if (sceneNodes.TryGetValue(definition.nodeId, out RescueNode sceneNode))
            {
                sceneNode.difficulty = definition.difficulty;
            }
        }

        foreach (RescueNode sceneNode in sceneNodes.Values)
        {
            sceneNode.ResetDiscovery();
        }
    }

    /// <summary>
    /// Applies the reports scheduled after <paramref name="finishedDay"/> and
    /// returns their messages for the day summary.
    /// </summary>
    public List<string> ApplyIntelligenceReports(int finishedDay)
    {
        List<string> messages = new List<string>();

        foreach (IntelligenceReport report in scenario.intelligenceReports)
        {
            if (report.afterDay != finishedDay)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(report.nodeId))
            {
                RescueNodeDefinition definition = GetDefinition(report.nodeId);
                if (definition == null)
                {
                    Debug.LogWarning("Intelligence report names unknown node " + report.nodeId + ".");
                    continue;
                }

                definition.difficulty = report.newDifficulty;
                if (sceneNodes.TryGetValue(report.nodeId, out RescueNode sceneNode))
                    sceneNode.difficulty = report.newDifficulty;
            }

            if (report.revisedEstimates.Count > 0)
            {
                pendingBranchReports.Add(report);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(report.message))
            {
                messages.Add(report.message);
                releasedRouteReports.Add(report.message);
            }
        }

        return messages;
    }

    public IEnumerable<string> PendingBranchReportMessages()
    {
        foreach (IntelligenceReport report in pendingBranchReports)
        {
            var changes = new List<string>();
            foreach (BranchEstimateRevision revision in report.revisedEstimates)
            {
                BranchReport branch = scenario.branches.Find(item => item.rootNodeId == revision.rootNodeId);
                if (branch != null)
                    changes.Add($"{branch.branchName}: {GetBranchEstimate(branch)} → {revision.reportedSurvivors}");
            }
            yield return ((report.message ?? "") + " " + string.Join(" · ", changes)).Trim();
        }
    }

    public void AcknowledgeBranchReports()
    {
        releasedRouteReports.AddRange(PendingBranchReportMessages());
        foreach (IntelligenceReport report in pendingBranchReports)
        foreach (BranchEstimateRevision revision in report.revisedEstimates)
            revisedBranchEstimates[revision.rootNodeId] = revision.reportedSurvivors;
        pendingBranchReports.Clear();
    }

    private bool ValidateScenario()
    {
        bool valid = true;
        Dictionary<string, RescueNodeDefinition> definitions =
            new Dictionary<string, RescueNodeDefinition>();

        if (scenario.totalDays < 1)
        {
            Debug.LogError("Scenario must contain at least one day.");
            valid = false;
        }

        if (scenario.dailyDeploymentCap < 1 || scenario.maxExpeditionsPerDay < 1)
        {
            Debug.LogError("Scenario must allow at least one logistics point and one expedition per day.");
            valid = false;
        }

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            if (string.IsNullOrWhiteSpace(definition.nodeId))
            {
                Debug.LogError("Scenario contains a node with no ID.");
                valid = false;
                continue;
            }

            if (!definitions.TryAdd(definition.nodeId, definition))
            {
                Debug.LogError("Duplicate scenario node ID: " + definition.nodeId);
                valid = false;
            }

            if (
                definition.difficulty < 0 ||
                definition.survivorCount < 0 ||
                definition.logisticsReward < 0
            )
            {
                Debug.LogError("Node " + definition.nodeId + " contains a negative value.");
                valid = false;
            }
        }

        if (!definitions.ContainsKey("Camp"))
        {
            Debug.LogError("Scenario must contain the Camp node.");
            return false;
        }

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            if (definition.nodeId == "Camp")
            {
                continue;
            }

            if (!definitions.TryGetValue(definition.parentNodeId, out RescueNodeDefinition parent))
            {
                Debug.LogError(
                    "Node " + definition.nodeId +
                    " has missing parent " + definition.parentNodeId + "."
                );
                valid = false;
                continue;
            }

            if (definition.depth != parent.depth + 1)
            {
                Debug.LogError("Node " + definition.nodeId + " has an invalid depth.");
                valid = false;
            }

            if (definition.difficulty < parent.difficulty)
            {
                Debug.LogError(
                    "Difficulty decreases from " +
                    parent.nodeId +
                    " to " +
                    definition.nodeId + "."
                );
                valid = false;
            }

            HashSet<string> visited = new HashSet<string>();
            RescueNodeDefinition current = definition;

            while (current != null && current.nodeId != "Camp")
            {
                if (!visited.Add(current.nodeId))
                {
                    Debug.LogError("Scenario contains a parent cycle at " + current.nodeId + ".");
                    valid = false;
                    break;
                }

                definitions.TryGetValue(current.parentNodeId, out current);
            }
        }

        return valid;
    }

    private int CountNodeAndChildren(string nodeId)
    {
        RescueNodeDefinition definition =
            scenario.nodes.Find(node => node.nodeId == nodeId);

        if (definition == null)
        {
            return 0;
        }

        int total = definition.survivorCount;

        foreach (RescueNodeDefinition possibleChild in scenario.nodes)
        {
            if (possibleChild.parentNodeId == nodeId)
            {
                total += CountNodeAndChildren(possibleChild.nodeId);
            }
        }

        return total;
    }
}
