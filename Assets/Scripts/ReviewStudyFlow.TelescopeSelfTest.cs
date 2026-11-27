#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CLSR.Presentation;
using CLSR.Simulation;
using UnityEngine;

// Development self-test for the Cedar Valley telescope phase. Plays a full three-day session
// through the same actions the buttons perform (nine private turns), and checks ordering,
// privacy resets, camera behaviour, the viewing clock, survivor figures against the
// simulation, and that a sighting leaves the rescue simulation untouched.
// Editor: Tools > Cedar Valley > Telescope > Run Self-Test In Play Mode (also writes captures).
// Development web build: open the page with ?selftest=telescope and read the browser console.
public sealed partial class ReviewStudyFlow
{
    private static bool selfTestRequested;
    private static string selfTestOutput; // editor: folder for the report and captures
    private bool selfTestRunning;
    private readonly StringBuilder selfTestLog = new();
    private int selfTestPass, selfTestFail;

    public static bool SelfTestRunning => active != null && active.selfTestRunning;
    public static string SelfTestReport => active != null ? active.selfTestLog.ToString() : "";
    public static bool SelfTestFinished { get; private set; }

    /// <summary>Starts the self-test in the next Cedar Valley scene that has a lookout.</summary>
    public static void RequestTelescopeSelfTest(string outputFolder)
    {
        selfTestRequested = true;
        selfTestOutput = outputFolder;
        SelfTestFinished = false;
    }

    private void PollSelfTest()
    {
        if (!selfTestRequested && Application.platform == RuntimePlatform.WebGLPlayer &&
            Application.absoluteURL.Contains("selftest=telescope") && !SelfTestFinished && !selfTestRunning)
            selfTestRequested = true;
        if (!selfTestRequested || selfTestRunning || scenario == null || !scenario.IsReady || Lookout == null) return;
        selfTestRequested = false;
        selfTestRunning = true;
        StartCoroutine(TelescopeSelfTest());
    }

    private void Check(bool ok, string what)
    {
        if (ok) selfTestPass++; else selfTestFail++;
        string line = (ok ? "PASS " : "FAIL ") + what;
        selfTestLog.AppendLine(line);
        if (ok) Debug.Log("[telescope self-test] " + line); else Debug.LogWarning("[telescope self-test] " + line);
    }

    private void Note(string what)
    {
        selfTestLog.AppendLine("     " + what);
        Debug.Log("[telescope self-test] " + what);
    }

    private string SimSnapshot()
    {
        var sim = scenario.Simulation;
        var sb = new StringBuilder();
        sb.Append($"day {sim.Day} pool {sim.Pool} pending {sim.PendingPoints} granted {sim.PointsGranted} rescued {sim.TotalRescued} over {sim.GameOver} ");
        sb.Append("reports " + string.Join("|", sim.ReleasedReports) + " pendingBranch " + sim.HasPendingBranchReports + " ");
        foreach (NodeSpec n in scenario.Spec.Nodes)
            sb.Append($"{n.Id}:{(sim.IsExplored(n.Id) ? 1 : 0)}{(sim.IsReached(n.Id) ? 1 : 0)}{(sim.IsRescued(n.Id) ? 1 : 0)}{(sim.IsCacheClaimed(n.Id) ? 1 : 0)}" +
                      $"d{sim.CurrentDifficulty(n.Id)}r{sim.RemainingHere(n.Id)} ");
        foreach (PublicNode n in scenario.View().Nodes)
            sb.Append($"[{n.Id} {n.KnownRemainingHere} {n.KnownRemainingBeyond} {n.KnownDifficulty} {n.KnownBranchRemaining}]");
        sb.Append(" history " + sim.History.Count);
        return sb.ToString();
    }

