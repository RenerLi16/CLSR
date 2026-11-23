using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CampLabel : MonoBehaviour
{
    private GameObject labelObject;
    private RectTransform labelTransform;
    private float labelScale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AttachOnSceneLoad()
    {
        SceneManager.sceneLoaded -= EnsureCampLabelExists;
        SceneManager.sceneLoaded += EnsureCampLabelExists;
    }

    private static void EnsureCampLabelExists(Scene scene, LoadSceneMode mode)
    {
        GameObject camp = GameObject.Find("N00_Camp");

        if (camp != null && camp.GetComponent<CampLabel>() == null)
        {
            camp.AddComponent<CampLabel>();
        }
    }

    private void Awake()
    {
        BuildLabel();
    }

    private void LateUpdate()
    {
        Camera activeCamera = Camera.main;

        if (activeCamera == null || labelTransform == null)
        {
            return;
        }

        labelTransform.position =
            transform.position - activeCamera.transform.up * (0.82f * labelScale) -
            activeCamera.transform.forward * 1.25f;
        labelTransform.rotation = activeCamera.transform.rotation;
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
            "CampLabel",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );

        labelTransform = labelObject.GetComponent<RectTransform>();
        labelScale = WorldLabel.SceneScale;
        labelTransform.localScale = Vector3.one * (WorldLabel.PixelsToWorld * labelScale);
        labelTransform.sizeDelta = new Vector2(242f, 80f);

        Canvas canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 20;

        GameObject textObject = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(Text)
        );
        textObject.transform.SetParent(labelObject.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 6f);
        textRect.offsetMax = new Vector2(-12f, -6f);

        Text text = textObject.GetComponent<Text>();
        text.font = ReviewStudyFlow.StudyFont;
        text.fontSize = 52;
        text.color = FrostUiTheme.Enamel;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        WorldLabel.AddReadabilityOutline(text);
        text.text = "CAMP";
    }
}
