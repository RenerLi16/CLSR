using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor entry points for checking the Cedar Valley telescope phase:
///  * Run Self-Test In Play Mode: plays a full three-day session (nine private turns) through
///    ReviewStudyFlow's own actions, then frames every site at each supported resolution.
///    Report and captures: Temp/CedarValleyBuild/telescope/selftest-*/.
///  * Build WebGL: the beta's build scenes as a release build and as a development build (the
///    development build runs the same self-test with ?selftest=telescope). Serve with
///    Tools > Cedar Valley > Perf > Serve Builds on localhost:8123.
/// </summary>
internal static class CedarTelescopeVerify
{
    private const string Flag = "CedarTelescopeVerify.Pending";
    private static string runFolder;

    [MenuItem("Tools/Cedar Valley/Telescope/Run Self-Test In Play Mode", false, 62)]
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

    private static void Begin()
    {
        Application.runInBackground = true; // keep ticking while the editor window is not focused
        runFolder = Path.Combine(CedarTelescopeBuilder.Out, "selftest-" + DateTime.Now.ToString("HHmmss"));
        Directory.CreateDirectory(runFolder);
        File.WriteAllText(Path.Combine(CedarTelescopeBuilder.Out, "selftest-latest.txt"), runFolder + "\nrunning\n");
        ReviewStudyFlow.RequestTelescopeSelfTest(runFolder);
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Poll; return; }
        if (!ReviewStudyFlow.SelfTestFinished) return;
        EditorApplication.update -= Poll;
        File.WriteAllText(Path.Combine(CedarTelescopeBuilder.Out, "selftest-latest.txt"), runFolder + "\nfinished\n");
        EditorApplication.ExitPlaymode();
    }

    [MenuItem("Tools/Cedar Valley/Telescope/Build WebGL (release and development)", false, 63)]
    private static void BuildWeb() => CedarTelescopeBuilder.Run("webbuild.txt", () =>
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string root = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        foreach ((string name, BuildOptions options) in new[] { ("TelescopeWeb", BuildOptions.None), ("TelescopeWeb-dev", BuildOptions.Development) })
        {
            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(root, name),
                target = BuildTarget.WebGL,
                options = options
            };
            BuildReport r = BuildPipeline.BuildPlayer(opts);
            string line = $"{name}: {r.summary.result}, {r.summary.totalSize / 1048576f:0.0} MB, {r.summary.totalTime.TotalSeconds:0} s, " +
                          $"errors {r.summary.totalErrors}, warnings {r.summary.totalWarnings}, scenes {string.Join(", ", scenes)}";
            File.AppendAllText(Path.Combine(CedarTelescopeBuilder.Out, "webbuild-summary.txt"), DateTime.Now.ToString("HH:mm:ss ") + line + "\n");
            foreach (BuildStep step in r.steps)
                foreach (BuildStepMessage m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        File.AppendAllText(Path.Combine(CedarTelescopeBuilder.Out, "webbuild-summary.txt"), "  " + m.type + ": " + m.content + "\n");
        }
    });
}
