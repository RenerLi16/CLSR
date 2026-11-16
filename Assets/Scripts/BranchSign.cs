using UnityEngine;

/// <summary>
/// Sign above a branch showing its public report (from the scenario's
/// branch list, matched by <see cref="branchName"/>). In Designer truth view
/// it also shows how many survivors are really left on the branch.
/// The sign's left edge sits at this object's position.
/// </summary>
public class BranchSign : MonoBehaviour
{
    [Tooltip("Must match a branch name in the scenario's branch list.")]
    [SerializeField]
    private string branchName;

    private BranchReport branch;
    private ScenarioController scenarioController;
    private WorldLabel label;

    private void Awake()
    {
        label = WorldLabel.Create(
            "SignLabel_" + branchName,
            transform.position,
            new Vector2(554f, 150f),
            new Vector2(0f, 0.5f),
            36,
            TextAnchor.MiddleLeft,
            false
        );
        label.transform.SetParent(transform, true);
    }

    private void Start()
    {
        scenarioController = FindAnyObjectByType<ScenarioController>();

        if (scenarioController != null && scenarioController.CurrentScenario != null)
        {
            branch = scenarioController.CurrentScenario.branches.Find(
                report => report.branchName == branchName
            );
        }

        if (branch == null)
        {
            Debug.LogWarning("Branch sign " + name + " has no matching branch \"" + branchName + "\" in the scenario.");
        }
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
