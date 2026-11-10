using TMPro;
using UnityEngine;

public class RescuePlanningUI : MonoBehaviour
{
    [Header("Game")]
    [SerializeField]
    private RescueGameController gameController;

    [Header("Text")]
    [SerializeField]
    private TMP_Text dayText;

    [SerializeField]
    private TMP_Text groupsText;

    [SerializeField]
    private TMP_Text selectedText;

    [SerializeField]
    private TMP_Text assignmentText;

    private void Update()
    {
        RefreshDisplay();
    }

    public void AddGroup()
    {
        gameController.AddGroupToSelectedNode();
        RefreshDisplay();
    }

    public void RemoveGroup()
    {
        gameController.RemoveGroupFromSelectedNode();
        RefreshDisplay();
    }

    public void ClearPlan()
    {
        gameController.ClearPlan();
        RefreshDisplay();
    }

    public void BeginSearch()
    {
        gameController.ConfirmPlan();
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (gameController == null)
        {
            return;
        }

        dayText.text =
            "DAY " +
            gameController.CurrentDay +
            " / " +
            gameController.TotalDays;

        groupsText.text =
            "RESCUE GROUPS AVAILABLE: " +
            gameController.GroupsRemaining;

        RescueNode selectedNode = null;

        if (NodeSelectionManager.Instance != null)
        {
            selectedNode =
                NodeSelectionManager.Instance.SelectedNode;
        }

        if (selectedNode == null)
        {
            selectedText.text = "SELECTED DESTINATION: NONE";
            assignmentText.text = "GROUPS ASSIGNED HERE: 0";
            return;
        }

        selectedText.text =
            "SELECTED DESTINATION: " +
            selectedNode.nodeId;

        assignmentText.text =
            "GROUPS ASSIGNED HERE: " +
            gameController.GetAssignedGroups(selectedNode.nodeId);
    }
}