using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RescueNode))]
public class RescueNodeLabel : MonoBehaviour
{
    private static readonly Color UnknownColor =
        new Color(0.66f, 0.66f, 0.66f, 1f);

    private static readonly Color KnownColor =
        new Color(0.118f, 0.839f, 0.643f, 1f);

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

        bool informationKnown =
            scenarioController.IsNodeInformationKnown(rescueNode.nodeId);

        if (informationKnown)
        {
            labelText.text =
                "NUM: " +
                scenarioController.CountRemainingBranchSurvivors(
                    rescueNode.nodeId
                ) +
                "\nDIFF: " +
                rescueNode.difficulty;
            labelText.color = KnownColor;
        }
        else
        {
            labelText.text = "NUM: NAN\nDIFF: NAN";
            labelText.color = UnknownColor;
        }

        Camera activeCamera = Camera.main;

        if (activeCamera != null)
        {
            labelTransform.position =
                transform.position + new Vector3(0.85f, 0.9f, 0f);
            labelTransform.rotation = activeCamera.transform.rotation;
        }
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
            transform.position + new Vector3(0.85f, 0.9f, 0f);
        labelTransform.localScale = Vector3.one * 0.0083f;
        labelTransform.sizeDelta = new Vector2(330f, 142f);

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
        labelText.fontSize = 50;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelText.verticalOverflow = VerticalWrapMode.Overflow;
        labelText.raycastTarget = false;
        labelText.text = "NUM: NAN\nDIFF: NAN";
        labelText.color = UnknownColor;
    }
}
