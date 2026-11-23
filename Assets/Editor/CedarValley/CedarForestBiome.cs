using UnityEditor;
using UnityEngine;

/// <summary>
/// Configurable species mix for the Cedar Valley winter forest (editor-time; read by the
/// landscape builder). Frost-covered broadleaf trees dominate sheltered valley floors and
/// riverbanks; conifers take over with elevation, on steep slopes and along wind-exposed
/// crests. Species form patches (stands) rather than a per-tree salt-and-pepper mix.
/// Edit the asset, then use Tools > Cedar Valley > Rebuild Forest and Map Overlays.
/// </summary>
public class CedarForestBiome : ScriptableObject
{
    public const string AssetPath = "Assets/Art/CedarValley/Cedar winter forest biome.asset";

    [Header("Broadleaf (frosted deciduous) share")]
    [Range(0f, 1f)] public float valleyBroadleafShare = 0.66f;
    [Tooltip("Above this ground height (m) conifers increasingly dominate.")]
    public float broadleafElevation = 16f;
    [Tooltip("Height range (m) over which broadleaf share fades out above broadleafElevation.")]
    public float broadleafFade = 22f;
    [Tooltip("Slope (rise/run) beyond which conifers are favoured.")]
    public float coniferSlope = 0.36f;
    [Tooltip("Extra broadleaf share within ~30 m of the river.")]
    [Range(0f, 0.5f)] public float riparianBoost = 0.18f;
    [Tooltip("Stand (patch) size in metres for species clustering.")]
    public float standScale = 46f;
    [Range(0f, 1f)] public float standContrast = 0.7f;

    [Header("Structure")]
    [Tooltip("Share of young trees and saplings at forest edges.")]
    [Range(0f, 1f)] public float saplingShare = 0.16f;
    [Tooltip("Shrubs per tree at edges and in clearings (close and mid zoom).")]
    [Range(0f, 2f)] public float shrubDensity = 0.55f;
    [Tooltip("Global scale for broadleaf heights.")]
    [Range(0.6f, 1.4f)] public float broadleafHeightScale = 1f;

    [Header("Shadows")]
    [Tooltip("Sun elevation used for baked tree shadows (degrees).")]
    [Range(10f, 60f)] public float shadowSunElevation = 24f;
    [Range(0f, 1f)] public float shadowStrength = 0.55f;

    public static CedarForestBiome LoadOrCreate()
    {
        var b = AssetDatabase.LoadAssetAtPath<CedarForestBiome>(AssetPath);
        if (b != null) return b;
        b = CreateInstance<CedarForestBiome>();
        AssetDatabase.CreateAsset(b, AssetPath);
        AssetDatabase.SaveAssets();
        return b;
    }
}
