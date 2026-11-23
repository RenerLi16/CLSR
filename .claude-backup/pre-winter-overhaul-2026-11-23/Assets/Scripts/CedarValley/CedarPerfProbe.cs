using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Measurement-only probe for Cedar Valley rendering. Inert unless the page URL contains
/// "perf=1" (web builds) or it is started from the editor. It steps the existing map camera
/// through fixed views (1:1 overview, mid zoom and close zoom over a sample forest area) and
/// logs triangles, draw calls, set-pass calls, main-thread time and frame interval.
/// It never touches game state.
/// </summary>
public class CedarPerfProbe : MonoBehaviour
{
    public static readonly Vector3 SampleArea = new Vector3(-68f, 12f, 138f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoStart()
    {
        if (!Debug.isDebugBuild ||
            !System.Uri.TryCreate(Application.absoluteURL, System.UriKind.Absolute, out var uri) ||
            System.Array.IndexOf(uri.Query.TrimStart('?').Split('&'), "perf=1") < 0) return;
        Launch(null);
    }

    public static void Launch(System.Action<string> report)
    {
        if (SceneManager.GetActiveScene().name != "CedarValley") return;
        var go = new GameObject("CedarPerfProbe");
        var probe = go.AddComponent<CedarPerfProbe>();
        probe.report = report;
    }

    private System.Action<string> report;

    private struct View { public string Name; public float Zoom; public bool Centre; }

    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(5f);
        var framing = Camera.main != null ? Camera.main.GetComponent<GraphCameraFraming>() : null;
        if (framing == null) { Emit("CEDAR_PERF {\"error\":\"no camera framing\"}"); yield break; }
        var views = new List<View>
        {
            new View { Name = "overview_1x1", Zoom = 0f, Centre = false },
            new View { Name = "mid_zoom6", Zoom = 6f, Centre = true },
            new View { Name = "close_zoom11", Zoom = 11f, Centre = true },
        };
        foreach (View v in views)
        {
            framing.ResetView();
            yield return null;
            if (v.Centre) framing.CenterOnWorldPoint(SampleArea);
            yield return null;
            if (v.Zoom > 0f) framing.ZoomTo(v.Zoom, null);
            yield return new WaitForSecondsRealtime(3f);
            yield return Measure(v.Name, Camera.main.orthographicSize);
            yield return new WaitForSecondsRealtime(0.5f);
        }
        framing.ResetView();
        if (FindAnyObjectByType<MapOverlayScaler>() != null)
            yield return CheckMarkers(framing);
        framing.ResetView();
        Emit("CEDAR_PERF {\"done\":true}");
    }

    private IEnumerator Measure(string name, float ortho)
    {
        using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        using var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        const int frames = 240;
        var dt = new List<float>(frames);
        double t = 0, d = 0, b = 0, s = 0, m = 0;
        int counted = 0;
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            dt.Add(Time.unscaledDeltaTime * 1000f);
            if (i < 10) continue;
            t += tris.LastValue; d += draws.LastValue; b += batches.LastValue; s += setpass.LastValue;
            m += main.LastValue * 1e-6;
            counted++;
        }
        dt.Sort();
        float mean = 0f;
        foreach (float x in dt) mean += x;
        mean /= dt.Count;
        int tier = FindAnyObjectByType<ForestDetailTiers>()?.Tier ?? -1;
        string json = "{\"view\":\"" + name + "\",\"ortho\":" + Num(ortho, "0.0") +
                      ",\"screen\":\"" + Screen.width + "x" + Screen.height + "\"" +
                      ",\"forestTier\":" + tier +
                      ",\"triangles\":" + Num(t / counted, "0") +
                      ",\"drawCalls\":" + Num(d / counted, "0") +
                      ",\"batches\":" + Num(b / counted, "0") +
                      ",\"setPass\":" + Num(s / counted, "0") +
                      ",\"mainThreadMs\":" + Num(m / counted, "0.00") +
                      ",\"frameMsMean\":" + Num(mean, "0.00") +
                      ",\"frameMsP95\":" + Num(dt[(int)(dt.Count * 0.95f)], "0.00") +
                      ",\"countersValid\":" + (tris.Valid && draws.Valid ? "true" : "false") + "}";
        Emit("CEDAR_PERF " + json);
    }

    private IEnumerator CheckMarkers(GraphCameraFraming framing)
    {
        Transform visualRoot = GameObject.Find("Map marker visuals")?.transform;
        CapsuleCollider site = GameObject.Find("Graph/Nodes/Node_W1")?.GetComponent<CapsuleCollider>();
        Transform marker = site != null ? visualRoot?.Find(site.name + " marker") : null;
        Camera cam = Camera.main;
        if (marker == null || cam == null)
        {
            Emit("CEDAR_PERF {\"error\":\"missing W1 marker\"}");
            yield break;
        }

        float MarkerPixels() => marker.lossyScale.x * cam.pixelHeight / (2f * cam.orthographicSize);
        bool TryHit(CapsuleCollider expected) =>
            Physics.Raycast(cam.ScreenPointToRay(cam.WorldToScreenPoint(expected.transform.TransformPoint(expected.center))),
                out var hit, cam.farClipPlane) && hit.collider == expected;
        yield return new WaitForSecondsRealtime(0.35f);
        float initial = MarkerPixels();
        int atOverview = 0, hitOverview = 0;
        foreach (RescueNode node in FindObjectsByType<RescueNode>(FindObjectsSortMode.None))
        {
            if (!node.TryGetComponent(out CapsuleCollider capsule)) continue;
            Vector3 inside = cam.WorldToScreenPoint(capsule.transform.TransformPoint(capsule.center));
            if (inside.z <= 0 || !cam.pixelRect.Contains(inside)) continue;
            atOverview++;
            if (TryHit(capsule)) hitOverview++;
        }
        framing.CenterOnWorldPoint(site.transform.position);
        yield return null;
        framing.ZoomTo(11f, null);
        yield return new WaitForSecondsRealtime(1.3f);
        float close = MarkerPixels();
        bool hitClose = TryHit(site);
        Emit("CEDAR_PERF {\"view\":\"marker_check\",\"overviewHit\":" + hitOverview + ",\"overviewVisible\":" + atOverview +
             ",\"closeHit\":" + (hitClose ? "true" : "false") + ",\"overviewMarkerPx\":" + Num(initial, "0.00") +
             ",\"closeMarkerPx\":" + Num(close, "0.00") + "}");
    }

    private static string Num(double n, string format) => n.ToString(format, CultureInfo.InvariantCulture);

    private void Emit(string line)
    {
        Debug.Log(line);
        report?.Invoke(line);
    }
}
