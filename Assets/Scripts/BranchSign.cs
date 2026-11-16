using UnityEngine;

/// <summary>
/// Sign above a branch showing its public report. In Designer truth view it
/// also shows how many survivors are really left on the branch.
/// </summary>
public class BranchSign : MonoBehaviour
{
    private BranchReport branch;
    private ScenarioController scenarioController;
    private WorldLabel label;

    public void Initialise(BranchReport report, ScenarioController controller)
    {
        branch = report;
        scenarioController = controller;
        label = GetComponent<WorldLabel>();
    }

    private void LateUpdate()
    {
        if (branch == null || label == null)
        {
            return;
        }

        string report =
            "Report: " + branch.reportedSurvivors +
            (branch.reportedSurvivors == 1 ? " survivor" : " survivors");

        if (
            DesignerView.TruthVisible &&
            scenarioController != null &&
            !string.IsNullOrEmpty(branch.rootNodeId)
        )
        {
            report +=
                "  <color=#" + DesignerView.TruthHex + ">(true: " +
                scenarioController.CountRemainingBranchSurvivors(branch.rootNodeId) +
                " left)</color>";
        }

        label.Content =
            "<b>" + branch.branchName.ToUpper() + "</b>\n" +
            report + "\n" +
            "<color=#A8A8A8>" + branch.note + "</color>";
    }
}
