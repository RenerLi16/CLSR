using System;
using System.Collections.Generic;
using CLSR.Presentation;
using UnityEngine;

/// <summary>
/// Drives one private telescope sighting on the map camera: a continuous move from the planning
/// view to the fixed camp lookout, an aim along the sightline to the chosen site, an optical
/// zoom (field of view only, the camera stays at camp), then a fixed-framing viewing window in
/// which focus is the only control. Afterwards everything is put back exactly as it was.
///
/// While a sighting runs the map camera's own framing is switched off (so wheel input cannot
/// move the planning map), map overlays are hidden, only the observed site's survivor figures
/// are shown, and vegetation in front of the site is left out of the sightline. Presentation
/// only: the simulation is never touched; the flow passes in what to show.
/// </summary>
[DefaultExecutionOrder(240)] // after WinterAtmosphere (220) so the sightline haze reference wins
public sealed class TelescopeSighting : MonoBehaviour
{
    public enum Stage { Idle, Flying, Aiming, Zooming, Settling, Viewing, FadingOut, FadingIn }

    private static readonly int ClipId = Shader.PropertyToID("_TelescopeClipDepth");
    private static readonly int RefDepthId = Shader.PropertyToID("_WinterRefDepth");
    private const float FadeSeconds = 0.32f;

    public Stage Current { get; private set; } = Stage.Idle;
    public bool IsActive => Current != Stage.Idle;
    public bool IsViewing => Current == Stage.Viewing;
    /// <summary>The focus control is usable (and the clock running) only while viewing.</summary>
    public bool FocusUsable => Current == Stage.Viewing;
    public float SecondsLeft { get; private set; }
    public float FocusNear { get; private set; }
    public float FocusFar { get; private set; }
    public float FocusTarget { get; private set; }
    public float FocusCurrent { get; private set; }
    public float WheelAngle => TelescopeOptics.WheelAngle(FocusCurrent, FocusNear, FocusFar);
    public string NodeId { get; private set; }
    public int CrossingCost { get; private set; }
    /// <summary>Black overlay for the return to the map (0..1).</summary>
    public float FadeAlpha { get; private set; }
    public float AimDistance { get; private set; }

    /// <summary>Raised once the map view has been restored (behind the fade).</summary>
    public event Action Restored;

    private Camera cam;
    private GraphCameraFraming framing;
    private TelescopeDepthOfField dof;
    private TelescopeLookout lookout;
    private TelescopeSite site;
    private float clock;
    private Vector3 startPos, eyePos;
    private Quaternion startRot, restRot, aimRot;
    private float startFov, restFov, aimFov, startNear, startFar;
    private bool framingWasEnabled;
    private float clipDepth;
    private readonly List<(Renderer r, bool on)> hiddenRenderers = new List<(Renderer, bool)>();
    private readonly List<(Canvas c, bool on)> hiddenCanvases = new List<(Canvas, bool)>();

    public static TelescopeSighting On(Camera camera)
    {
        if (camera == null) return null;
        return camera.TryGetComponent(out TelescopeSighting s) ? s : camera.gameObject.AddComponent<TelescopeSighting>();
    }

