using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small camera-facing text panel placed in the world (branch signs,
/// storage camp tags). Every active label is listed in <see cref="Active"/>
/// so the camera framing can keep them on screen.
/// </summary>
public class WorldLabel : MonoBehaviour
{
    public const float PixelsToWorld = 0.0083f;

    public static readonly List<WorldLabel> Active = new List<WorldLabel>();

    private RectTransform rect;
    private Text text;
    private Image background;

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
        TextAnchor alignment,
        bool showBackground
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
        label.rect.localScale = Vector3.one * PixelsToWorld;
        label.rect.position = worldPosition;

        Canvas canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 15;

        GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
        backgroundObject.transform.SetParent(labelObject.transform, false);
        Stretch(backgroundObject.GetComponent<RectTransform>(), Vector2.zero);
        label.background = backgroundObject.GetComponent<Image>();
        label.background.color = new Color(0.16f, 0.16f, 0.16f, 0.92f);
        label.background.raycastTarget = false;
        label.background.enabled = showBackground;

        GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(labelObject.transform, false);
        Stretch(textObject.GetComponent<RectTransform>(), new Vector2(16f, 8f));
        label.text = textObject.GetComponent<Text>();
        label.text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text.fontSize = fontSize;
        label.text.alignment = alignment;
        label.text.color = Color.white;
        label.text.supportRichText = true;
        label.text.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.text.verticalOverflow = VerticalWrapMode.Truncate;
        label.text.resizeTextForBestFit = true;
        label.text.resizeTextMinSize = 16;
        label.text.resizeTextMaxSize = fontSize;
        label.text.raycastTarget = false;

        label.FaceCamera();
        return label;
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
