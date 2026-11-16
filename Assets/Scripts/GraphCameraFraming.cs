using System.Collections.Generic;
using UnityEngine;

// Keeps the whole rescue graph (nodes, trails, structures and node labels)
// visible in the part of the screen that the planning panel doesn't cover.
[RequireComponent(typeof(Camera))]
public class GraphCameraFraming : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Attach()
    {
        Camera mainCamera = Camera.main;

        if (
            mainCamera != null &&
            mainCamera.GetComponent<GraphCameraFraming>() == null
        )
        {
            mainCamera.gameObject.AddComponent<GraphCameraFraming>();
        }
    }

    [SerializeField, Range(0f, 0.2f)]
    [Tooltip("Empty space kept around the graph, as a fraction of the screen.")]
    private float margin = 0.04f;

    private Camera cam;
    private float minOrthographicSize;
    private RescuePlanningUI planningUI;
    private RescueNodeLabel[] labels;
    private readonly List<Renderer> graphRenderers = new List<Renderer>();
    private readonly Vector3[] corners = new Vector3[4];

    private float minX, maxX, minY, maxY;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        minOrthographicSize = cam.orthographicSize;
    }

    private void Start()
    {
        planningUI = FindAnyObjectByType<RescuePlanningUI>();
        labels = FindObjectsByType<RescueNodeLabel>(FindObjectsSortMode.None);

        HashSet<Transform> graphRoots = new HashSet<Transform>();

        foreach (RescueNode node in FindObjectsByType<RescueNode>(FindObjectsSortMode.None))
        {
            graphRoots.Add(node.transform.root);
        }

        foreach (Transform root in graphRoots)
        {
            graphRenderers.AddRange(root.GetComponentsInChildren<Renderer>());
        }
    }

    private void LateUpdate()
    {
        if (!cam.orthographic || graphRenderers.Count == 0)
        {
            return;
        }

        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;

        foreach (Renderer graphRenderer in graphRenderers)
        {
            if (graphRenderer == null || !graphRenderer.enabled)
            {
                continue;
            }

            Bounds b = graphRenderer.bounds;

            for (int i = 0; i < 8; i++)
            {
                Include(new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z
                ));
            }
        }

        foreach (RescueNodeLabel label in labels)
        {
            if (label == null || label.LabelRect == null)
            {
                continue;
            }

            label.LabelRect.GetWorldCorners(corners);

            foreach (Vector3 corner in corners)
            {
                Include(corner);
            }
        }

        foreach (WorldLabel worldLabel in WorldLabel.Active)
        {
            if (worldLabel == null || worldLabel.Rect == null)
            {
                continue;
            }

            worldLabel.Rect.GetWorldCorners(corners);

            foreach (Vector3 corner in corners)
            {
                Include(corner);
            }
        }

                // Screen area to the right of the planning panel, in 0..1 viewport units.
        float left = margin;

        if (
            planningUI != null &&
            planningUI.PanelRect != null &&
            planningUI.PanelRect.gameObject.activeInHierarchy
        )
        {
            planningUI.PanelRect.GetWorldCorners(corners);
            left = corners[2].x / Screen.width + margin;
        }

        float right = 1f - margin;
        float bottom = margin;
        float top = 1f - margin;
        float availableWidth = Mathf.Max(0.1f, right - left);
        float availableHeight = top - bottom;

        // Zoom out only if the graph wouldn't fit at the scene's camera size.
        float size = Mathf.Max(
            minOrthographicSize,
            (maxY - minY) / (2f * availableHeight),
            (maxX - minX) / (2f * cam.aspect * availableWidth)
        );
        cam.orthographicSize = size;

        // Slide the camera so the graph's centre lands in the middle of that area.
        float targetX = ((left + right) * 0.5f - 0.5f) * 2f * size * cam.aspect;
        float targetY = ((bottom + top) * 0.5f - 0.5f) * 2f * size;
        float shiftX = (minX + maxX) * 0.5f - targetX;
        float shiftY = (minY + maxY) * 0.5f - targetY;

        transform.position += transform.right * shiftX + transform.up * shiftY;
    }

    private void Include(Vector3 worldPoint)
    {
        Vector3 p = transform.InverseTransformPoint(worldPoint);
        minX = Mathf.Min(minX, p.x);
        maxX = Mathf.Max(maxX, p.x);
        minY = Mathf.Min(minY, p.y);
        maxY = Mathf.Max(maxY, p.y);
    }
}
