using UnityEngine;

/// <summary>
/// Visible storage camp. Its reward is public from the start; the tag
/// switches to CLAIMED once an expedition passes the camp's node.
/// </summary>
public class StorageCampMarker : MonoBehaviour
{
    private string nodeId;
    private int reward;
    private WorldLabel campLabel;
    private RescueGameController gameController;
    private Renderer markerRenderer;
    private Color unclaimedColor;

    public void Initialise(string campNodeId, int logisticsReward, WorldLabel label)
    {
        nodeId = campNodeId;
        reward = logisticsReward;
        campLabel = label;
        markerRenderer = GetComponent<Renderer>();

        if (markerRenderer != null)
        {
            unclaimedColor = markerRenderer.material.color;
        }
    }

    private void Start()
    {
        gameController = FindAnyObjectByType<RescueGameController>();
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
