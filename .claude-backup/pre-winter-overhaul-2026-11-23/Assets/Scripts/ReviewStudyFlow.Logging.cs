using System;
using System.Collections.Generic;
using System.IO;
using CLSR.Simulation;
using UnityEngine;

// Research event log. Records phases, timing, plans, outcomes and the exact
// intervention text shown, with its provider metadata and failure status.
// It never records names, private free-text answers or survey choices.
// Writing to disk is off by default and only offered in the Editor or a
// native development build; the WebGL preview keeps the log in memory only.
public sealed partial class ReviewStudyFlow
{
    private double formOpenedAt, interventionShownAt;

    private static bool LocalLogAvailable =>
        Application.isEditor || (Debug.isDebugBuild && Application.platform != RuntimePlatform.WebGLPlayer);

    private static double SessionClock => session == null ? 0 : Time.realtimeSinceStartupAsDouble - session.StartedAt;

    private static void LogEvent(string type, params object[] fields)
    {
        if (session?.Log == null) return;
        try { session.Log.Add(SessionClock, type, fields); }
        catch (ArgumentException exception) { Debug.LogWarning("Event log: " + exception.Message); }
    }

    private static string ScenarioLabel(ScenarioController controller) =>
        controller != null && controller.CurrentScenario != null ? controller.CurrentScenario.scenarioName : "";

    private static List<Leg> Legs(IEnumerable<ExpeditionAssignment> plan) => RescueGameController.ToLegs(plan);

    private void LogSceneStart()
    {
        if (session == null) return;
        LogEvent("scenario_start", "scenario", ScenarioLabel(scenario),
            "scenario_index", session.ScenarioIndex, "practice", session.ScenarioIndex < 0,
            "total_days", game != null ? game.TotalDays : 0);
    }

    private void LogPhaseChange(Phase from, Phase to)
    {
        if (session == null || from == to) return;
        LogEvent("phase", "from", from.ToString(), "to", to.ToString(),
            "day", game != null && scenario != null && scenario.IsReady ? game.CurrentDay : 0,
            "scenario", ScenarioLabel(scenario));
        if (to == Phase.Initial || to == Phase.PreOutcome || to == Phase.PostOutcome || to == Phase.Reasoning)
        {
            formOpenedAt = SessionClock;
            LogEvent("form_open", "form", to.ToString(), "player_slot", player + 1);
        }
    }

    private void LogFormComplete()
    {
        LogEvent("form_complete", "form", form.ToString(), "player_slot", player + 1,
            "seconds", Math.Round(SessionClock - formOpenedAt, 3));
    }

    private void LogDayResolved(DayOutcome outcome)
    {
        if (session == null || outcome == null) return;
        var plan = new List<Leg>();
        var blocked = new List<string>();
        foreach (ExpeditionOutcome expedition in outcome.Expeditions)
        {
            plan.Add(new Leg(expedition.Target, expedition.Points));
            if (expedition.StoppedAt != null) blocked.Add(expedition.Target + "@" + expedition.StoppedAt);
        }
        LogEvent("day_resolved", "scenario", ScenarioLabel(scenario), "day", outcome.Day,
            "plan", (IReadOnlyList<Leg>)plan, "points_spent", outcome.PointsSpent,
            "rescued", outcome.Rescued, "total_rescued", game.TotalRescued,
            "blocked", string.Join(";", blocked), "points_arriving", outcome.PointsArrived,
            "game_over", outcome.GameOver);
    }

    private void LogIntervention(string status, string provider, string model, string promptVersion,
        double latencySeconds, string failure)
    {
        interventionShownAt = SessionClock;
        LogEvent("intervention_shown", "condition", StudyProtocol.ConditionCode(session.Condition),
            "scenario", ScenarioLabel(scenario), "day", game.CurrentDay, "status", status,
            "label", interventionLabel, "message", message, "counts_as_ai_message", showedSupportMessage,
            "provider", provider, "model", model, "prompt_version", promptVersion,
            "latency_seconds", Math.Round(latencySeconds, 3), "failure", failure,
            "reading_seconds", session.SkipTimers ? 0 : StudyProtocol.InterventionReadingSeconds,
            "timers_skipped", session.SkipTimers);
    }

    private void LogInterventionClosed()
    {
        LogEvent("intervention_closed", "day", game.CurrentDay,
            "seconds_displayed", Math.Round(SessionClock - interventionShownAt, 3));
    }

    /// <summary>Writes the JSON Lines log if the reviewer enabled it. Returns the path or null.</summary>
    private static string WriteEventLog()
    {
        if (session?.Log == null || !session.WriteLog || !LocalLogAvailable) return null;
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "clsr-event-logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, session.Log.SessionId + ".jsonl");
            File.WriteAllText(path, session.Log.ToJsonLines());
            return path;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogWarning("Could not write the event log: " + exception.Message);
            return null;
        }
    }
}
