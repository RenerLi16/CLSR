using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Play-mode capture of the planning board: starts the practice map through the study flow
// (no-AI condition, timers skipped), opens a dispatch, adds one, and runs the practice.
// Everything goes through ReviewStudyFlow's own methods by reflection. Output: Temp/CedarValleyBuild/ui/.
internal static class CedarUiReview
{
    private const string Flag = "CedarUiReview.Pending";
    private static readonly List<(double at, Action act)> steps = new List<(double, Action)>();
    private static double clock;
    private static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
    private static string Dir => Path.Combine(CedarValleyBuilder.OutDir, "ui");
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Cedar Valley/Winter/Capture Planning UI (play mode)", false, 63)]
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

    private static Component Flow()
    {
        Type t = Type.GetType("ReviewStudyFlow, Assembly-CSharp");
        return t != null ? UnityEngine.Object.FindAnyObjectByType(t) as Component : null;
    }

    private static object Call(Component c, string name, params object[] args)
    {
        foreach (MethodInfo m in c.GetType().GetMethods(Any))
            if (m.Name == name && m.GetParameters().Length == args.Length) return m.Invoke(c, args);
        log.AppendLine("missing " + name);
        return null;
    }

    private static void Begin()
    {
        Directory.CreateDirectory(Dir);
        Application.runInBackground = true;
        log.Clear();
        log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + " ui review");
        steps.Clear();
        clock = EditorApplication.timeSinceStartup + 2.5;
        Step(0f, () => Shot("00_setup"));
        Step(0.3f, StartPractice);
        Step(3.0f, () => Shot("01_practice_board"));
        Step(0.2f, () => OpenDraft(0));
        Step(1.0f, () => Shot("02_dispatch_editor"));
        Step(0.2f, () => { Component f = Flow(); var cur = (object)null; int max = (int)Call(f, "LiteDraftMax", cur); Call(f, "SaveLiteDraft", Target(0), cur, max); });
        Step(0.2f, () => OpenDraft(2));
        Step(0.2f, () => { Component f = Flow(); var cur = (object)null; int max = (int)Call(f, "LiteDraftMax", cur); Call(f, "SaveLiteDraft", Target(2), cur, max); });
        Step(1.0f, () => Shot("03_two_routes"));
        Step(0.2f, () => Call(Flow(), "AdvanceLiteBoard"));
        Step(2.0f, () => Shot("04_practice_result"));
        Step(0.5f, Finish);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static string Target(int index)
    {
        var ids = new List<string>();
        foreach (Component n in UnityEngine.Object.FindObjectsByType(Type.GetType("RescueNode, Assembly-CSharp")))
        {
            string id = (string)n.GetType().GetField("nodeId").GetValue(n);
            if (id != "Camp") ids.Add(id);
        }
        ids.Sort();
        return ids.Count == 0 ? null : ids[Mathf.Clamp(index, 0, ids.Count - 1)];
    }

    private static void OpenDraft(int index)
    {
        string id = Target(index);
        foreach (Component n in UnityEngine.Object.FindObjectsByType(Type.GetType("RescueNode, Assembly-CSharp")))
            if ((string)n.GetType().GetField("nodeId").GetValue(n) == id) { Call(Flow(), "OpenLiteDraft", n); log.AppendLine("open " + id); }
    }

    private static void StartPractice()
    {
        Component f = Flow();
        if (f == null) { log.AppendLine("no ReviewStudyFlow"); return; }
        Type t = f.GetType();
        Type sessionType = t.GetNestedType("Session", Any);
        object session = Activator.CreateInstance(sessionType, true);
        sessionType.GetField("Condition").SetValue(session, 1);
        sessionType.GetField("SkipTimers").SetValue(session, true);
        sessionType.GetField("Order").SetValue(session, ((string[])t.GetField("MapScenes", Any).GetValue(null)).Clone());
        sessionType.GetField("StartedAt").SetValue(session, (double)Time.realtimeSinceStartup);
        t.GetField("session", Any).SetValue(null, session);
        Type phase = t.GetNestedType("Phase", Any);
        Call(f, "Load", "PrototypeMap", Enum.Parse(phase, "Practice"));
        log.AppendLine("practice requested");
    }

    private static void Step(float delay, Action a) { clock += delay; steps.Add((clock, a)); }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; Write(); return; }
        double now = EditorApplication.timeSinceStartup;
        while (steps.Count > 0 && steps[0].at <= now)
        {
            var s = steps[0];
            steps.RemoveAt(0);
            try { s.act(); } catch (Exception e) { log.AppendLine("step error " + (e.InnerException ?? e)); }
        }
    }

    private static void Shot(string name)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png"));
        log.AppendLine("shot " + name + " " + Screen.width + "x" + Screen.height);
    }

    private static void Finish()
    {
        Write();
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }

    private static void Write() => File.WriteAllText(Path.Combine(CedarValleyBuilder.OutDir, "uireview.txt"), log.ToString() + "done\n");
}