    public void Begin(TelescopeLookout from, TelescopeSite target, int survivorsHere, int crossingCost)
    {
        if (IsActive) Restore();
        cam = GetComponent<Camera>();
        lookout = from;
        site = target;
        NodeId = target.nodeId;
        CrossingCost = crossingCost;

        framing = cam.GetComponent<GraphCameraFraming>();
        framingWasEnabled = framing != null && framing.enabled;
        if (framing != null) framing.enabled = false;
        startPos = cam.transform.position;
        startRot = cam.transform.rotation;
        startFov = cam.fieldOfView;
        startNear = cam.nearClipPlane;
        startFar = cam.farClipPlane;

        restRot = from.RestingRotation;
        restFov = from.restingFieldOfView;
        PoseFor(from, target, out eyePos, out aimRot, out aimFov, out float distance);
        AimDistance = distance;
        clipDepth = Mathf.Max(0f, AimDistance - target.clipClearance);

        // One repeatable starting setting for every turn: the near end of the focus travel.
        FocusNear = Mathf.Max(5f, AimDistance - target.focusRange);
        FocusFar = AimDistance + target.focusRange;
        FocusTarget = FocusCurrent = FocusNear;

        if (!cam.TryGetComponent(out dof)) dof = cam.gameObject.AddComponent<TelescopeDepthOfField>();
        dof.shader = from.depthOfFieldShader;
        dof.Aperture = TelescopeOptics.Aperture(AimDistance, target.frameHeight);
        dof.FocusDistance = FocusCurrent;
        dof.enabled = false;

        HideMapOverlays();
        foreach (TelescopeSite s in from.sites) if (s != null && s != target) s.HideSurvivors();
        target.ShowSurvivors(Mathf.Clamp(survivorsHere, 0, target.Capacity));

        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = Mathf.Max(startFar, 3000f);
        clock = 0f;
        SecondsLeft = TelescopeOptics.ViewSeconds;
        FadeAlpha = 0f;
        Current = Stage.Flying;
    }

    /// <summary>The fixed sighting pose for a site: camera at the lookout, aimed and framed.</summary>
    public static void PoseFor(TelescopeLookout from, TelescopeSite target, out Vector3 position,
        out Quaternion rotation, out float fieldOfView, out float aimDistance)
    {
        position = from.EyePosition;
        Vector3 toSite = target.aim.position - position;
        aimDistance = toSite.magnitude;
        rotation = Quaternion.LookRotation(toSite, Vector3.up);
        fieldOfView = TelescopeOptics.FovForFrame(target.frameHeight, aimDistance);
    }

    /// <summary>Applies the sighting's clip depth for a site (used by the editor framing review).</summary>
    public static void SetSightlineClip(float depth) => Shader.SetGlobalFloat(ClipId, Mathf.Max(0f, depth));

    /// <summary>Turns the dial by a focus distance (m); ignored outside the viewing window.</summary>
    public void TurnFocus(float metres)
    {
        if (!FocusUsable) return;
        FocusTarget = TelescopeOptics.Clamp(FocusTarget + metres, FocusNear, FocusFar);
    }

    /// <summary>Ends the sighting at once (scene change, abort) and restores the map.</summary>
    public void Abort()
    {
        if (!IsActive) return;
        Restore();
        FadeAlpha = 0f;
        Current = Stage.Idle;
    }

