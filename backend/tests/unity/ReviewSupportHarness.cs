using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

// Installed only in the harness's isolated project copy.
public static class ReviewSupportHarness
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Direct = "direct_recommendation";
    private const string Dissent = "constructive_dissent";
    private static ReviewStudyFlow flow;
    private static IEnumerator request;
    private static UnityWebRequestAsyncOperation pending;
    private static string dissentPayload;
    private static int step;

    public static void Run()
    {
        try
        {
            Check(PlayerSettings.insecureHttpOption == InsecureHttpOption.DevelopmentOnly, "development-only HTTP");
            EditorSceneManager.OpenScene("Assets/Scenes/CedarValley.unity");
            var game = UnityEngine.Object.FindAnyObjectByType<RescueGameController>();
            var scenario = game.GetComponent<ScenarioController>();
            Invoke(scenario, "LoadScenario");
            Invoke(game, "Start");
            Check(scenario.IsReady, "scenario initialized");

            var judgments = new[] {
                new ReviewSupportSnapshot.Judgment { Branch = "W1", Reason = "access", Plan = Plan("W2", 2) },
                new ReviewSupportSnapshot.Judgment { Branch = "N1", Reason = "branch_count", Plan = Plan("N3", 4) },
                new ReviewSupportSnapshot.Judgment { Plan = Array.Empty<ExpeditionAssignment>() }
            };
            var caches = UnityEngine.Object.FindObjectsByType<StorageCampMarker>(FindObjectsSortMode.None)
                .Select(marker => marker.NodeId).ToHashSet();
            var empty = Array.Empty<ExpeditionAssignment>();
            string day1 = ReviewSupportSnapshot.Build(Direct, scenario, game, Plan("W2", 2), judgments, caches);
            Emit("day1", day1);
            Check(game.TrySetPlan(Plan("W2", 1)), "blocked test plan accepted");
            game.ConfirmPlan();
            Emit("day2", ReviewSupportSnapshot.Build(Direct, scenario, game, empty, judgments, caches));
            Check(game.TrySetPlan(Plan("W2", 2)), "cache test plan accepted");
            game.ConfirmPlan();
            dissentPayload = ReviewSupportSnapshot.Build(Dissent, scenario, game, empty, judgments, caches);
            Emit("day3", dissentPayload);
            game.RestartGame();
            Emit("reset", ReviewSupportSnapshot.Build(Direct, scenario, game, empty, judgments, caches));

            flow = game.gameObject.AddComponent<ReviewStudyFlow>();
            var sessionField = typeof(ReviewStudyFlow).GetField("session", BindingFlags.Static | BindingFlags.NonPublic);
            var session = Activator.CreateInstance(sessionField.FieldType, true);
            sessionField.FieldType.GetField("Condition").SetValue(session, 1);
            sessionField.FieldType.GetField("UseMockBackend").SetValue(session, true);
            sessionField.SetValue(null, session);
            Invoke(flow, "Awake");
            Invoke(flow, "StartIntervention"); // Even when enabled, no-AI does not start a request.
            CheckIntervention("Neutral message", false);
            BeginRequest(Direct, day1);
            EditorApplication.update += Poll;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void Poll()
    {
        if (!pending.isDone) return;
        try
        {
            Check(!request.MoveNext(), "request completed in one wait");
            CheckIntervention(step < 2 ? "Mock AI message (local backend)" : "Support unavailable", step < 2);
            if (step < 2)
            {
                string message = (string)flow.GetType().GetField("message", Instance).GetValue(flow);
                Check(message.StartsWith(step == 0 ? "Test recommendation:" : "Test discussion prompt:"),
                    "correct role message reached the Unity flow");
            }
            step++;
            if (step == 1) BeginRequest(Dissent, dissentPayload);
            else if (step == 2) BeginRequest(Direct, "{}"); // Rejected request must not stall or show a sample.
            else
            {
                EditorApplication.update -= Poll;
                Debug.Log("CLSR_SUPPORT_HARNESS_OK");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void BeginRequest(string condition, string payload)
    {
        request = (IEnumerator)Invoke(flow, "RequestSupport", condition, payload);
        Check(request.MoveNext(), "Unity accepted the loopback request");
        pending = (UnityWebRequestAsyncOperation)request.Current;
    }

    private static void CheckIntervention(string label, bool shown)
    {
        var type = flow.GetType();
        Check(type.GetField("phase", Instance).GetValue(flow).ToString() == "Intervention", "intervention phase");
        Check((string)type.GetField("interventionLabel", Instance).GetValue(flow) == label, label);
        Check((bool)type.GetField("showedSupportMessage", Instance).GetValue(flow) == shown, "rating eligibility");
        float remaining = (float)type.GetField("phaseEnds", Instance).GetValue(flow) - Time.unscaledTime;
        Check(remaining > 14 && remaining <= 15, "full reading time after result");
    }

    private static ExpeditionAssignment[] Plan(string target, int points) => new[] {
        new ExpeditionAssignment { targetNodeId = target, assignedGroups = points }
    };

    private static object Invoke(object obj, string method, params object[] args) =>
        obj.GetType().GetMethod(method, Instance).Invoke(obj, args);

    private static void Emit(string label, string json) =>
        Debug.Log("CLSR_SNAPSHOT " + label + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void Fail(Exception exception)
    {
        EditorApplication.update -= Poll;
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }
}
