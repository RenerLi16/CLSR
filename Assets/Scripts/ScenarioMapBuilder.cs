using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a scenario's map at runtime from the layout positions stored in
/// its <see cref="RescueScenarioData"/>: base camp, route nodes, trails
/// (dashed for uncertain continuations), storage camps and branch signs.
/// Called by <see cref="ScenarioController"/> before it looks for nodes.
/// </summary>
public class ScenarioMapBuilder : MonoBehaviour
{
    private const float NodeHeight = 0.12f;
    private const float TrailHeight = 0.08f;
    private const float TrailWidth = 0.15f;
    private const float DashLength = 0.35f;
    private const float DashGap = 0.25f;
    private const float StorageSize = 0.7f;

    [Header("Materials")]
    [SerializeField]
    private Material nodeMaterial;

    [SerializeField]
    private Material campMaterial;

    [SerializeField]
    private Material trailMaterial;

    [SerializeField]
    private Material storageMaterial;

    public void Build(RescueScenarioData scenario)
    {
        Transform root = new GameObject("Graph").transform;
        Transform nodesParent = CreateGroup("Nodes", root);
        Transform trailsParent = CreateGroup("Trails", root);
        Transform structuresParent = CreateGroup("Structures", root);
        Transform signsParent = CreateGroup("Signs", root);

        Dictionary<string, Vector3> positions = new Dictionary<string, Vector3>();

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            positions[definition.nodeId] =
                scenario.LayoutToWorld(definition.layoutPosition, NodeHeight);
        }

        foreach (RescueNodeDefinition definition in scenario.nodes)
        {
            Vector3 position = positions[definition.nodeId];

            if (definition.nodeId == "Camp")
            {
                BuildCamp(position, nodesParent);
                continue;
            }

            BuildNode(definition, position, nodesParent);

            if (positions.TryGetValue(definition.parentNodeId, out Vector3 parentPosition))
            {
                BuildTrail(
                    "Trail_" + definition.parentNodeId + "_" + definition.nodeId,
                    parentPosition,
                    position,
                    definition.uncertainRoute,
                    trailsParent
                );
            }

            if (definition.logisticsReward > 0)
            {
                BuildStorageCamp(scenario, definition, structuresParent);
            }
        }

        foreach (BranchReport branch in scenario.branches)
        {
            BuildBranchSign(scenario, branch, signsParent);
        }
    }

    private static Transform CreateGroup(string groupName, Transform parent)
    {
        Transform group = new GameObject(groupName).transform;
        group.SetParent(parent, false);
        return group;
    }

    private void BuildCamp(Vector3 position, Transform parent)
    {
        GameObject camp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        // CampLabel attaches itself to the object with this name.
        camp.name = "N00_Camp";
        camp.transform.SetParent(parent, false);
        camp.transform.position = position;
        camp.transform.localScale = new Vector3(0.52f, 0.08f, 0.52f);
        Destroy(camp.GetComponent<Collider>());
        SetMaterial(camp, campMaterial);
    }

    private void BuildNode(RescueNodeDefinition definition, Vector3 position, Transform parent)
    {
        GameObject nodeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        nodeObject.name = "Node_" + definition.nodeId;
        nodeObject.transform.SetParent(parent, false);
        nodeObject.transform.position = position;
        nodeObject.transform.localScale = new Vector3(0.4f, 0.08f, 0.4f);
        SetMaterial(nodeObject, nodeMaterial);

        // Adding RescueNode also adds its RescueNodeLabel.
        RescueNode node = nodeObject.AddComponent<RescueNode>();
        node.LoadDefinition(definition);
    }

    private void BuildTrail(string trailName, Vector3 from, Vector3 to, bool dashed, Transform parent)
    {
        from.y = TrailHeight;
        to.y = TrailHeight;

        if (!dashed)
        {
            CreateLine(trailName, from, to, parent);
            return;
        }

        Transform group = CreateGroup(trailName, parent);
        float length = Vector3.Distance(from, to);
        Vector3 direction = (to - from).normalized;
        int index = 0;

        for (float start = 0f; start < length; start += DashLength + DashGap)
        {
            float end = Mathf.Min(start + DashLength, length);
            CreateLine("Dash_" + index++, from + direction * start, from + direction * end, group);
        }
    }

    private void CreateLine(string lineName, Vector3 from, Vector3 to, Transform parent)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(parent, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = trailMaterial;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.widthMultiplier = TrailWidth;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void BuildStorageCamp(
        RescueScenarioData scenario,
        RescueNodeDefinition definition,
        Transform parent
    )
    {
        Vector3 position = scenario.LayoutToWorld(definition.storageLayoutPosition, StorageSize * 0.5f + 0.28f);

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Storage_" + definition.nodeId;
        marker.transform.SetParent(parent, false);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * StorageSize;
        Destroy(marker.GetComponent<Collider>());
        SetMaterial(marker, storageMaterial);

        Camera activeCamera = Camera.main;
        Vector3 up = activeCamera != null ? activeCamera.transform.up : Vector3.up;
        Vector3 tagPosition = position + up * (StorageSize * 0.75f + 0.3f);

        WorldLabel campTag = WorldLabel.Create(
            "StorageTag_" + definition.nodeId,
            tagPosition,
            new Vector2(240f, 60f),
            new Vector2(0.5f, 0.5f),
            36,
            TextAnchor.MiddleCenter,
            true
        );
        campTag.transform.SetParent(parent, true);

        marker.AddComponent<StorageCampMarker>()
            .Initialise(definition.nodeId, definition.logisticsReward, campTag);
    }

    private void BuildBranchSign(RescueScenarioData scenario, BranchReport branch, Transform parent)
    {
        WorldLabel sign = WorldLabel.Create(
            "Sign_" + branch.branchName,
            scenario.LayoutToWorld(branch.signLayoutPosition, 0.12f),
            new Vector2(554f, 150f),
            new Vector2(0f, 0.5f),
            36,
            TextAnchor.MiddleLeft,
            false
        );
        sign.transform.SetParent(parent, true);

        sign.gameObject.AddComponent<BranchSign>()
            .Initialise(branch, GetComponent<ScenarioController>());
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        Renderer targetRenderer = target.GetComponent<Renderer>();

        if (targetRenderer != null && material != null)
        {
            targetRenderer.sharedMaterial = material;
        }
    }
}
