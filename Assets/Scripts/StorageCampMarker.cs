using UnityEngine;

/// <summary>
/// Visible storage camp. Its reward is public from the start; the tag
/// switches to CLAIMED once an expedition passes the camp's node.
/// The reward itself comes from the scenario data for <see cref="nodeId"/>.
/// </summary>
public class StorageCampMarker : MonoBehaviour
{
    [Tooltip("Scenario node whose logistics reward this camp represents.")]
    [SerializeField]
    private string nodeId;

    private int reward;
    private WorldLabel campLabel;
    private RescueGameController gameController;
    private Renderer markerRenderer;
    private Color unclaimedColor;

    private void Awake()
    {
        markerRenderer = GetComponent<Renderer>();

        if (markerRenderer != null)
        {
            unclaimedColor = markerRenderer.material.color;
        }

        Camera activeCamera = Camera.main;
        Vector3 up = activeCamera != null ? activeCamera.transform.up : Vector3.up;

        campLabel = WorldLabel.Create(
            "StorageTag_" + nodeId,
            transform.position + up * (transform.localScale.y * 0.75f + 0.3f),
            new Vector2(240f, 60f),
            new Vector2(0.5f, 0.5f),
            36,
            TextAnchor.MiddleCenter,
            true
        );
        campLabel.transform.SetParent(transform.parent, true);
    }

    private void Start()
    {
        gameController = FindAnyObjectByType<RescueGameController>();

        ScenarioController scenarioController = FindAnyObjectByType<ScenarioController>();
        RescueNodeDefinition definition =
            scenarioController != null ? scenarioController.GetDefinition(nodeId) : null;

        if (definition == null)
        {
            Debug.LogWarning("Storage camp " + name + " points at unknown node " + nodeId + ".");
            return;
        }

        reward = definition.logisticsReward;
    }

    private void LateUpdate()
    {
        if (campLabel == null)
        {
            return;
        }

        bool claimed = gameController != null && gameController.IsStorageCampClaimed(nodeId);

        campLabel.Content = claimed
            ? "<b>CLAIMED</b>"
            : "<b>STORAGE +" + reward + "</b>";

        if (markerRenderer != null)
        {
            markerRenderer.material.color = claimed
                ? new Color(0.45f, 0.45f, 0.45f, 1f)
                : unclaimedColor;
        }
    }
}
