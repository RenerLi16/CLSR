using System.Collections.Generic;
using CLSR.Simulation;
using UnityEngine;

/// <summary>
/// Loads the authored scenario into the engine-independent
/// <see cref="RescueSimulation"/> and connects it to the scene. The simulation
/// is the single source of truth for rules, hidden values and discovery; the
/// scene nodes only mirror its state for rendering.
/// </summary>
public class ScenarioController : MonoBehaviour
{
    [Header("Scenario")]
    [SerializeField]
    private RescueScenarioData scenario;

    private readonly Dictionary<string, RescueNode> sceneNodes =
        new Dictionary<string, RescueNode>();
    private HashSet<string> publicCaches;

    // The scenario asset as authored. During play the controller works on a
    // copy, so intelligence reports never overwrite the asset itself.
    private RescueScenarioData sourceScenario;

    public RescueScenarioData CurrentScenario
    {
        get { return scenario; }
    }

    public ScenarioSpec Spec { get; private set; }
    public RescueSimulation Simulation { get; private set; }
    public bool IsReady { get; private set; }

    public IReadOnlyList<string> ReleasedRouteReports => Simulation.ReleasedReports;
    public bool HasPendingBranchReports => Simulation.HasPendingBranchReports;

    public bool HasRevisedBranchEstimate(BranchReport branch) => Simulation.HasRevisedEstimate(branch.rootNodeId);

    public int GetBranchEstimate(BranchReport branch)
    {
        BranchReportSpec spec = Spec.FindBranch(branch.rootNodeId);
        return spec != null ? Simulation.BranchEstimate(spec) : branch.reportedSurvivors;
    }

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

        ScenarioSpec spec = ToSpec(scenario);
        List<string> errors = spec.Validate();
        if (errors.Count > 0)
        {
            foreach (string error in errors) Debug.LogError(error);
            Debug.LogError("Scenario validation failed. Gameplay initialization stopped.");
            return;
        }

        sourceScenario = scenario;
        scenario = Instantiate(sourceScenario);
        scenario.name = sourceScenario.name + " (runtime)";
        Spec = spec;
        Simulation = new RescueSimulation(spec);

        sceneNodes.Clear();

        foreach (RescueNode node in FindObjectsByType<RescueNode>())
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
            if (definition.nodeId == ScenarioSpec.CampId)
            {
                continue;
            }

