using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class ReviewStudyFlow
{
    private static readonly string[] Agreement =
        { "Not at all", "A little", "Moderately", "Very much", "Completely" };
    private static readonly string[] Reasons = {
        "Number of survivors in a branch", "Revealed logistics requirements",
        "Trail connections and accessibility", "Information from previous expeditions",
        "Remaining rescue days", "Available logistics points", "Other / Uncertain",
        "Prefer not to answer"
    };
    private static readonly string[] Reasoning = {
        "What was your overall strategy in this scenario?",
        "Which decision mattered most, and why?",
        "How did the trail connections, logistics requirements, or uncertainty affect your decisions?",
        "What would you do differently if you played this scenario again, and why?"
    };

    private sealed class InitialResponse
    {
        public readonly string Branch;
        public readonly string Reason;
        public readonly List<ExpeditionAssignment> Plan;

        public InitialResponse(string branch, string reason, List<ExpeditionAssignment> plan)
        {
            Branch = branch;
            Reason = reason;
            Plan = plan;
        }
    }

    private void DrawPreOutcome()
    {
        Choice("agreement", "How much did you agree with the team's final decisions this round?", Agreement);
        Choice("addressed", "To what extent did the team discuss and respond to concerns you raised this round?",
            Agreement.Append("Not applicable — I did not raise any concerns").ToArray());
        Choice("confidence", "How confident are you in the team's final decisions this round?", new[] {
            "Not at all confident", "Slightly confident", "Moderately confident",
            "Very confident", "Extremely confident"
        });
    }

    private void DrawPostOutcome()
    {
        Choice("change", "Compared with your initial judgment, how much did your preferred plan change?",
            new[] { "Not at all", "A little", "Moderately", "A lot", "Completely" });
        var influences = new List<string> { "Initial judgment", "Team discussion", "Map and observations",
            "Survivor information and logistics requirements" };
        if (session.Condition != 1) influences.Add("AI message");
        influences.Add("Other");
        Choice("influence", "What most influenced your final judgment?", influences.ToArray());
        Written("reflection", "Briefly explain what changed your mind, or why your view stayed the same.", 600);
        if (session.Condition == 1) return;

        Choice("usefulness", "How useful was the AI message for your decision-making?",
            new[] { "Not at all", "Slightly", "Moderately", "Very", "Extremely", "Unable to judge" });
        Choice("reliance", "How much did you rely on the AI message when forming your final judgment?", Agreement);
    }

    private string Value(string key) => answers.TryGetValue(key, out var answer) ? answer : "";

    private void Choice(string key, string question, string[] options)
    {
        Label(question);
        string current = Value(key);
        if (Button(current == "" ? "Choose an answer (optional)" : current))
            openChoice = openChoice == key ? null : key;
        if (openChoice == key)
        {
            foreach (var option in options.Prepend("Skip / no response"))
            {
                if (!Button(option)) continue;
                answers[key] = option == "Skip / no response" ? "" : option;
                openChoice = null;
            }
        }
        GUILayout.Space(6);
    }

    private void Written(string key, string question, int limit)
    {
        Label(question);
        GUI.SetNextControlName(key);
        answers[key] = GUILayout.TextArea(Value(key), limit, textAreaStyle, GUILayout.MinHeight(78));
        GUILayout.Space(6);
    }

    private string BuildSampleMessage()
    {
        if (session.Condition == 1)
            return "Please continue discussing your team's rescue plan. No additional information or recommendation is provided.";
        if (Mathf.Min(game.DailyDeploymentCap, game.LogisticsRemaining) == 0)
        {
            return session.Condition == 2
                ? "I recommend sending no expeditions: no logistics points remain available today."
                : "No points are available today. Which earlier assumption about the budget would you reconsider?";
        }
        if (session.Condition == 3)
        {
            var branches = initialResponses.Select(r => r.Branch)
                .Where(b => b != "" && b != "Not sure").Distinct().ToArray();
            if (branches.Length > 1)
                return $"Initial proposals considered both {branches[0]} and {branches[1]}. What evidence would make one route a better use of today’s points than the other?";
            bool exploredAlternative = initialResponses.Any(r =>
                r.Reason == "Information from previous expeditions" ||
                r.Plan.Any(t => provisional.All(p => p.targetNodeId != t.targetNodeId)));
            return exploredAlternative
                ? "At least one initial idea emphasized gathering information or a route not in the current plan. What could that option reveal, and would it change tomorrow’s decision?"
                : "What are you assuming about the route’s difficulty or survivor distribution? Would scouting, committing more points, or saving points change your plan?";
        }

        if (provisional.Count > 0)
            return "I recommend " + Describe(provisional) +
                ". This concentrates on the team’s selected destinations and fits the visible budget. Unrevealed thresholds could still block the route.";

        var report = scenario.CurrentScenario.branches.OrderByDescending(r => r.reportedSurvivors).FirstOrDefault();
        string root = report?.rootNodeId ??
            scenario.CurrentScenario.nodes.First(n => n.parentNodeId == "Camp").nodeId;
        return $"I recommend sending 1 LP toward {root} to test its access threshold. " +
            "That uses little of the remaining pool, but it does not guarantee a rescue.";
    }
}
