using UnityEngine;
using UnityEngine.UI;

public class RescuePlanningUI : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInterfaceExists()
    {
        if (FindAnyObjectByType<RescuePlanningUI>() != null)
        {
            return;
        }

        RescueGameController controller =
            FindAnyObjectByType<RescueGameController>();

        if (controller != null)
        {
            controller.gameObject.AddComponent<RescuePlanningUI>();
        }
    }

    [Header("Game")]
    [SerializeField]
    private RescueGameController gameController;

    [Header("Text")]
    [SerializeField]
    private Text dayText;

    [SerializeField]
    private Text groupsText;

    [SerializeField]
    private Text selectedText;

    [SerializeField]
    private Text assignmentText;

    [SerializeField]
    private Text rescuedText;

    [SerializeField]
    private Text resultText;

    private static readonly Color PanelColor = Color.white;
    private static readonly Color TextColor = Color.black;
    private static readonly Color ButtonColor = new Color(0.05f, 0.29f, 0.31f, 1f);
    private static readonly Color ButtonTextColor = Color.white;

    private const float PanelWidth = 720f;
    private const float PanelHeight = 860f;
    private const int MinFontSize = 16;

    private GameObject planningContent;
    private GameObject gameOverContent;
    private Text gameOverTitleText;
    private Text gameOverScoreText;

    private void Awake()
    {
        if (gameController == null)
        {
            gameController = GetComponent<RescueGameController>();
        }

        if (dayText == null)
        {
            BuildPrototypeInterface();
        }
    }

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

    public void RestartGame()
    {
        gameController.RestartGame();
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (gameController == null)
        {
            return;
        }

        bool gameOver = gameController.GameOver;

        if (planningContent != null)
        {
            planningContent.SetActive(!gameOver);
            gameOverContent.SetActive(gameOver);
        }

        if (gameOver)
        {
            gameOverTitleText.text = "SEARCH COMPLETE";
            gameOverScoreText.text =
                gameController.TotalRescued +
                " OF " +
                gameController.TotalSurvivors +
                " SURVIVORS RESCUED";
            return;
        }

        dayText.text =
            "DAY " +
            gameController.CurrentDay +
            " OF " +
            gameController.TotalDays;

        groupsText.text =
            "GROUPS LEFT: " +
            gameController.GroupsRemaining;

        rescuedText.text =
            "TOTAL RESCUED: " +
            gameController.TotalRescued +
            " / " +
            gameController.TotalSurvivors;

        resultText.text =
            "<b>LAST SEARCH</b>\n" +
            gameController.LastDaySummary;

        RescueNode selectedNode = null;

        if (NodeSelectionManager.Instance != null)
        {
            selectedNode =
                NodeSelectionManager.Instance.SelectedNode;
        }

        if (selectedNode == null)
        {
            selectedText.text = "SELECTED LOCATION: <b>NONE</b>";
            assignmentText.text =
                "Status: <b>No location selected</b>\n" +
                "Assigned today: <b>0</b>";
            return;
        }

        selectedText.text =
            "SELECTED LOCATION: <b>" + selectedNode.nodeId.ToUpper() + "</b>";
        assignmentText.text = BuildSelectedNodeDetails(selectedNode);
    }

    private string BuildSelectedNodeDetails(RescueNode node)
    {
        int assignedGroups =
            gameController.GetAssignedGroups(node.nodeId);

        if (!node.explored)
        {
            return
                "Status: <b>Unexplored</b>\n" +
                "Assigned today: <b>" +
                assignedGroups +
                "</b>";
        }

        if (!node.reached)
        {
            return
                "Status: <b>Route not passed</b>" +
                "\nAssigned today: <b>" +
                assignedGroups +
                "</b>";
        }

        return
            "Status: <b>Reached</b>" +
            "\nAssigned today: <b>" +
            assignedGroups +
            "</b>";
    }

    private void BuildPrototypeInterface()
    {
        GameObject canvasObject = new GameObject(
            "GameUI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = CreateUIObject("PlanningPanel", canvasObject.transform);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = PanelColor;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.zero;
        panelRect.pivot = Vector2.zero;
        panelRect.anchoredPosition = new Vector2(30f, 30f);
        panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        planningContent = CreateStretchContainer("PlanningContent", panel.transform);
        gameOverContent = CreateStretchContainer("GameOverContent", panel.transform);

        const float pad = 32f;
        const float contentWidth = PanelWidth - pad * 2f;
        const float gap = 12f;
        const float buttonHeight = 66f;

        dayText = CreateText(planningContent.transform, "DayText", pad, -26f, 330f, 56f, false, 44, TextAnchor.MiddleLeft, true);
        groupsText = CreateText(planningContent.transform, "GroupsText", pad + 330f, -26f, contentWidth - 330f, 56f, false, 32, TextAnchor.MiddleRight, true);

        selectedText = CreateText(planningContent.transform, "SelectedText", pad, -108f, contentWidth, 46f, false, 32);
        assignmentText = CreateText(planningContent.transform, "AssignmentText", pad, -162f, contentWidth, 84f, true, 29, TextAnchor.UpperLeft);

        rescuedText = CreateText(planningContent.transform, "RescuedText", pad, -272f, contentWidth, 50f, false, 36, TextAnchor.MiddleLeft, true);

        float buttonsTop = pad + buttonHeight * 2f + gap;
        float resultTop = 346f;
        resultText = CreateText(planningContent.transform, "ResultText", pad, -resultTop, contentWidth, PanelHeight - resultTop - buttonsTop - 24f, true, 28, TextAnchor.UpperLeft);

        float smallWidth = (contentWidth - gap * 2f) / 3f;
        CreateButton(planningContent.transform, "RemoveGroupButton", "- GROUP", pad, pad + buttonHeight + gap, smallWidth, buttonHeight, RemoveGroup);
        CreateButton(planningContent.transform, "AddGroupButton", "+ GROUP", pad + smallWidth + gap, pad + buttonHeight + gap, smallWidth, buttonHeight, AddGroup);
        CreateButton(planningContent.transform, "ClearPlanButton", "CLEAR PLAN", pad + (smallWidth + gap) * 2f, pad + buttonHeight + gap, smallWidth, buttonHeight, ClearPlan);
        CreateButton(planningContent.transform, "BeginSearchButton", "BEGIN SEARCH", pad, pad, contentWidth, buttonHeight, BeginSearch, 34);

        gameOverTitleText = CreateText(gameOverContent.transform, "GameOverTitle", pad, -180f, contentWidth, 70f, false, 46, TextAnchor.MiddleCenter, true);
        gameOverScoreText = CreateText(gameOverContent.transform, "GameOverScore", pad, -270f, contentWidth, 110f, true, 36, TextAnchor.MiddleCenter);
        CreateButton(gameOverContent.transform, "RestartButton", "RESTART", (PanelWidth - 420f) / 2f, 110f, 420f, 72f, RestartGame, 34);
        gameOverContent.SetActive(false);
    }

    private static GameObject CreateStretchContainer(string objectName, Transform parent)
    {
        GameObject container = CreateUIObject(objectName, parent);
        RectTransform rect = container.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return container;
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static Text CreateText(
        Transform parent,
        string objectName,
        float x,
        float y,
        float width,
        float height,
        bool wrap = false,
        int fontSize = 21,
        TextAnchor alignment = TextAnchor.MiddleLeft,
        bool bold = false
    )
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.supportRichText = true;
        text.color = TextColor;
        text.alignment = alignment;
        text.raycastTarget = false;
        // Wrap/truncate inside the rect and shrink the font if the text
        // would not otherwise fit, so nothing spills out of the panel.
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = MinFontSize;
        text.resizeTextMaxSize = fontSize;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);

        return text;
    }

    private static Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        float x,
        float y,
        float width,
        float height,
        UnityEngine.Events.UnityAction action,
        int fontSize = 30
    )
    {
        GameObject buttonObject = CreateUIObject(objectName, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = ButtonColor;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);

        GameObject labelObject = CreateUIObject("Label", buttonObject.transform);
        Text labelText = labelObject.AddComponent<Text>();
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.text = label;
        labelText.fontSize = fontSize;
        labelText.fontStyle = FontStyle.Bold;
        labelText.resizeTextForBestFit = true;
        labelText.resizeTextMinSize = MinFontSize;
        labelText.resizeTextMaxSize = fontSize;
        labelText.raycastTarget = false;
        labelText.color = ButtonTextColor;
        labelText.alignment = TextAnchor.MiddleCenter;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }
}
