using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RescueNode))]
public class RescueNodeLabel : MonoBehaviour
{
    private static readonly Color UnknownColor =
        new Color(0.66f, 0.66f, 0.66f, 1f);

    private static readonly Color KnownColor =
        new Color(0.094f, 0.702f, 0.541f, 1f);

    private static readonly string UnknownHex = ColorUtility.ToHtmlStringRGB(UnknownColor);
    private static readonly string KnownHex = ColorUtility.ToHtmlStringRGB(KnownColor);

    // Label sits above and to the right of its node; its bottom edge stays
    // just above the node so the node itself is never covered.
    private static readonly Vector3 LabelOffset = new Vector3(0.85f, 1.1f, 0f);

    private RescueNode rescueNode;
    private ScenarioController scenarioController;
    private GameObject labelObject;
    private RectTransform labelTransform;
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

        Camera activeCamera = Camera.main;

        if (activeCamera != null)
        {
            labelTransform.position =
                transform.position + LabelOffset;
            labelTransform.rotation = activeCamera.transform.rotation;
        }
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
        labelTransform.sizeDelta = new Vector2(330f, 190f);

        Canvas canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;

        GameObject backgroundObject = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(Image)
        );
        backgroundObject.transform.SetParent(labelObject.transform, false);

        RectTransform backgroundRect =
            backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        Image background = backgroundObject.GetComponent<Image>();
        background.color = new Color(0.16f, 0.16f, 0.16f, 0.92f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text)
        );
        textObject.transform.SetParent(labelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(18f, 10f);
        textRect.offsetMax = new Vector2(-18f, -10f);

        labelText = textObject.GetComponent<Text>();
        labelText.font =
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 44;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelText.verticalOverflow = VerticalWrapMode.Overflow;
        labelText.raycastTarget = false;
        labelText.text = Line("HERE", false, 0) + "\n" + Line("BEYOND", false, 0) + "\n" + Line("DIFF", false, 0);
        labelText.color = Color.white;
    }
}
