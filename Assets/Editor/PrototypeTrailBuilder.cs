using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PrototypeTrailBuilder
{
    private readonly struct Edge
    {
        public Edge(string name, string start, string end)
        {
            Name = name;
            Start = start;
            End = end;
        }

        public string Name { get; }
        public string Start { get; }
        public string End { get; }
    }

    private static readonly Edge[] Edges =
    {
        new("T_Camp_A", "N00_Camp", "N01_A"),
        new("T_Camp_B", "N00_Camp", "N02_B"),
        new("T_A_C", "N01_A", "N03_C"),
        new("T_A_D", "N01_A", "N04_D"),
        new("T_B_E", "N02_B", "N05_E"),
        new("T_B_F", "N02_B", "N06_F"),
        new("T_E_G", "N05_E", "N07_G")
    };

    [MenuItem("Tools/Cascade Lab/Create Prototype Trails")]
    public static void CreatePrototypeTrails()
    {
        GameObject graph = GameObject.Find("Graph");
        if (graph == null)
        {
            Debug.LogError("PrototypeTrailBuilder: Could not find the Graph object.");
            return;
        }

        Transform trails = graph.transform.Find("Trails");
        if (trails == null)
        {
            GameObject trailsObject = new("Trails");
            Undo.RegisterCreatedObjectUndo(trailsObject, "Create Trails container");
            trailsObject.transform.SetParent(graph.transform, false);
            trails = trailsObject.transform;
        }

        Material trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/M_trail.mat");

        foreach (Edge edge in Edges)
        {
            GameObject startNode = GameObject.Find(edge.Start);
            GameObject endNode = GameObject.Find(edge.End);

            if (startNode == null || endNode == null)
            {
                Debug.LogError(
                    $"PrototypeTrailBuilder: Missing node for {edge.Name}: " +
                    $"{edge.Start} -> {edge.End}.");
                continue;
            }

            Transform existing = trails.Find(edge.Name);
            GameObject trailObject;

            if (existing == null)
            {
                trailObject = new GameObject(edge.Name);
                Undo.RegisterCreatedObjectUndo(trailObject, $"Create {edge.Name}");
                trailObject.transform.SetParent(trails, false);
            }
            else
            {
                trailObject = existing.gameObject;
                Undo.RecordObject(trailObject, $"Update {edge.Name}");
            }

            LineRenderer line = trailObject.GetComponent<LineRenderer>();
            if (line == null)
            {
                line = Undo.AddComponent<LineRenderer>(trailObject);
            }

            Undo.RecordObject(line, $"Configure {edge.Name}");
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 2;
            line.startWidth = 0.15f;
            line.endWidth = 0.15f;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.generateLightingData = false;

            if (trailMaterial != null)
            {
                line.sharedMaterial = trailMaterial;
            }

            Vector3 start = startNode.transform.position;
            Vector3 end = endNode.transform.position;
            start.y = 0.08f;
            end.y = 0.08f;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Selection.activeObject = trails.gameObject;
        Debug.Log("Created or updated all seven prototype trails.");
    }
}
