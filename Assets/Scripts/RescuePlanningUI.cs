using TMPro;
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
    private TMP_Text dayText;

    [SerializeField]
    private TMP_Text groupsText;

    [SerializeField]
    private TMP_Text selectedText;

    [SerializeField]
    private TMP_Text assignmentText;

    [SerializeField]
    private TMP_Text branchText;

    [SerializeField]
    private TMP_Text rescuedText;

    [SerializeField]
    private TMP_Text resultText;

    private Button removeGroupButton;
    private Button addGroupButton;
    private Button clearPlanButton;
    private Button beginSearchButton;
    private Button restartButton;

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

        dayText.text =
            "DAY " +
            gameController.CurrentDay +
            " / " +
            gameController.TotalDays;

        groupsText.text =
            "RESCUE GROUPS AVAILABLE: " +
            gameController.GroupsRemaining;

        ScenarioController scenarioController =
            gameController.GetComponent<ScenarioController>();

        branchText.text =
            "KNOWN SURVIVORS: A " +
            scenarioController.CountBranchSurvivors("A") +
            " | B " +
            scenarioController.CountBranchSurvivors("B");

        rescuedText.text =
            "RESCUED: " +
            gameController.TotalRescued +
            " / " +
            gameController.TotalSurvivors;

        resultText.text = gameController.LastDaySummary;

        UpdateButtonState();

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

        selectedText.text = BuildSelectedNodeText(selectedNode);

        assignmentText.text =
            "GROUPS ASSIGNED HERE: " +
            gameController.GetAssignedGroups(selectedNode.nodeId);
    }

    private static string BuildSelectedNodeText(RescueNode node)
    {
        if (!node.explored)
        {
            return "SELECTED: " + node.nodeId + " | UNEXPLORED";
        }

        if (!node.reached)
        {
            return
                "SELECTED: " +
                node.nodeId +
                " | STOPPED | NEEDS " +
                node.difficulty +
                " GROUPS";
        }

        string rescueStatus =
            node.survivorsRescued ? " | RESCUED" : string.Empty;

        return
            "SELECTED: " +
            node.nodeId +
            " | NEEDS " +
            node.difficulty +
            " | SURVIVORS " +
            node.survivorCount +
            rescueStatus;
    }

    private void UpdateButtonState()
    {
        bool planning = !gameController.GameOver;

        if (removeGroupButton != null)
        {
            removeGroupButton.interactable = planning;
            addGroupButton.interactable = planning;
            clearPlanButton.interactable = planning;
            beginSearchButton.gameObject.SetActive(planning);
            restartButton.gameObject.SetActive(!planning);
        }
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
        panelRect.sizeDelta = new Vector2(580f, 390f);

        dayText = CreateText(panel.transform, "DayText", 22f, -18f, 536f, 30f);
        groupsText = CreateText(panel.transform, "GroupsText", 22f, -50f, 536f, 30f);
        branchText = CreateText(panel.transform, "BranchText", 22f, -82f, 536f, 30f);
        selectedText = CreateText(panel.transform, "SelectedText", 22f, -124f, 536f, 30f);
        assignmentText = CreateText(panel.transform, "AssignmentText", 22f, -156f, 536f, 30f);
        rescuedText = CreateText(panel.transform, "RescuedText", 22f, -198f, 536f, 30f);
        resultText = CreateText(panel.transform, "ResultText", 22f, -230f, 536f, 64f, true);

        removeGroupButton = CreateButton(panel.transform, "RemoveGroupButton", "- GROUP", 22f, 74f, 165f, 48f, RemoveGroup);
        addGroupButton = CreateButton(panel.transform, "AddGroupButton", "+ GROUP", 197f, 74f, 165f, 48f, AddGroup);
        clearPlanButton = CreateButton(panel.transform, "ClearPlanButton", "CLEAR PLAN", 372f, 74f, 186f, 48f, ClearPlan);
        beginSearchButton = CreateButton(panel.transform, "BeginSearchButton", "BEGIN SEARCH", 22f, 18f, 536f, 46f, BeginSearch);
        restartButton = CreateButton(panel.transform, "RestartButton", "RESTART", 22f, 18f, 536f, 46f, RestartGame);
        restartButton.gameObject.SetActive(false);
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        float x,
        float y,
        float width,
        float height,
        bool wrap = false
    )
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = 23f;
        text.color = new Color(0.92f, 0.95f, 0.96f, 1f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode =
            wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;

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
        TextMeshProUGUI labelText = labelObject.AddComponent<TextMeshProUGUI>();
        labelText.text = label;
        labelText.fontSize = 20f;
        labelText.color = Color.white;
        labelText.alignment = TextAlignmentOptions.Center;

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        return button;
    }
}
