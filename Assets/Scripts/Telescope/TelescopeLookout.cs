using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single fixed lookout at Cedar Valley camp that every telescope sighting is taken from.
/// Scenes without one (practice map, Blackridge) keep their existing round flow.
/// </summary>
public sealed class TelescopeLookout : MonoBehaviour
{
    [Tooltip("Telescope eye point; its rotation is the resting view before the telescope is aimed.")]
    public Transform eye;
    [Tooltip("Field of view at the lookout before the optical zoom (degrees).")]
    public float restingFieldOfView = 50f;
    public Shader depthOfFieldShader;
    public List<TelescopeSite> sites = new List<TelescopeSite>();

    public static TelescopeLookout Find() => FindAnyObjectByType<TelescopeLookout>();

    public TelescopeSite Site(string nodeId)
    {
        foreach (TelescopeSite site in sites)
            if (site != null && site.nodeId == nodeId) return site;
        return null;
    }

    public Vector3 EyePosition => eye != null ? eye.position : transform.position;
    public Quaternion RestingRotation => eye != null ? eye.rotation : transform.rotation;
}
