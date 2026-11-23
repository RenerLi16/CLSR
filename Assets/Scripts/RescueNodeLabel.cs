using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RescueNode))]
public class RescueNodeLabel : MonoBehaviour
{
    private static readonly string UnknownHex = ColorUtility.ToHtmlStringRGB(FrostUiTheme.Frost);
    private static readonly string KnownHex = ColorUtility.ToHtmlStringRGB(FrostUiTheme.Rescued);

    // Label sits above and to the right of its node; its bottom edge stays
    // just above the node so the node itself is never covered.
    private static readonly Vector3 LabelOffset = new Vector3(0.96f, 1.24f, 0f);

    private RescueNode rescueNode;
    private ScenarioController scenarioController;
    private GameObject labelObject;
    private RectTransform labelTransform;
    private Canvas labelCanvas;
    private Text labelText;

    public RectTransform LabelRect => labelTransform;

    private void Awake()
    {
        rescueNode = GetComponent<RescueNode>();
        scenarioController = FindAnyObjectByType<ScenarioController>();
        BuildLabel();
    }

    private void LateUpdate()
    {
        if (
            rescueNode == null ||
            scenarioController == null ||
            labelText == null
        )
        {
            return;
        }

        Camera activeCamera = Camera.main;
        if (activeCamera != null)
        {
            labelTransform.position = transform.position + LabelOffset;
            labelTransform.rotation = activeCamera.transform.rotation;
        }

        // The study flow draws a screen-space hover card. Keep the legacy label's bounds
        // for camera framing and render it only outside the study flow.
        labelCanvas.enabled = !ReviewStudyFlow.IsActive;
        if (!labelCanvas.enabled)
            return;

        string nodeId = rescueNode.nodeId;
        bool survivorsKnown = scenarioController.AreSurvivorCountsKnown(nodeId);
        bool difficultyKnown = scenarioController.IsDifficultyKnown(nodeId);
        RescueNodeDefinition definition = scenarioController.GetDefinition(nodeId);

        bool frontierCount = scenarioController.TryGetDiscoveredBranchCount(nodeId, out int branchCount)
            && !survivorsKnown;
        string hereAndBeyond = frontierCount
            ? Line("BRANCH", true, branchCount) + "\n" + Line("HERE", false, 0)
            : Line("HERE", survivorsKnown, scenarioController.CountRemainingHere(nodeId)) + "\n" +
            Line("BEYOND", survivorsKnown, scenarioController.CountRemainingBeyond(nodeId));
        labelText.text = hereAndBeyond + "\n" +
            Line("DIFF", difficultyKnown, definition != null ? definition.difficulty : 0);
    }

    private static string Line(string name, bool known, int value)
    {
        if (known)
        {
            return name + ": <color=#" + KnownHex + ">" + value + "</color>";
        }

        // Designer truth: show the hidden value in purple so it is clear
        // that players cannot see it yet.
        if (DesignerView.TruthVisible)
        {
            return "<color=#" + UnknownHex + ">" + name + ":</color> <color=#" +
                DesignerView.TruthHex + ">" + value + "</color>";
        }

        return "<color=#" + UnknownHex + ">" + name + ": ?</color>";
    }

    private void OnDestroy()
    {
        if (labelObject != null)
        {
            Destroy(labelObject);
        }
    }

    private void BuildLabel()
    {
        labelObject = new GameObject(
            "NodeInfoLabel",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        labelTransform = labelObject.GetComponent<RectTransform>();
        labelTransform.position =
            transform.position + LabelOffset;
        labelTransform.localScale = Vector3.one * 0.0083f;
        labelTransform.sizeDelta = new Vector2(360f, 225f);

        labelCanvas = labelObject.GetComponent<Canvas>();
        labelCanvas.renderMode = RenderMode.WorldSpace;
        labelCanvas.overrideSorting = true;
        labelCanvas.sortingOrder = 20;
        labelCanvas.enabled = false;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text)
        );
        textObject.transform.SetParent(labelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(13f, 10f);
        textRect.offsetMax = new Vector2(-13f, -10f);

        labelText = textObject.GetComponent<Text>();
        labelText.font = ReviewStudyFlow.StudyFont;
        labelText.fontSize = 55;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelText.verticalOverflow = VerticalWrapMode.Overflow;
        labelText.raycastTarget = false;
        labelText.text = Line("HERE", false, 0) + "\n" + Line("BEYOND", false, 0) + "\n" + Line("DIFF", false, 0);
        labelText.color = FrostUiTheme.Enamel;
        WorldLabel.AddReadabilityOutline(labelText);
    }
}
