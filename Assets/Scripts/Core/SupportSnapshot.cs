using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CLSR.Simulation
{
    /// <summary>One anonymous, structured initial judgment. No names, confidence or free text.</summary>
    public sealed class SupportJudgment
    {
        public string Branch;
        public string Reason;
        public IReadOnlyList<Leg> Plan;
    }

    /// <summary>
    /// Builds the rescue-support-v2 request body from the player-visible view
    /// only. Both AI conditions receive exactly this projection; only the
    /// condition string differs. The builder cannot see hidden scenario values.
    /// </summary>
    public static class SupportSnapshot
    {
        public const string Version = "rescue-support-v2";

        public static string Build(string condition, PublicView view, IReadOnlyList<Leg> provisional,
            IEnumerable<SupportJudgment> judgments)
        {
            var json = new StringBuilder(4096);
            json.Append("{\"version\":").Append(Quote(Version))
                .Append(",\"condition\":").Append(Quote(condition))
                .Append(",\"context\":{")
                .Append("\"day\":").Append(Number(view.Day))
                .Append(",\"total_days\":").Append(Number(view.TotalDays))
                .Append(",\"points_available\":").Append(Number(view.Pool))
                .Append(",\"daily_cap\":").Append(Number(view.DailyCap))
                .Append(",\"max_expeditions\":").Append(Number(view.MaxExpeditions))
                .Append(view.CumulativeCosts ? ",\"cumulative_costs\":true" : "")
                .Append(",\"sites\":[");

            bool first = true;
            foreach (PublicNode node in view.Nodes)
            {
                if (!first) json.Append(',');
                first = false;
                json.Append("{\"id\":").Append(Quote(node.Id))
                    .Append(",\"parent\":").Append(Quote(node.ParentId))
                    .Append(",\"known_difficulty\":").Append(Number(node.KnownDifficulty))
                    .Append(",\"initial_branch_report\":").Append(Number(node.InitialBranchReport))
                    .Append(",\"initial_branch_note\":")
                    .Append(Quote(string.IsNullOrEmpty(node.BranchNote) ? null : node.BranchNote))
                    .Append(",\"known_branch_remaining\":").Append(Number(node.KnownBranchRemaining))
                    .Append(",\"known_remaining_here\":").Append(Number(node.KnownRemainingHere))
                    .Append(",\"known_remaining_beyond\":").Append(Number(node.KnownRemainingBeyond))
                    .Append(",\"available_cache_reward\":").Append(Number(node.AvailableCacheReward))
                    .Append('}');
            }

            json.Append("],\"released_route_reports\":[");
            for (int i = 0; i < view.ReleasedReports.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Quote(view.ReleasedReports[i]));
            }
            json.Append("],\"provisional_plan\":").Append(Plan(provisional)).Append(",\"initial_judgments\":[");
            first = true;
            if (judgments != null)
            {
                foreach (SupportJudgment judgment in judgments)
                {
                    if (!first) json.Append(',');
                    first = false;
                    json.Append("{\"branch\":").Append(Quote(judgment.Branch))
                        .Append(",\"reason\":").Append(Quote(judgment.Reason))
                        .Append(",\"plan\":").Append(Plan(judgment.Plan)).Append('}');
                }
            }
            return json.Append("]}}").ToString();
        }

        private static string Plan(IReadOnlyList<Leg> plan)
        {
            var json = new StringBuilder("[");
            if (plan != null)
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    json.Append("{\"target\":").Append(Quote(plan[i].Target))
                        .Append(",\"points\":").Append(Number(plan[i].Points)).Append('}');
                }
            }
            return json.Append(']').ToString();
        }

        private static string Number(int? value) =>
            value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";

        public static string Quote(string value)
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
}