            if (sceneNodes.TryGetValue(definition.nodeId, out RescueNode sceneNode))
            {
                sceneNode.LoadDefinition(definition);
            }
            else
            {
                Debug.LogWarning("No scene node found for ID: " + definition.nodeId);
            }
        }

        Debug.Log("Loaded scenario: " + scenario.scenarioName);
        IsReady = true;
    }

    /// <summary>Converts the authored asset to the engine-independent specification.</summary>
    public static ScenarioSpec ToSpec(RescueScenarioData data)
    {
        var nodes = new List<NodeSpec>();
        foreach (RescueNodeDefinition node in data.nodes)
            nodes.Add(new NodeSpec(node.nodeId, node.parentNodeId, node.depth, node.difficulty,
                node.survivorCount, node.logisticsReward));

        var branches = new List<BranchReportSpec>();
        foreach (BranchReport branch in data.branches)
            branches.Add(new BranchReportSpec(branch.branchName, branch.rootNodeId, branch.reportedSurvivors, branch.note));

        var reports = new List<IntelReportSpec>();
        foreach (IntelligenceReport report in data.intelligenceReports)
        {
            var revisions = new List<EstimateRevisionSpec>();
            foreach (BranchEstimateRevision revision in report.revisedEstimates)
                revisions.Add(new EstimateRevisionSpec(revision.rootNodeId, revision.reportedSurvivors));
            reports.Add(new IntelReportSpec(report.afterDay, report.nodeId, report.newDifficulty, revisions, report.message));
        }

        return new ScenarioSpec(data.scenarioName, data.totalDays, data.totalLogisticsPoints,
            data.dailyDeploymentCap, data.maxExpeditionsPerDay, data.briefing, nodes, branches, reports, data.cumulativeCosts);
    }

    // ----- What the player is allowed to know -----
    //
    // DIFF  is revealed once any expedition reaches the node, whether it got
    //       past or was blocked there. A blocked team reveals nothing else.
    // HERE and BEYOND are revealed only once a team has passed the node.
    // Passing a junction also reveals a separate aggregate for each immediate
    // child branch; it does not reveal an unreached child's HERE or DIFF.
    // All of this is computed by PublicView; UI and AI payloads read View().

    /// <summary>Storage camps announced on this map (their markers are visible).</summary>
    public ICollection<string> PublicCacheIds
    {
        get
        {
            if (publicCaches == null)
            {
                publicCaches = new HashSet<string>();
                foreach (StorageCampMarker marker in FindObjectsByType<StorageCampMarker>(FindObjectsSortMode.None))
                    if (marker.isActiveAndEnabled) publicCaches.Add(marker.NodeId);
            }
            return publicCaches;
        }
    }

    /// <summary>The player-visible state. Everything participant-facing should read this.</summary>
    public PublicView View() => Simulation.View(PublicCacheIds);

    public PublicNode GetPublicNode(string nodeId) => View().Get(nodeId);

    public bool IsDifficultyKnown(string nodeId) => Simulation.IsExplored(nodeId);

    public bool AreSurvivorCountsKnown(string nodeId) => Simulation.IsReached(nodeId);

    public bool TryGetDiscoveredBranchCount(string nodeId, out int remaining)
    {
        PublicNode node = GetPublicNode(nodeId);
        remaining = node?.KnownBranchRemaining ?? 0;
        // Keep the original meaning: a count discovered by passing the parent junction.
        return node != null && node.KnownBranchRemaining.HasValue && Simulation.IsReached(node.ParentId);
    }

    // ----- Ground truth: designer view, scoring and legacy prototype only -----

    public int CountBranchSurvivors(string rootNodeId)
    {
        NodeSpec node = Spec.Get(rootNodeId);
        if (node == null) return 0;
        int total = node.Survivors;
        foreach (string child in Spec.ChildrenOf(rootNodeId)) total += CountBranchSurvivors(child);
        return total;
    }

    public int CountRemainingBranchSurvivors(string rootNodeId) => Simulation.RemainingInSubtree(rootNodeId);

    /// <summary>Survivors still waiting at this node itself.</summary>
    public int CountRemainingHere(string nodeId) => Simulation.RemainingHere(nodeId);

    /// <summary>Survivors still waiting further along the branch past this node.</summary>
    public int CountRemainingBeyond(string nodeId) => Simulation.RemainingBeyond(nodeId);

    public int CountTotalSurvivors() => Spec.TotalSurvivors;

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
        var path = new List<RescueNodeDefinition>();
        foreach (NodeSpec node in Spec.PathFromCamp(targetNodeId))
            path.Add(GetDefinition(node.Id));
        return path;
    }

    /// <summary>Copies the simulation's discovery state and current thresholds to the scene.</summary>
    public void SyncSceneFromSimulation()
    {
        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            string id = definition.nodeId;
            int current = Simulation.CurrentDifficulty(id);
            definition.difficulty = current;
            if (!sceneNodes.TryGetValue(id, out RescueNode sceneNode)) continue;

            sceneNode.difficulty = current;
            if (Simulation.IsExplored(id) &&
                (!sceneNode.explored || sceneNode.reached != Simulation.IsReached(id)))
                sceneNode.RevealEncounter(Simulation.IsReached(id));
            if (Simulation.IsRescued(id) && !sceneNode.survivorsRescued)
                sceneNode.MarkSurvivorsRescued();
        }
    }

    /// <summary>Restores scene visuals after the simulation has been reset.</summary>
    public void ResetSceneNodes()
    {
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

    public IEnumerable<string> PendingBranchReportMessages() => Simulation.PendingBranchReportMessages();

    public void AcknowledgeBranchReports() => Simulation.AcknowledgeBranchReports();
}
