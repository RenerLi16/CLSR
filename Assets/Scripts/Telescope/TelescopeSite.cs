using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the camp telescope frames one Cedar Valley site, and the survivor figures it can show
/// there. Authored by Tools > Cedar Valley > Telescope > Build Lookout And Sites. The figures
/// are inactive except during a sighting of this site; which of them appear comes from the
/// simulation's current count of survivors still at the node, never from the authored scenario.
/// </summary>
public sealed class TelescopeSite : MonoBehaviour
{
    public string nodeId;
    [Tooltip("Point the telescope centres on (ground level of the site, raised to chest height).")]
    public Transform aim;
    [Tooltip("Height of the framed view at the aim point (m).")]
    public float frameHeight = 7.5f;
    [Tooltip("Focus travel either side of the aim distance (m along the view axis).")]
    public float focusRange = 16f;
    [Tooltip("Vegetation and props nearer than (aim distance - clearance) are left out of the sightline.")]
    public float clipClearance = 13f;
    [Tooltip("Slots in the order they fill: the first n are shown for n survivors.")]
    public List<SurvivorFigure> survivors = new List<SurvivorFigure>();
    [Tooltip("Named near and far details used by the focus review (editor only).")]
    public Transform nearDetail, farDetail;

    public int Capacity => survivors.Count;

    public void ShowSurvivors(int count)
    {
        for (int i = 0; i < survivors.Count; i++)
            if (survivors[i] != null) survivors[i].gameObject.SetActive(i < count);
    }

    public void HideSurvivors() => ShowSurvivors(0);

    private void Awake() => HideSurvivors();
}
