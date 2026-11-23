using System.Collections.Generic;
using UnityEngine;

public sealed partial class ReviewStudyFlow : MonoBehaviour
{
    private readonly Dictionary<string, List<Vector3>> liteTrails = new();
    private readonly Dictionary<string, int> liteRouteEdges = new();
    private readonly List<Vector2> liteProjected = new();

    private int LiteSlot(string target) => liteRouteSlots.TryGetValue(target, out int slot) ? slot : 0;
    private static Color LiteRouteColor(int slot) => slot == 0 ? FrostUiTheme.RouteOrange : FrostUiTheme.RouteGreen;

    private void DrawLiteRoutes()
    {
        if (!tacticalRoutesVisible || !IsLitePlanning || draft.Count == 0 || Event.current.type != EventType.Repaint ||
            Camera.main is not Camera camera)
            return;

        liteRouteEdges.Clear();
        foreach (var expedition in draft)
        {
            int route = 1 << LiteSlot(expedition.targetNodeId);
            foreach (var node in scenario.GetPathFromCamp(expedition.targetNodeId))
            {
                liteRouteEdges.TryGetValue(node.nodeId, out int previous);
                liteRouteEdges[node.nodeId] = previous | route;
            }
        }

        Rect board = LiteMapRect;
        if (board.width <= 0 || board.height <= 0) return;
        GUI.BeginGroup(board);
        float size = LiteScale;
        foreach (var edge in liteRouteEdges)
        {
            liteProjected.Clear();
            foreach (var point in GetLiteTrail(edge.Key))
                liteProjected.Add(ToLiteMap(camera, point, board));

            // Give shared edges two lanes so neither color overwrites the other.
            for (int i = 1; i < liteProjected.Count; i++)
            {
                Vector2 a = liteProjected[i - 1], b = liteProjected[i], d = b - a;
                if (d.sqrMagnitude < .01f) continue;
                Vector2 offset = edge.Value == 3 ? new Vector2(-d.y, d.x).normalized * (3.2f * size) : Vector2.zero;
                if ((edge.Value & 1) != 0) DrawLiteRouteStroke(a - offset, b - offset, LiteRouteColor(0), size);
                if ((edge.Value & 2) != 0) DrawLiteRouteStroke(a + offset, b + offset, LiteRouteColor(1), size);
            }
        }

        foreach (var expedition in draft)
        {
            if (!scenario.TryGetSceneNode(expedition.targetNodeId, out var node)) continue;
            int slot = LiteSlot(expedition.targetNodeId);
            Vector2 target = ToLiteMap(camera, node.transform.position, board);
            Rect tag = new(target.x + 7 * size, target.y - 21 * size, 23 * size, 21 * size);
            TintLite(new Rect(tag.x + 5 * size, tag.y, tag.width - 5 * size, tag.height),
                (slot + 1).ToString(), liteSmall, LiteRouteColor(slot));
        }
        GUI.EndGroup();
    }

    private List<Vector3> GetLiteTrail(string id)
    {
        if (liteTrails.TryGetValue(id, out var points)) return points;
        points = new List<Vector3>();
        liteTrails.Add(id, points);
        var node = scenario.GetDefinition(id);
        if (node == null || !scenario.TryGetSceneNode(id, out var target)) return points;
        string parentId = node.parentNodeId;
        string suffix = (parentId + "_" + id).Replace(" ", "_").Replace("'", "");
        GameObject trail = GameObject.Find("Trail_" + suffix) ?? GameObject.Find("T_" + suffix);
        if (trail != null)
        {
            // The authored map uses both continuous lines and ordered dash segments.
            foreach (LineRenderer renderer in trail.GetComponentsInChildren<LineRenderer>())
            {
                for (int i = 0; i < renderer.positionCount; i++)
                {
                    Vector3 p = renderer.GetPosition(i);
                    points.Add(renderer.useWorldSpace ? p : renderer.transform.TransformPoint(p));
                }
            }
        }
        if (points.Count >= 2) return points;

        points.Clear();
        Transform from = parentId == "Camp" ? GameObject.Find("N00_Camp")?.transform :
            scenario.TryGetSceneNode(parentId, out var parent) ? parent.transform : null;
        if (from != null)
        {
            points.Add(from.position);
            points.Add(target.transform.position);
        }
        return points;
    }

    private static Vector2 ToLiteMap(Camera camera, Vector3 point, Rect board)
    {
        Vector3 projected = camera.WorldToScreenPoint(point);
        return new Vector2(projected.x - board.x, Screen.height - projected.y - board.y);
    }

    private static void DrawLiteLine(Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 d = b - a;
        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        FillLite(new Rect(a.x - .4f, a.y - width / 2f, d.magnitude + .8f, width), color);
        GUI.matrix = matrix;
    }

    private static void DrawLiteRouteStroke(Vector2 a, Vector2 b, Color color, float scale)
    {
        // A clean dark edge makes planned routes legible on bright snow as well as rock.
        DrawLiteLine(a, b, FrostUiTheme.Coal, 6.2f * scale);
        DrawLiteLine(a, b, color, 4.6f * scale);
    }
}
