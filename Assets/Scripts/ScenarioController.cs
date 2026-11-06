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

        RescueNode[] foundNodes =
            FindObjectsByType<RescueNode>(FindObjectsSortMode.None);

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