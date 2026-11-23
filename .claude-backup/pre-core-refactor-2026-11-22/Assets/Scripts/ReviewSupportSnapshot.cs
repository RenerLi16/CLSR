using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Explicit public projection: never serialize a scenario, MonoBehaviour, or answer form.
public static class ReviewSupportSnapshot
{
    public const string Version = "rescue-support-v2";

    public sealed class Judgment
    {
        public string Branch, Reason;
        public IReadOnlyList<ExpeditionAssignment> Plan;
    }

    public static string Build(string condition, ScenarioController scenario,
        RescueGameController game, IReadOnlyList<ExpeditionAssignment> provisional,
        IEnumerable<Judgment> judgments, ISet<string> visibleCaches)
    {
        var map = scenario.CurrentScenario;
        var sites = map.nodes.Where(node => node.nodeId != "Camp").Select(node =>
        {
            string id = node.nodeId;
            var report = node.parentNodeId == "Camp"
                ? map.branches.Find(r => r.rootNodeId == id) : null;
            bool knowsSurvivors = scenario.AreSurvivorCountsKnown(id);
            int? branchRemaining = null;
            if (scenario.TryGetDiscoveredBranchCount(id, out int count))
                branchRemaining = count;
            else if (node.parentNodeId == "Camp" && map.branches.Count == 0)
                branchRemaining = scenario.CountRemainingBranchSurvivors(id);

            return $"{{\"id\":{Quote(id)},\"parent\":{Quote(node.parentNodeId)}," +
                $"\"known_difficulty\":{Number(scenario.IsDifficultyKnown(id) ? node.difficulty : null)}," +
                $"\"initial_branch_report\":{Number(report?.reportedSurvivors)}," +
                $"\"initial_branch_note\":{Quote(string.IsNullOrEmpty(report?.note) ? null : report.note)}," +
                $"\"known_branch_remaining\":{Number(branchRemaining)}," +
                $"\"known_remaining_here\":{Number(knowsSurvivors ? scenario.CountRemainingHere(id) : null)}," +
                $"\"known_remaining_beyond\":{Number(knowsSurvivors ? scenario.CountRemainingBeyond(id) : null)}," +
                $"\"available_cache_reward\":{Number(visibleCaches.Contains(id) && !game.IsStorageCampClaimed(id) ? node.logisticsReward : null)}}}";
        });

        return $"{{\"version\":{Quote(Version)},\"condition\":{Quote(condition)},\"context\":{{" +
            $"\"day\":{Number(game.CurrentDay)},\"total_days\":{Number(game.TotalDays)}," +
            $"\"points_available\":{Number(game.LogisticsRemaining)},\"daily_cap\":{Number(game.DailyDeploymentCap)}," +
            $"\"max_expeditions\":{Number(game.MaxExpeditionsPerDay)},\"sites\":[{string.Join(",", sites)}]," +
            $"\"released_route_reports\":[{string.Join(",", scenario.ReleasedRouteReports.Select(Quote))}]," +
            $"\"provisional_plan\":{Plan(provisional)},\"initial_judgments\":[" +
            string.Join(",", judgments.Select(j => $"{{\"branch\":{Quote(j.Branch)}," +
                $"\"reason\":{Quote(j.Reason)},\"plan\":{Plan(j.Plan)}}}")) + "]}}";
    }

    private static string Plan(IEnumerable<ExpeditionAssignment> plan) => "[" +
        string.Join(",", plan.Select(leg =>
            $"{{\"target\":{Quote(leg.targetNodeId)},\"points\":{Number(leg.assignedGroups)}}}")) + "]";

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";

    private static string Quote(string value)
    {
        if (value == null) return "null";
        var json = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c == '\\' || c == '"') json.Append('\\').Append(c);
            else if (c < ' ' || char.IsSurrogate(c))
                json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else json.Append(c);
        }
        return json.Append('"').ToString();
    }
}
