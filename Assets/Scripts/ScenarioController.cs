using System.Collections.Generic;
using UnityEngine;

public class ScenarioController : MonoBehaviour
{
    [Header("Scenario")]
    [SerializeField]
    private RescueScenarioData scenario;

    private readonly Dictionary<string, RescueNode> sceneNodes =
        new Dictionary<string, RescueNode>();

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
        Debug.Log(
            "A branch survivors: " + CountBranchSurvivors("A")
        );
        Debug.Log(
            "B branch survivors: " + CountBranchSurvivors("B")
        );

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

    public bool IsNodeInformationKnown(string nodeId)
    {
        RescueNodeDefinition definition = GetDefinition(nodeId);

        if (definition == null || definition.nodeId == "Camp")
        {
            return false;
        }

        if (
            sceneNodes.TryGetValue(nodeId, out RescueNode sceneNode) &&
            sceneNode.explored
        )
        {
            return true;
        }

        if (definition.parentNodeId == "Camp")
        {
            return true;
        }

        return
            sceneNodes.TryGetValue(
                definition.parentNodeId,
                out RescueNode parentNode
            ) &&
            parentNode.explored;
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
        foreach (RescueNode sceneNode in sceneNodes.Values)
        {
            sceneNode.ResetDiscovery();
        }
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

        if (scenario.rescueGroupsPerDay < 1)
        {
            Debug.LogError("Scenario must provide at least one rescue group per day.");
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

            if (definition.difficulty < 0 || definition.survivorCount < 0)
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
