using UnityEngine;

/// <summary>
/// Player view vs Designer truth. In Designer truth every hidden value is
/// shown (in purple) so the scenario can be checked and balanced.
/// </summary>
public static class DesignerView
{
    public static readonly Color TruthColor = new Color(0.71f, 0.55f, 1f, 1f);
    public static readonly string TruthHex = ColorUtility.ToHtmlStringRGB(TruthColor);

    // Darker shade for text on the white planning panel.
    public static readonly string TruthOnLightHex =
        ColorUtility.ToHtmlStringRGB(new Color(0.42f, 0.24f, 0.72f, 1f));

    public static bool TruthVisible { get; set; }

    /// <summary>
    /// The toggle is only offered in the Editor and in Development builds,
    /// so published builds cannot reveal the answers.
    /// </summary>
    public static bool Available
    {
        get { return Application.isEditor || Debug.isDebugBuild; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        TruthVisible = false;
    }
}
