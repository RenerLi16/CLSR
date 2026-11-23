using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Before/after WebGL builds of Cedar Valley for performance comparison, plus a tiny local
// web server (editor process, localhost only) so the builds can be opened in a browser.
internal static class CedarWebBuild
{
    private static string Root => Path.Combine(Directory.GetCurrentDirectory(), "Builds");

    [MenuItem("Tools/Cedar Valley/Perf/Build WebGL (tag in buildtag.txt)", false, 50)]
    private static void BuildMenu() => CedarValleyBuilder.Run("webbuild.txt", Build);

    private static void Build()
    {
        string tagFile = Path.Combine(CedarValleyBuilder.OutDir, "buildtag.txt");
        string tag = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "build";
        string path = Path.Combine(Root, "CedarPerf-" + tag);
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { CedarValleyBuilder.ScenePath },
            locationPathName = path,
            target = BuildTarget.WebGL,
            options = BuildOptions.Development
        };
        BuildReport r = BuildPipeline.BuildPlayer(opts);
        CedarValleyBuilder.Info("build " + tag + ": " + r.summary.result + ", " + (r.summary.totalSize / 1048576f).ToString("0.0") +
                                " MB, " + r.summary.totalTime.TotalSeconds.ToString("0") + " s, errors " + r.summary.totalErrors + " -> " + path);
    }

    private static HttpListener listener;

    [MenuItem("Tools/Cedar Valley/Perf/Serve Builds on localhost:8123", false, 51)]
    private static void Serve()
    {
        if (listener != null && listener.IsListening) return;
        listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8123/");
        listener.Start();
        string root = Root;
        new Thread(() =>
        {
            while (listener != null && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { break; }
                try { Respond(ctx, root); } catch { }
            }
        }) { IsBackground = true }.Start();
        CedarValleyBuilder.Run("serve.txt", () => CedarValleyBuilder.Info("serving " + root + " at http://localhost:8123/"));
    }

    private static void Respond(HttpListenerContext ctx, string root)
    {
        string rel = Uri.UnescapeDataString(ctx.Request.Url.AbsolutePath).TrimStart('/');
        if (rel.EndsWith("/") || rel.Length == 0) rel += "index.html";
        string file = Path.GetFullPath(Path.Combine(root, rel));
        if (!file.StartsWith(Path.GetFullPath(root)) || !File.Exists(file))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }
        string name = file;
        if (name.EndsWith(".gz")) { ctx.Response.AddHeader("Content-Encoding", "gzip"); name = name.Substring(0, name.Length - 3); }
        else if (name.EndsWith(".br")) { ctx.Response.AddHeader("Content-Encoding", "br"); name = name.Substring(0, name.Length - 3); }
        string ext = Path.GetExtension(name).ToLowerInvariant();
        ctx.Response.ContentType = ext == ".html" ? "text/html" : ext == ".js" ? "application/javascript" : ext == ".wasm" ? "application/wasm" :
            ext == ".css" ? "text/css" : ext == ".png" ? "image/png" : ext == ".json" ? "application/json" : ext == ".ico" ? "image/x-icon" : "application/octet-stream";
        ctx.Response.AddHeader("Cache-Control", "no-store");
        byte[] data = File.ReadAllBytes(file);
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }
}