    private IEnumerator Wait(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end) yield return null;
    }

    private IEnumerator TelescopeSelfTest()
    {
        selfTestLog.Clear();
        selfTestPass = selfTestFail = 0;
        Note("Cedar Valley telescope self-test " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
             $" screen {Screen.width}x{Screen.height} platform {Application.platform}");
        yield return null;

        // A scenario session as the setup screen would make it (No AI, countdowns skipped).
        session = new Session
        {
            Condition = 1, SkipTimers = true, UseMockBackend = false, ScenarioIndex = 0,
            Order = new[] { "CedarValley", "BlackridgeStorm" }, StartedAt = Time.realtimeSinceStartupAsDouble,
            WriteLog = false, Log = new ResearchLog("selftest")
        };
        game.RestartGame();
        Go(Phase.Observe);
        yield return null;
        Check(UsesTelescope, "scenario session in Cedar Valley uses the telescope");

        string[][] looks =
        {
            new[] { "C3", "R5", "C3" },  // day 1: a repeat selection by a different player, and the farthest site
            new[] { "C2", "W2", "R1" },  // day 2: two sites cleared on day 1, and the nearest site
            new[] { "C3", "N3", "W4" },  // day 3: C3 cleared on day 2
        };
        List<ExpeditionAssignment>[] plans =
        {
            Plan(("C2", 2), ("W2", 2)),
            Plan(("C3", 3), ("N2", 2)),
            Plan(),
        };
        int sightings = 0;
        bool sawReportBeforeDay2 = false;

        for (int day = 1; day <= 3; day++)
        {
            yield return null;
            Check(phase == Phase.Observe && game.CurrentDay == day, $"day {day} begins on the shared map (phase {phase}, day {game.CurrentDay})");
            if (day == 2) Check(sawReportBeforeDay2, "day 2 intelligence report was shown and acknowledged before the day 2 sightings");
            StartPrivateJudgments();
            for (int p = 0; p < 3; p++)
            {
                yield return null;
                Check(phase == Phase.Handoff && IsTelescopeHandoff && player == p,
                    $"day {day} player {p + 1}: neutral handoff screen first");
                Check(NodeSelectionManager.Instance == null || NodeSelectionManager.Instance.SelectedNode == null,
                    "  no site selected on the handoff screen");
                Check(draft.Count == 0 && answers.Count == 0, "  previous player's plan and form are cleared");
                Check(sighting == null || !sighting.IsActive || sighting.Current == TelescopeSighting.Stage.FadingIn,
                    "  no telescope view is left on screen");
                bool ui = day == 1 && p == 0;
                if (ui) yield return Shot("ui_1_handoff.png");
                Go(Phase.TelescopeSelect); // Ready
                yield return null;
                Check(phase == Phase.TelescopeSelect, "  Ready opens the site browser");
                if (ui) yield return Shot("ui_2_browse.png");
                captureUi = ui;
                yield return RunSighting(day, p, looks[day - 1][p]);
                sightings++;
                Check(phase == Phase.Initial && player == p, $"  player {p + 1} goes straight into the private initial judgment");
                if (ui) { yield return Wait(0.5f); yield return Shot("ui_6_initial.png"); }
                draft.Clear();
                foreach (ExpeditionAssignment leg in plans[day - 1])
                    draft.Add(new ExpeditionAssignment { targetNodeId = leg.targetNodeId, assignedGroups = leg.assignedGroups });
                FinishInitialJudgment();
            }
            yield return null;
            Check(phase == Phase.Discuss, $"day {day}: discussion follows the third judgment (phase {phase})");
            Check(initialResponses.Count == 3, "  three initial judgments recorded");

            // provisional plan -> intervention -> reconsider -> final plan -> ratings -> results
            SetDraft(plans[day - 1]);
            AdvanceLiteBoard();
            for (int i = 0; i < 30 && phase == Phase.WaitForSupport; i++) yield return null;
            Check(phase == Phase.Intervention, "  provisional plan leads to the intervention");
            yield return null;
            ContinueAfterIntervention();
            yield return null;
            AdvanceLiteBoard();
            Check(phase == Phase.FinalPlan, "  reconsideration leads to the final plan");
            SetDraft(plans[day - 1]);
            AdvanceLiteBoard();
            for (int p = 0; p < 3; p++)
            {
                yield return null;
                Check(phase == Phase.Handoff && !IsTelescopeHandoff, $"  pre-outcome handoff {p + 1} has no telescope");
                Go(form);
                FinishPreOutcome();
            }
            yield return null;
            Check(phase == Phase.Result, $"  day {day} resolves (phase {phase})");
            AdvanceLiteBoard();
            for (int p = 0; p < 3; p++)
            {
                yield return null;
                Go(form);
                FinishPostOutcome();
            }
            yield return null;
            if (phase == Phase.BranchReport)
            {
                sawReportBeforeDay2 |= day == 1;
                Note("intelligence report: " + string.Join(" | ", scenario.PendingBranchReportMessages()));
                AcknowledgeBranchReport();
            }
        }
        yield return null;
        Check(phase == Phase.Handoff && form == Phase.Reasoning, "after day 3 the scenario moves on to reasoning");
        Check(sightings == 9, $"nine private telescope turns ({sightings})");

        if (Application.isEditor) yield return FramingReview();

        Note($"done: {selfTestPass} passed, {selfTestFail} failed");
        WriteSelfTestReport();
        selfTestRunning = false;
        SelfTestFinished = true;
        session = null;
        Go(Phase.Setup);
    }

    private bool captureUi;

    // Editor: a capture of the Game view including the IMGUI interface.
    private static void ScreenShotNow(string name)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(selfTestOutput)) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(selfTestOutput, name));
