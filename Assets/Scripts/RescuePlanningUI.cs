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
    private Text branchText;

    [SerializeField]
    private Text rescuedText;

    [SerializeField]
    private Text resultText;

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

        ScenarioController scenarioController =
            gameController.GetComponent<ScenarioController>();

        branchText.text =
            "KNOWN SURVIVORS\nBranch A: " +
            scenarioController.CountBranchSurvivors("A") +
            "     Branch B: " +
            scenarioController.CountBranchSurvivors("B");

        rescuedText.text =
            "TOTAL RESCUED: " +
            gameController.TotalRescued +
            " / " +
            gameController.TotalSurvivors;

        resultText.text =
            "LAST SEARCH\n" +
            gameController.LastDaySummary;

        RescueNode selectedNode = null;

        if (NodeSelectionManager.Instance != null)
        {
            selectedNode =
                NodeSelectionManager.Instance.SelectedNode;
        }

        if (selectedNode == null)
        {
            selectedText.text = "SELECTED LOCATION: NONE";
            assignmentText.text =
                "Select a location on the map.\n" +
                "Assigned today: 0";
            return;
        }

        selectedText.text = "SELECTED LOCATION: " + selectedNode.nodeId;
        assignmentText.text = BuildSelectedNodeDetails(selectedNode);
    }

    private string BuildSelectedNodeDetails(RescueNode node)
    {
        int assignedGroups =
            gameController.GetAssignedGroups(node.nodeId);

        if (!node.explored)
        {
            return
                "Status: Unexplored\n" +
                "Assigned today: " +
                assignedGroups;
        }

        if (!node.reached)
        {
            return
                "Required groups: " +
                node.difficulty +
                "\nStatus: Route not passed" +
                "\nAssigned today: " +
                assignedGroups;
        }

        string rescueStatus =
            node.survivorsRescued ? " - Rescued" : string.Empty;

        return
            "Required groups: " +
            node.difficulty +
            "\nSurvivors: " +
            node.survivorCount +
            rescueStatus +
            "\nAssigned today: " +
            assignedGroups;
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
        panelImage.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.zero;
        panelRect.pivot = Vector2.zero;
        panelRect.anchoredPosition = new Vector2(30f, 30f);
        panelRect.sizeDelta = new Vector2(640f, 520f);

        planningContent = CreateStretchContainer("PlanningContent", panel.transform);
        gameOverContent = CreateStretchContainer("GameOverContent", panel.transform);

        dayText = CreateText(planningContent.transform, "DayText", 24f, -18f, 270f, 38f, false, 28);
        groupsText = CreateText(planningContent.transform, "GroupsText", 330f, -18f, 286f, 38f, false, 22, TextAnchor.MiddleRight);
        CreateDivider(planningContent.transform, -66f, 592f);

        branchText = CreateText(planningContent.transform, "BranchText", 24f, -78f, 592f, 62f, true, 20);
        CreateDivider(planningContent.transform, -150f, 592f);

        selectedText = CreateText(planningContent.transform, "SelectedText", 24f, -162f, 592f, 36f, false, 23);
        assignmentText = CreateText(planningContent.transform, "AssignmentText", 24f, -198f, 592f, 90f, true, 20);
        CreateDivider(planningContent.transform, -296f, 592f);

        rescuedText = CreateText(planningContent.transform, "RescuedText", 24f, -306f, 592f, 34f, false, 21);
        resultText = CreateText(planningContent.transform, "ResultText", 24f, -340f, 592f, 76f, true, 19);

        CreateButton(planningContent.transform, "RemoveGroupButton", "- GROUP", 24f, 74f, 180f, 48f, RemoveGroup);
        CreateButton(planningContent.transform, "AddGroupButton", "+ GROUP", 214f, 74f, 180f, 48f, AddGroup);
        CreateButton(planningContent.transform, "ClearPlanButton", "CLEAR PLAN", 404f, 74f, 212f, 48f, ClearPlan);
        CreateButton(planningContent.transform, "BeginSearchButton", "BEGIN SEARCH", 24f, 18f, 592f, 46f, BeginSearch);

        gameOverTitleText = CreateText(gameOverContent.transform, "GameOverTitle", 24f, -120f, 592f, 60f, false, 30, TextAnchor.MiddleCenter);
        gameOverScoreText = CreateText(gameOverContent.transform, "GameOverScore", 24f, -190f, 592f, 70f, true, 26, TextAnchor.MiddleCenter);
        CreateButton(gameOverContent.transform, "RestartButton", "RESTART", 120f, 90f, 400f, 52f, RestartGame);
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

    private static void CreateDivider(Transform parent, float y, float width)
    {
        GameObject divider = CreateUIObject("Divider", parent);
        divider.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

        RectTransform rect = divider.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, y);
        rect.sizeDelta = new Vector2(width, 2f);
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
        TextAnchor alignment = TextAnchor.MiddleLeft
    )
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = new Color(0.92f, 0.95f, 0.96f, 1f);
        text.alignment = alignment;
        text.horizontalOverflow =
            wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

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
        UnityEngine.Events.UnityAction action
    )
    {
        GameObject buttonObject = CreateUIObject(objectName, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.35f, 0.35f, 0.35f, 1f);

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
        labelText.fontSize = 20;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleCenter;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }
}