    private void Update()
    {
        if (!IsActive) return;
        float dt = Time.unscaledDeltaTime;
        clock += dt;
        FocusCurrent = TelescopeOptics.Approach(FocusCurrent, FocusTarget, dt);

        switch (Current)
        {
            case Stage.Flying:
            {
                float u = TelescopeOptics.SmootherStep(clock / TelescopeOptics.FlySeconds);
                cam.transform.SetPositionAndRotation(Vector3.Lerp(startPos, eyePos, u), Quaternion.Slerp(startRot, restRot, u));
                cam.fieldOfView = Mathf.Lerp(startFov, restFov, u);
                if (clock >= TelescopeOptics.FlySeconds) Next(Stage.Aiming);
                break;
            }
            case Stage.Aiming:
            {
                float u = TelescopeOptics.SmoothStep(clock / TelescopeOptics.AimSeconds);
                cam.transform.SetPositionAndRotation(eyePos, Quaternion.Slerp(restRot, aimRot, u));
                cam.fieldOfView = restFov;
                if (clock >= TelescopeOptics.AimSeconds) { Next(Stage.Zooming); dof.enabled = true; }
                break;
            }
            case Stage.Zooming:
            {
                float u = TelescopeOptics.SmootherStep(clock / TelescopeOptics.ZoomSeconds);
                cam.transform.SetPositionAndRotation(eyePos, aimRot);
                // magnification changes geometrically, so the narrowing reads as one steady zoom
                float t0 = Mathf.Log(Mathf.Tan(restFov * 0.5f * Mathf.Deg2Rad));
                float t1 = Mathf.Log(Mathf.Tan(aimFov * 0.5f * Mathf.Deg2Rad));
                cam.fieldOfView = 2f * Mathf.Atan(Mathf.Exp(Mathf.Lerp(t0, t1, u))) * Mathf.Rad2Deg;
                Shader.SetGlobalFloat(ClipId, clipDepth * u * u);
                if (clock >= TelescopeOptics.ZoomSeconds) Next(Stage.Settling);
                break;
            }
            case Stage.Settling:
                Hold();
                if (clock >= TelescopeOptics.SettleSeconds) Next(Stage.Viewing);
                break;
            case Stage.Viewing:
                Hold();
                SecondsLeft = Mathf.Max(0f, TelescopeOptics.ViewSeconds - clock);
                if (SecondsLeft <= 0f) Next(Stage.FadingOut);
                break;
            case Stage.FadingOut:
                Hold();
                FadeAlpha = Mathf.Clamp01(clock / FadeSeconds);
                if (clock >= FadeSeconds)
                {
                    Restore();
                    Next(Stage.FadingIn);
                    Restored?.Invoke();
                }
                break;
            case Stage.FadingIn:
                FadeAlpha = 1f - Mathf.Clamp01(clock / FadeSeconds);
                if (clock >= FadeSeconds) { FadeAlpha = 0f; Current = Stage.Idle; }
                break;
        }
        if (dof != null && dof.enabled) dof.FocusDistance = FocusCurrent;
    }

    private void LateUpdate()
    {
        if (Current == Stage.Zooming || Current == Stage.Settling || Current == Stage.Viewing || Current == Stage.FadingOut)
            Shader.SetGlobalFloat(RefDepthId, Mathf.Max(0f, AimDistance - 45f)); // haze only beyond the site
    }

    private void Hold()
    {
        cam.transform.SetPositionAndRotation(eyePos, aimRot);
        cam.fieldOfView = aimFov;
        Shader.SetGlobalFloat(ClipId, clipDepth);
    }

    private void Next(Stage stage)
    {
        Current = stage;
        clock = 0f;
    }

    private void HideMapOverlays()
    {
        MapOverlayScaler.Suppressed = true;
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
        GameObject graph = GameObject.Find("Graph");
        if (graph == null) return;
        foreach (Renderer r in graph.GetComponentsInChildren<Renderer>(true))
        {
            hiddenRenderers.Add((r, r.enabled));
            r.enabled = false;
        }
        foreach (Canvas c in graph.GetComponentsInChildren<Canvas>(true))
        {
            hiddenCanvases.Add((c, c.enabled));
            c.enabled = false;
        }
    }

    private void Restore()
    {
        Shader.SetGlobalFloat(ClipId, 0f);
        if (dof != null) dof.enabled = false;
        if (site != null) site.HideSurvivors();
        foreach (var (r, on) in hiddenRenderers) if (r != null) r.enabled = on;
        foreach (var (c, on) in hiddenCanvases) if (c != null) c.enabled = on;
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
        MapOverlayScaler.Suppressed = false;
        if (cam != null)
        {
            cam.transform.SetPositionAndRotation(startPos, startRot);
            cam.fieldOfView = startFov;
            cam.nearClipPlane = startNear;
            cam.farClipPlane = startFar;
        }
        if (framing != null) framing.enabled = framingWasEnabled;
        site = null;
        NodeId = null;
        CrossingCost = 0;
        FocusTarget = FocusCurrent = FocusNear;
    }

    private void OnDisable()
    {
        if (IsActive) Abort();
        Shader.SetGlobalFloat(ClipId, 0f);
    }
}
