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

    private void Start()
    {
        LoadScenario();
    }

    private void LoadScenario()
    {
        if (scenario == null)
        {
            Debug.LogError("No rescue scenario has been assigned.");
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
    }

    public int CountBranchSurvivors(string rootNodeId)
    {
        return CountNodeAndChildren(rootNodeId);
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
