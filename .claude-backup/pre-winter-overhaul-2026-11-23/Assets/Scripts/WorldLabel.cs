using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small camera-facing label placed in the world (branch signs,
/// storage camp tags). Every active label is listed in <see cref="Active"/>
/// so the camera framing can keep them on screen.
/// </summary>
public class WorldLabel : MonoBehaviour
{
    public const float PixelsToWorld = 0.0083f;

    internal static float SceneScale => Camera.main is Camera camera &&
        camera.TryGetComponent(out GraphCameraFraming framing) ? framing.WorldLabelScale : 1f;

    public static readonly List<WorldLabel> Active = new List<WorldLabel>();

    private RectTransform rect;
    private Text text;

    public RectTransform Rect => rect;

    public string Content
    {
        get { return text.text; }
        set { text.text = value; }
    }

    public static WorldLabel Create(
        string objectName,
        Vector3 worldPosition,
        Vector2 sizePixels,
        Vector2 pivot,
        int fontSize,
        TextAnchor alignment
    )
    {
        GameObject labelObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler)
        );

        WorldLabel label = labelObject.AddComponent<WorldLabel>();
        label.rect = labelObject.GetComponent<RectTransform>();
        label.rect.pivot = pivot;
        label.rect.sizeDelta = sizePixels;
        label.rect.localScale = Vector3.one * (PixelsToWorld * SceneScale);
        label.rect.position = worldPosition;

        Canvas canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 15;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(labelObject.transform, false);
        Stretch(textObject.GetComponent<RectTransform>(), new Vector2(16f, 8f));
        label.text = textObject.GetComponent<Text>();
        label.text.font = ReviewStudyFlow.StudyFont;
        label.text.fontSize = fontSize;
        label.text.alignment = alignment;
        label.text.color = FrostUiTheme.Enamel;
        label.text.supportRichText = true;
        label.text.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.text.verticalOverflow = VerticalWrapMode.Truncate;
        label.text.resizeTextForBestFit = true;
        label.text.resizeTextMinSize = 16;
        label.text.resizeTextMaxSize = fontSize;
        label.text.raycastTarget = false;
        AddReadabilityOutline(label.text);

        label.FaceCamera();
        return label;
    }

    internal static void AddReadabilityOutline(Text label)
    {
        var outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = FrostUiTheme.Coal;
        outline.effectDistance = new Vector2(3f, -3f);
    }

    private static void Stretch(RectTransform child, Vector2 inset)
    {
        child.anchorMin = Vector2.zero;
        child.anchorMax = Vector2.one;
        child.offsetMin = inset;
        child.offsetMax = -inset;
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void LateUpdate()
    {
        FaceCamera();
    }

    private void FaceCamera()
    {
        Camera activeCamera = Camera.main;

        if (activeCamera != null && rect != null)
        {
            rect.rotation = activeCamera.transform.rotation;
        }
    }
}
