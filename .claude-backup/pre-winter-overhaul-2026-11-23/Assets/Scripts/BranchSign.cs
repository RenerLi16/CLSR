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
        Camera activeCamera = Camera.main;
        Vector3 up = activeCamera != null ? activeCamera.transform.up : Vector3.up;
        label = WorldLabel.Create(
            "SignLabel_" + branchName,
            transform.position + up * (0.8f * WorldLabel.SceneScale),
            new Vector2(625f, 224f),
            new Vector2(0f, 0.5f),
            47,
            TextAnchor.MiddleLeft
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

        int estimate = scenarioController.GetBranchEstimate(branch);
        string report = (scenarioController.HasRevisedBranchEstimate(branch) ? "Revised: " : "Report: ") +
            estimate + (estimate == 1 ? " survivor" : " survivors");

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
            "<color=#C8C8C8>" + (branch.note ?? "").Replace(" · ", "\n") + "</color>";
    }
}
