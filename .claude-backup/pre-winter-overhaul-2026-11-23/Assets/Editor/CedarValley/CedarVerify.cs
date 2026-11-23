using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Play-mode verification of the rebuilt Cedar Valley map through the game's own camera:
// 1:1 fit, maximum close-up zoom on camp / crossings / every site, pan limits, zoom-out,
// and a physics raycast through each site to confirm nothing in the scenery blocks clicks.
// Camera methods are called by reflection so this editor tool never depends on game code.
internal static class CedarVerify
{
    private const string Flag = "CedarVerify.Pending";
    private static readonly List<(double at, Action act)> steps = new List<(double, Action)>();
    private static double clock;
    private static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

    [MenuItem("Tools/Cedar Valley/Verify In Play Mode", false, 30)]
    private static void Menu()
    {
        if (EditorApplication.isPlaying) { Begin(); return; }
        SessionState.SetBool(Flag, true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnMode;
        EditorApplication.playModeStateChanged += OnMode;
    }

    private static void OnMode(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
        {
            SessionState.SetBool(Flag, false);
            Begin();
        }
    }

    private static string Dir => Path.Combine(CedarValleyBuilder.OutDir, "play");

    private static Component Framing => Camera.main != null ? Camera.main.GetComponent("GraphCameraFraming") : null;

    private static object Call(string method, params object[] args)
    {
        Component f = Framing;
        if (f == null) { log.AppendLine("no GraphCameraFraming"); return null; }
        foreach (MethodInfo m in f.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
            if (m.Name == method && m.GetParameters().Length == args.Length) return m.Invoke(f, args);
        log.AppendLine("missing method " + method);
        return null;
    }

    private static float Prop(string name)
    {
        Component f = Framing;
        PropertyInfo p = f != null ? f.GetType().GetProperty(name) : null;
        return p != null ? Convert.ToSingle(p.GetValue(f)) : float.NaN;
    }

    private static Vector3 Pos(string path)
    {
        GameObject go = GameObject.Find(path);
        if (go == null) return Vector3.zero;
        var r = go.GetComponent<Renderer>();
        return r != null ? r.bounds.center : go.transform.position;
    }

    private static void Begin()
    {
        Directory.CreateDirectory(Dir);
        log.Clear();
        log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " verify");
        steps.Clear();
        clock = EditorApplication.timeSinceStartup + 3.0;
        Step(0f, () => Report("1:1"));
        Step(0.1f, () => Shot("01_full_1x1"));
        Step(0.6f, () => { Call("ZoomTo", -2f, null); });
        Step(1.6f, () => Shot("02_zoomed_out"));
        Step(0.4f, () => { Call("ResetView"); Call("ZoomTo", 0f, null); Call("PanByScreenDelta", new Vector2(4000f, 4000f)); });
        Step(1.0f, () => { Report("1:1 pan +x+y"); Shot("03_pan_corner_a"); });
        Step(0.3f, () => { Call("PanByScreenDelta", new Vector2(-8000f, -8000f)); });
        Step(1.0f, () => { Report("1:1 pan -x-y"); Shot("04_pan_corner_b"); });
        Step(0.3f, () => { Call("ResetView"); Call("CenterOnWorldPoint", Pos("N00_Camp")); });
        Step(0.4f, () => Call("ZoomTo", 99f, null));
        Step(1.8f, () => { Report("max zoom camp"); Shot("05_camp_max"); });
        Step(0.2f, () => Call("CenterOnWorldPoint", Pos("World/Landscape/Crossings/Lower crossing/Bridge")));
        Step(0.8f, () => Shot("06_lower_crossing_max"));
        Step(0.2f, () => Call("CenterOnWorldPoint", Pos("World/Landscape/Crossings/Upper crossing/Bridge")));
        Step(0.8f, () => Shot("07_upper_crossing_max"));
        Step(0.2f, () => { Call("PanByScreenDelta", new Vector2(9000f, 9000f)); });
        Step(0.8f, () => { Report("max zoom pan limit"); Shot("08_max_zoom_pan_limit"); });
        var nodes = new List<string>();
        foreach (Transform t in GameObject.Find("Graph/Nodes").transform) nodes.Add(t.name);
        foreach (string n in nodes)
        {
            string name = n;
            Step(0.2f, () => { Call("ResetView"); Call("CenterOnWorldPoint", Pos("Graph/Nodes/" + name)); Call("ZoomTo", 99f, null); });
            Step(1.8f, () => { Ray(name); Shot("site_" + name); });
        }
        Step(0.3f, () => { Call("ResetView"); });
        Step(1.0f, () => { foreach (string n in nodes) Ray(n); Report("final 1:1"); });
        Step(0.5f, Finish);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Step(float delay, Action a)
    {
        clock += delay;
        steps.Add((clock, a));
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; Write(); return; }
        double now = EditorApplication.timeSinceStartup;
        while (steps.Count > 0 && steps[0].at <= now)
        {
            var s = steps[0];
            steps.RemoveAt(0);
            try { s.act(); } catch (Exception e) { log.AppendLine("step error " + e.Message); }
        }
    }

    private static void Report(string label)
    {
        Camera c = Camera.main;
        log.AppendLine(label + ": ortho " + c.orthographicSize.ToString("0.0") + " zoom " + Prop("ZoomLevel").ToString("0.00") +
                       " cam " + c.transform.position.ToString("0") + " screen " + Screen.width + "x" + Screen.height);
    }

    private static void Ray(string node)
    {
        Camera c = Camera.main;
        GameObject go = GameObject.Find("Graph/Nodes/" + node);
        Vector3 centre = go.TryGetComponent(out CapsuleCollider capsule)
            ? go.transform.TransformPoint(capsule.center)
            : go.transform.position;
        Vector3 sp = c.WorldToScreenPoint(centre);
        bool onScreen = sp.z > 0 && c.pixelRect.Contains(sp);
        string hit = "nothing";
        if (Physics.Raycast(c.ScreenPointToRay(sp), out RaycastHit h, 5000f)) hit = h.collider.name;
        log.AppendLine("  click " + node + (onScreen ? "" : " (off screen)") + " -> " + hit + (hit == node ? " OK" : " BLOCKED"));
    }

    private static void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png"));
        log.AppendLine("shot " + name);
    }

    private static void Finish()
    {
        Write();
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void Write() => File.WriteAllText(Path.Combine(CedarValleyBuilder.OutDir, "verify.txt"), log.ToString());
}
