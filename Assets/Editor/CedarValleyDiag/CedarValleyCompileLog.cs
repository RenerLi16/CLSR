// Editor-only: mirrors script compile results into Temp/CedarValleyBuild/compile.txt
// so the Cedar Valley landscape builder can be iterated on without the console.
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

[InitializeOnLoad]
internal static class CedarValleyCompileLog
{
    private static readonly StringBuilder Log = new StringBuilder();
    private static string Dir => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "CedarValleyBuild");

    static CedarValleyCompileLog()
    {
        CompilationPipeline.compilationStarted -= OnStarted;
        CompilationPipeline.compilationStarted += OnStarted;
        CompilationPipeline.assemblyCompilationFinished -= OnAssembly;
        CompilationPipeline.assemblyCompilationFinished += OnAssembly;
        CompilationPipeline.compilationFinished -= OnFinished;
        CompilationPipeline.compilationFinished += OnFinished;
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "domain.txt"), System.DateTime.Now.ToString("HH:mm:ss") + " domain loaded\n");
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    // Lets the landscape builder be driven while the editor window is in the background:
    // Temp/CedarValleyBuild/cmd.txt holds "refresh" or "menu:<menu item path>" (one per line).
    private static double nextPoll;

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        string cmd = Path.Combine(Dir, "cmd.txt");
        if (!File.Exists(cmd) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string[] lines = File.ReadAllLines(cmd);
        File.Delete(cmd);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            string result;
            if (line == "refresh") { AssetDatabase.Refresh(); result = "refreshed"; }
            else if (line.StartsWith("menu:")) result = EditorApplication.ExecuteMenuItem(line.Substring(5)) ? "ran" : "menu not found";
            else continue;
            File.AppendAllText(Path.Combine(Dir, "ack.txt"), System.DateTime.Now.ToString("HH:mm:ss") + " " + line + " -> " + result + "\n");
        }
    }

    private static void OnStarted(object _)
    {
        Log.Clear();
        Log.AppendLine(System.DateTime.Now.ToString("HH:mm:ss") + " compile started");
    }

    private static void OnAssembly(string assembly, CompilerMessage[] messages)
    {
        foreach (CompilerMessage m in messages)
        {
            if (m.type == CompilerMessageType.Error || m.message.Contains("CedarValley"))
                Log.AppendLine(m.type + " " + Path.GetFileName(assembly) + ": " + m.message);
        }
    }

    private static void OnFinished(object _)
    {
        Log.AppendLine(System.DateTime.Now.ToString("HH:mm:ss") + " compile finished");
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, "compile.txt"), Log.ToString());
    }
}