#endif
    }

    private IEnumerator Shot(string name)
    {
        yield return null;
        ScreenShotNow(name);
        yield return null;
        yield return null;
    }

    private static List<ExpeditionAssignment> Plan(params (string node, int points)[] legs) =>
        legs.Select(l => new ExpeditionAssignment { targetNodeId = l.node, assignedGroups = l.points }).ToList();

    private void SetDraft(List<ExpeditionAssignment> plan)
    {
        draft.Clear();
        foreach (ExpeditionAssignment leg in plan)
            draft.Add(new ExpeditionAssignment { targetNodeId = leg.targetNodeId, assignedGroups = leg.assignedGroups });
    }

    private IEnumerator RunSighting(int day, int p, string id)
    {
        Camera cam = Camera.main;
        TelescopeSite site = Lookout.Site(id);
        Check(site != null, $"  site {id} has a telescope framing");
        if (site == null || cam == null) yield break;
        if (scenario.TryGetSceneNode(id, out RescueNode node)) NodeSelectionManager.Instance?.SelectNode(node); // map click
        yield return null;
        bool shots = captureUi;
        captureUi = false;
        if (shots) yield return Shot("ui_3_selected.png");
        bool shotZoom = false, shotView = false;

        string before = SimSnapshot();
        Vector3 mapPos = cam.transform.position;
        Quaternion mapRot = cam.transform.rotation;
        float mapFov = cam.fieldOfView;
        int expected = scenario.CountRemainingHere(id);
        int cost = scenario.Simulation.CurrentDifficulty(id);
        var framing = cam.GetComponent<GraphCameraFraming>();

        Vector3 restoredPos = default;
        Quaternion restoredRot = default;
        float restoredFov = 0f;
        void Capture()
        {
            restoredPos = cam.transform.position;
            restoredRot = cam.transform.rotation;
            restoredFov = cam.fieldOfView;
        }

        BeginSighting(site); // "Observe this site"
        Check(phase == Phase.TelescopeView && sighting != null && sighting.IsActive, $"  {id}: observation locked in");
        sighting.Restored += Capture;
        Check(NodeSelectionManager.Instance == null || NodeSelectionManager.Instance.SelectedNode == null, "  selection cleared at lock-in");

        float started = Time.unscaledTime, arrival = -1f;
        float lastFov = cam.fieldOfView, maxStep = 0f;
        bool clockStill = true, camFixedDuringZoom = true, fovMonotonic = true, mapLocked = true, overlaysHidden = true;
        bool figuresRight = true;
        int frames = 0;
        float frameTime = 0f;
        bool rolled = false;
        Vector3 eye = Lookout.EyePosition;
        var shown = new List<int>();

        while (sighting.IsActive && phase == Phase.TelescopeView)
        {
            var stage = sighting.Current;
            if (stage != TelescopeSighting.Stage.FadingIn)
            {
                mapLocked &= framing == null || !framing.enabled;
                mapLocked &= GraphCameraFraming.AllowWheelZoomAt == null ||
                             !GraphCameraFraming.AllowWheelZoomAt(new Vector2(Screen.width * .5f, Screen.height * .5f));
                overlaysHidden &= MapOverlayScaler.Suppressed;
                int active = site.survivors.Count(f => f != null && f.gameObject.activeSelf);
                int others = Lookout.sites.Where(s => s != site).Sum(s => s.survivors.Count(f => f != null && f.gameObject.activeSelf));
                figuresRight &= active == Mathf.Min(expected, site.Capacity) && others == 0;
            }
            if (stage == TelescopeSighting.Stage.Zooming || stage == TelescopeSighting.Stage.Aiming)
            {
                camFixedDuringZoom &= (cam.transform.position - eye).sqrMagnitude < 1e-6f;
                if (stage == TelescopeSighting.Stage.Zooming)
                {
                    fovMonotonic &= cam.fieldOfView <= lastFov + 1e-4f;
                    // rate of magnification change (per second, in log tan space): continuous
                    // whatever the frame rate, so a slow frame cannot hide a jump
                    float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
                    float rate = Mathf.Abs(Mathf.Log(Mathf.Tan(lastFov * .5f * Mathf.Deg2Rad)) -
                                           Mathf.Log(Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad))) / dt;
                    maxStep = Mathf.Max(maxStep, rate);
                }
            }
            if (stage != TelescopeSighting.Stage.Viewing && stage != TelescopeSighting.Stage.FadingOut && stage != TelescopeSighting.Stage.FadingIn)
                clockStill &= Mathf.Approximately(sighting.SecondsLeft, TelescopeOptics.ViewSeconds);
            if (shots && !shotZoom && stage == TelescopeSighting.Stage.Zooming && sighting.SecondsLeft > 0f &&
                cam.fieldOfView < Lookout.restingFieldOfView * 0.35f)
            {
                shotZoom = true;
                ScreenShotNow("ui_4_zooming.png");
            }
            if (shots && !shotView && stage == TelescopeSighting.Stage.Viewing && sighting.SecondsLeft < 9f)
            {
                shotView = true;
                ScreenShotNow("ui_5_viewing.png");
            }
            if (stage == TelescopeSighting.Stage.Viewing)
            {
                if (arrival < 0f) arrival = Time.unscaledTime - started;
                frames++;
                frameTime += Time.unscaledDeltaTime;
                if (!rolled)
                {
                    rolled = true;
                    if (day == 1 && p == 0) yield return FocusRoll(site);
                    else
                    {
                        // a player rolling focus outward over a few seconds
                        for (int i = 0; i < 40 && sighting.IsViewing; i++)
                        {
                            sighting.TurnFocus((sighting.FocusFar - sighting.FocusNear) / 40f);
                            yield return null;
                        }
                    }
                }
            }
            lastFov = cam.fieldOfView;
            yield return null;
        }
        for (int i = 0; i < 40 && sighting.IsActive; i++) yield return null;
        sighting.Restored -= Capture;

        Note($"  {id}: {expected} survivor(s) here, crossing cost {cost}, aim {sighting.AimDistance:0} m, " +
             $"arrival {arrival:0.00} s, viewing fps {(frameTime > 0 ? frames / frameTime : 0):0}");
        Check(figuresRight, $"  {id}: exactly {expected} survivor figure(s) shown, no other site's figures");
        if (scenario.Simulation.IsRescued(id))
            Check(expected == 0, $"  {id} was cleared earlier and looks empty");
        Check(mapLocked, "  map camera framing off and wheel zoom refused during the sighting");
        Check(overlaysHidden, "  map overlays hidden during the sighting");
        Check(camFixedDuringZoom, "  camera stays at the camp lookout while aiming and zooming");
        Check(fovMonotonic && maxStep < 4.5f, $"  zoom narrows continuously (peak magnification rate {maxStep:0.00}/s, no jumps)");
        Check(clockStill && arrival >= TelescopeOptics.ArrivalSeconds - 0.05f,
            $"  10 s clock starts only after the zoom settles ({arrival:0.00} s)");
        Check(SimSnapshot() == before, "  rescue simulation and public view unchanged by the sighting");
        Check((restoredPos - mapPos).sqrMagnitude < 1e-4f && Quaternion.Angle(restoredRot, mapRot) < 0.01f &&
              Mathf.Abs(restoredFov - mapFov) < 0.01f, "  map camera pose restored exactly");
        Check(framing == null || framing.enabled, "  map camera framing re-enabled");
        Check(!MapOverlayScaler.Suppressed && site.survivors.All(f => f == null || !f.gameObject.activeSelf),
            "  overlays back, figures hidden again");
        Check(Mathf.Approximately(Shader.GetGlobalFloat("_TelescopeClipDepth"), 0f), "  sightline clip switched off");
    }

    // Near-to-far focus roll on one site, with sharpness of the named near and far details.
    private IEnumerator FocusRoll(TelescopeSite site)
    {
        Camera cam = Camera.main;
        if (site.nearDetail == null || site.farDetail == null) { Note("  no near/far details authored"); yield break; }
        float dNear = Vector3.Dot(site.nearDetail.position - cam.transform.position, cam.transform.forward);
        float dFar = Vector3.Dot(site.farDetail.position - cam.transform.position, cam.transform.forward);
        Note($"  focus roll on {site.nodeId}: near detail '{site.nearDetail.name}' {dNear:0.0} m, far detail '{site.farDetail.name}' {dFar:0.0} m");
        float lo = dNear - 3f, hi = dFar + 3f;
        var near = new List<float>();
        var far = new List<float>();
        const int steps = 9;
        for (int i = 0; i <= steps && sighting.IsViewing; i++)
        {
            float f = Mathf.Lerp(lo, hi, i / (float)steps);
            sighting.TurnFocus(f - sighting.FocusTarget);
            yield return Wait(0.4f);
            float a = 0f, b = 0f;
            if (Application.isEditor)
            {
                bool[] maskNear = Mask(cam, site.nearDetail, 1280, 720), maskFar = Mask(cam, site.farDetail, 1280, 720);
                Texture2D shot = Render(cam, 1280, 720);
                a = Sharpness(shot, maskNear);
                b = Sharpness(shot, maskFar);
                SaveCapture(shot, $"focusroll_{site.nodeId}_{i:00}.png");
                Destroy(shot);
            }
            near.Add(a);
            far.Add(b);
            Note($"    focus {sighting.FocusCurrent:0.0} m: near sharpness {a:0.000}  far sharpness {b:0.000}");
        }
        if (Application.isEditor && near.Count > 2)
        {
            int peakNear = near.IndexOf(near.Max()), peakFar = far.IndexOf(far.Max());
            Check(peakNear < peakFar, $"  near detail peaks before far detail as focus rolls outward ({peakNear} < {peakFar})");
            Check(near.Last() < near.Max() * .8f && far.First() < far.Max() * .8f,
                "  each detail softens again away from its own depth");
        }
    }

    // Editor: frames every selectable site at each supported resolution (all figures shown,
    // focus on the aim point), checks every figure lies inside the frame at a recognizable size,
    // and writes a capture per site.
    private IEnumerator FramingReview()
    {
        while (sighting != null && sighting.IsActive) yield return null;
        Camera cam = Camera.main;
        var framing = cam.GetComponent<GraphCameraFraming>();
        if (framing != null) framing.enabled = false;
        Vector3 pos = cam.transform.position;
        Quaternion rot = cam.transform.rotation;
        float fov = cam.fieldOfView, nearClip = cam.nearClipPlane, farClip = cam.farClipPlane;
        MapOverlayScaler.Suppressed = true;
        if (!cam.TryGetComponent(out TelescopeDepthOfField dof)) dof = cam.gameObject.AddComponent<TelescopeDepthOfField>();
        dof.shader = Lookout.depthOfFieldShader;
        dof.enabled = true;
        var hidden = new List<Renderer>();
        GameObject graph = GameObject.Find("Graph");
        if (graph != null)
            foreach (Renderer r in graph.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        var resolutions = new[] { new Vector2Int(1060, 620), new Vector2Int(1280, 720), new Vector2Int(1366, 768),
            new Vector2Int(1920, 1080), new Vector2Int(1024, 768), new Vector2Int(2560, 1440) };
        Note("framing review (all figures shown, focus on the aim point):");
        var blockers = new HashSet<Collider>();
        var tempObjects = new List<UnityEngine.Object>();
        foreach (MeshFilter mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            if (!mf.gameObject.name.StartsWith("Cedar terrain ") || mf.sharedMesh == null) continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            blockers.Add(mc);
            tempObjects.Add(mc);
        }
        foreach (TelescopeSite site in Lookout.sites)
        {
            if (site == null) continue;
            TelescopeSighting.PoseFor(Lookout, site, out Vector3 p, out Quaternion q, out float f, out float d);
            cam.transform.SetPositionAndRotation(p, q);
            cam.fieldOfView = f;
            cam.nearClipPlane = .3f;
            cam.farClipPlane = 3000f;
            TelescopeSighting.SetSightlineClip(d - site.clipClearance);
            dof.Aperture = TelescopeOptics.Aperture(d, site.frameHeight);
            dof.FocusDistance = d;
            site.ShowSurvivors(site.Capacity);
            var siteBlockers = new HashSet<Collider>(blockers);
            foreach (string part in new[] { "Tent", "Crates" })
            {
                Transform t = site.transform.Find(part);
                if (t == null) continue;
                var go = new GameObject("review blocker");
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                bool any = false;
                var lb = new Bounds();
                foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.gameObject.name == "Contact shadow") continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = new((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                        Vector3 lp = go.transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                        if (!any) { lb = new Bounds(lp, Vector3.zero); any = true; } else lb.Encapsulate(lp);
                    }
                }
                var box = go.AddComponent<BoxCollider>();
                box.center = lb.center;
                box.size = lb.size * (part == "Tent" ? 0.92f : 1f);
                siteBlockers.Add(box);
                tempObjects.Add(go);
            }
            Physics.SyncTransforms();
            foreach (SurvivorFigure fig in site.survivors)
            {
                if (fig.pose == SurvivorFigure.Pose.Seated) continue; // inside the open tent mouth: judged from the captures
                float v = VisibleFraction(p, fig, siteBlockers);
                if (fig.pose == SurvivorFigure.Pose.Walking && fig.path.Length > 0)
                {
                    // a walker may pass behind a crate for a moment, but is never hidden
                    Vector3 keep = fig.transform.position;
                    float worst = 1f, sum = 0f;
                    foreach (Vector3 step in fig.path)
                    {
                        fig.transform.position = step;
                        float vis = VisibleFraction(p, fig, siteBlockers);
                        worst = Mathf.Min(worst, vis);
                        sum += vis;
                    }
                    fig.transform.position = keep;
                    float mean = sum / fig.path.Length;
                    Check(worst >= 0.5f && mean >= 0.75f,
                        $"  {site.nodeId}: '{fig.name}' in clear view along its walk (worst point {worst:P0}, mean {mean:P0})");
                    continue;
                }
                Check(v >= 0.75f, $"  {site.nodeId}: '{fig.name}' {v:P0} of sample points in clear view from the lookout");
            }
            yield return null;
            yield return null;
            foreach (Vector2Int res in resolutions)
            {
                Texture2D shot = Render(cam, res.x, res.y);
                bool inside = true;
                float minHeight = float.MaxValue;
                foreach (SurvivorFigure fig in site.survivors)
                {
                    Rect r = ScreenBounds(cam, fig.transform, res.x, res.y);
                    inside &= r.xMin >= res.x * .01f && r.xMax <= res.x * .99f && r.yMin >= res.y * .01f && r.yMax <= res.y * .99f;
                    if (fig.pose != SurvivorFigure.Pose.Seated) minHeight = Mathf.Min(minHeight, r.height);
                }
                float minHeight1080 = minHeight * 1080f / res.y;
                Check(inside && minHeight1080 >= 90f,
                    $"  {site.nodeId} at {res.x}x{res.y}: figures inside frame, smallest standing figure {minHeight:0} px");
                if (res.x == 1280 || res.x == 1024) SaveCapture(shot, $"framing_{site.nodeId}_{res.x}x{res.y}.png");
                Destroy(shot);
            }
            site.HideSurvivors();
        }
        foreach (UnityEngine.Object o in tempObjects) if (o != null) Destroy(o);
        TelescopeSighting.SetSightlineClip(0f);
        dof.enabled = false;
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        MapOverlayScaler.Suppressed = false;
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = fov;
        cam.nearClipPlane = nearClip;
        cam.farClipPlane = farClip;
        if (framing != null) framing.enabled = true;
    }

    private static Texture2D Render(Camera cam, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = previous;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        cam.ResetAspect();
        return tex;
    }

    // Projected bounds (pixels, y up) of everything rendered under a transform, for a w x h target.
    private static Rect ScreenBounds(Camera cam, Transform t, int w, int h)
    {
        float aspect = cam.aspect;
        cam.aspect = w / (float)h;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 v = cam.WorldToViewportPoint(c);
                x0 = Mathf.Min(x0, v.x * w); x1 = Mathf.Max(x1, v.x * w);
                y0 = Mathf.Min(y0, v.y * h); y1 = Mathf.Max(y1, v.y * h);
            }
        }
        cam.aspect = aspect;
        return x0 > x1 ? new Rect(0, 0, 0, 0) : Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // Pixels covered by a detail (and a 2 px border, where its edges are): the detail alone is
    // rendered on a spare layer against black, without depth of field.
    private static bool[] Mask(Camera cam, Transform detail, int w, int h)
    {
        const int spare = 31;
        var layers = new List<(GameObject g, int layer)>();
        foreach (Renderer r in detail.GetComponentsInChildren<Renderer>()) { layers.Add((r.gameObject, r.gameObject.layer)); r.gameObject.layer = spare; }
        int mask = cam.cullingMask;
        CameraClearFlags flags = cam.clearFlags;
        Color bg = cam.backgroundColor;
        bool dofOn = cam.TryGetComponent(out TelescopeDepthOfField dof) && dof.enabled;
        if (dofOn) dof.enabled = false;
        cam.cullingMask = 1 << spare;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        Texture2D t = Render(cam, w, h);
        cam.cullingMask = mask;
        cam.clearFlags = flags;
        cam.backgroundColor = bg;
        if (dofOn) dof.enabled = true;
        foreach (var (g, layer) in layers) g.layer = layer;
        Color32[] px = t.GetPixels32();
        Destroy(t);
        var on = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color32 c = px[y * w + x];
            if (c.r + c.g + c.b < 6) continue;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && yy >= 0 && xx < w && yy < h) on[yy * w + xx] = true;
            }
        }
        return on;
    }

    // Mean luminance gradient over a mask: a simple sharpness measure.
    private static float Sharpness(Texture2D shot, bool[] mask)
    {
        Color32[] px = shot.GetPixels32();
        int w = shot.width, h = shot.height;
        float Lum(int x, int y) { Color32 c = px[y * w + x]; return (.299f * c.r + .587f * c.g + .114f * c.b) / 255f; }
        double sum = 0; int n = 0;
        for (int y = 1; y < h - 1; y++)
        for (int x = 1; x < w - 1; x++)
        {
            if (!mask[y * w + x]) continue;
            float gx = Lum(x + 1, y) - Lum(x - 1, y), gy = Lum(x, y + 1) - Lum(x, y - 1);
            sum += Math.Sqrt(gx * gx + gy * gy);
            n++;
        }
        return n > 0 ? (float)(sum / n) : 0f;
    }

    // Editor: fraction of sample points on a figure with a clear line from the lookout, against
    // terrain and this site's props (temporary colliders).
    private static float VisibleFraction(Vector3 eye, SurvivorFigure fig, HashSet<Collider> blockers)
    {
        var pts = new List<Vector3>();
        Vector3 basePos = fig.transform.position;
        float top = fig.pose == SurvivorFigure.Pose.Seated ? 0.95f : 1.7f;
        foreach (float y in new[] { .25f, .5f, .75f, .95f })
        foreach (float x in new[] { -.12f, .12f })
            pts.Add(basePos + Vector3.up * (top * y) + fig.transform.right * x);
        int clear = 0;
        foreach (Vector3 p in pts)
        {
            Vector3 d = p - eye;
            bool hit = false;
            foreach (RaycastHit h in Physics.RaycastAll(eye, d.normalized, d.magnitude - 0.2f))
                if (blockers.Contains(h.collider)) { hit = true; break; }
            if (!hit) clear++;
        }
        return clear / (float)pts.Count;
    }

    private static void SaveCapture(Texture2D shot, string name)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(selfTestOutput)) return;
        System.IO.Directory.CreateDirectory(selfTestOutput);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(selfTestOutput, name), shot.EncodeToPNG());
#endif
    }

    private void WriteSelfTestReport()
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(selfTestOutput)) return;
        System.IO.Directory.CreateDirectory(selfTestOutput);
        System.IO.File.WriteAllText(System.IO.Path.Combine(selfTestOutput, "selftest.txt"), selfTestLog.ToString());
#endif
    }
}
#endif
