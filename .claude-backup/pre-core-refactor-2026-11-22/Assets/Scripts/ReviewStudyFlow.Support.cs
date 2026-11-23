using System;
using System.Collections;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class ReviewStudyFlow
{
    private const string SupportUrl = "http://127.0.0.1:8787/v1/support";
    private const string MockOnlyHeader = "X-CLSR-Mock-Only";
    private static readonly string[] ReasonCodes =
        { "branch_count", "difficulty", "access", "previous_search", "days_left", "logistics", "other", null };

    private string interventionLabel;
    private bool showedSupportMessage;
    private UnityWebRequest supportRequest;

    [Serializable]
    private sealed class SupportReply
    {
        public string version, condition, provider, message;
    }

    private void StartIntervention()
    {
        showedSupportMessage = false;
        if (session.Condition == 1)
        {
            ShowIntervention("Neutral message", BuildSampleMessage(), false);
            return;
        }
        if (!session.UseMockBackend)
        {
            ShowIntervention("Sample AI message (offline preview)", BuildSampleMessage(), true);
            return;
        }

        string condition = session.Condition == 2 ? "direct_recommendation" : "constructive_dissent";
        var roots = scenario.CurrentScenario.nodes.Where(n => n.parentNodeId == "Camp").ToArray();
        string[] branchLabels = BranchChoices();
        var judgments = initialResponses.Select(response =>
        {
            int branch = Array.IndexOf(branchLabels, response.Branch);
            int reason = Array.IndexOf(Reasons, response.Reason);
            return new ReviewSupportSnapshot.Judgment
            {
                Branch = branch >= 0 ? roots[branch].nodeId : null,
                Reason = reason >= 0 ? ReasonCodes[reason] : null,
                Plan = response.Plan
            };
        });
        var caches = FindObjectsByType<StorageCampMarker>(FindObjectsSortMode.None)
            .Where(marker => marker.isActiveAndEnabled).Select(marker => marker.NodeId).ToHashSet();
        string payload = ReviewSupportSnapshot.Build(condition, scenario, game, provisional, judgments, caches);
        Go(Phase.WaitForSupport); // Leave the submit phase before sending; no second click can resend.
        StartCoroutine(RequestSupport(condition, payload));
    }

    private IEnumerator RequestSupport(string condition, string payload)
    {
        var request = new UnityWebRequest(SupportUrl, "POST");
        supportRequest = request;
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader(MockOnlyHeader, "1");
        request.timeout = 5;
        request.redirectLimit = 0;
        UnityWebRequestAsyncOperation pending = null;
        try { pending = request.SendWebRequest(); }
        catch (InvalidOperationException) { /* Network disabled in this editor/build. */ }
        if (pending != null) yield return pending;
        var reply = pending != null && request.result == UnityWebRequest.Result.Success
            ? ParseSupport(request.downloadHandler.text) : null;
        request.Dispose();
        supportRequest = null;

        if (reply != null && reply.version == ReviewSupportSnapshot.Version &&
            reply.condition == condition && reply.provider == "mock" &&
            !string.IsNullOrWhiteSpace(reply.message))
        {
            ShowIntervention("Mock AI message (local backend)", reply.message, true);
        }
        else
            ShowIntervention("Support unavailable", "No support message was shown. Continue with the team plan.", false);
    }

    private static SupportReply ParseSupport(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 8192) return null;
        try { return JsonUtility.FromJson<SupportReply>(json); }
        catch (ArgumentException) { return null; }
    }

    private void ShowIntervention(string title, string text, bool shown)
    {
        interventionLabel = title;
        message = text;
        showedSupportMessage = shown;
        Timed(Phase.Intervention, 15);
    }

    private void OnDestroy()
    {
        if (liteArrowMask != null) Destroy(liteArrowMask);
        if (liteArrowEtch != null) Destroy(liteArrowEtch);
        if (liteLogisticsMask != null) Destroy(liteLogisticsMask);
        if (liteEdgeShade != null) Destroy(liteEdgeShade);
        if (tooltipFrame != null) Destroy(tooltipFrame);
        if (dispatchFrame != null) Destroy(dispatchFrame);
        if (tooltipPointer != null) Destroy(tooltipPointer);
        if (dispatchShell != null) Destroy(dispatchShell);
        if (dispatchTab != null) Destroy(dispatchTab);
        if (dispatchPlaque != null) Destroy(dispatchPlaque);
        if (supportRequest != null)
        {
            // Unity destroys this screen on scene change. Stop the now-irrelevant request.
            supportRequest.Abort();
            supportRequest.Dispose();
            supportRequest = null;
        }
    }
}
