using System;
using System.Collections.Generic;
using CLSR.Simulation;

namespace CLSR.Offline
{
    /// <summary>Enumerates every legal plan for a day from public information only.</summary>
    public static class PlanSpace
    {
        public static List<List<Leg>> Enumerate(PublicView view)
        {
            var plans = new List<List<Leg>> { new List<Leg>() };
            int budget = view.TodayBudget;
            if (budget <= 0 || view.BranchReportsPending) return plans;
            var targets = new List<string>();
            foreach (PublicNode node in view.Nodes) targets.Add(node.Id);
            Extend(plans, new List<Leg>(), targets, 0, budget, view.MaxExpeditions);
            return plans;
        }

        private static void Extend(List<List<Leg>> plans, List<Leg> current, List<string> targets, int start,
            int budgetLeft, int teamsLeft)
        {
            if (teamsLeft == 0) return;
            for (int t = start; t < targets.Count; t++)
            {
                for (int points = 1; points <= budgetLeft; points++)
                {
                    current.Add(new Leg(targets[t], points));
                    plans.Add(new List<Leg>(current));
                    Extend(plans, current, targets, t + 1, budgetLeft - points, teamsLeft - 1);
                    current.RemoveAt(current.Count - 1);
                }
            }
        }
    }

    /// <summary>
    /// A compact, fast copy of a scenario for search: survivors, thresholds per
    /// day and caches as arrays, with bitmask state. Used both for full-truth
    /// oracle search and for worlds sampled from a player's belief.
    /// </summary>
    public sealed class World
    {
        public int Count;
        public string[] Ids;
        public int[][] Paths;          // node indices from first node after Camp to the node
        public int[] Survivors;        // remaining at the start of the search horizon
        public int[] Cache;            // claimable reward (0 if none/claimed)
        public int[,] Difficulty;      // [dayIndex, node]; dayIndex 0 = first horizon day
        public int Days;
        public int DailyCap;
        public int MaxExpeditions;
        public bool CumulativeCosts;

        public static World FromTruth(ScenarioSpec spec)
        {
            var ids = new List<string>();
            foreach (NodeSpec node in spec.Nodes) if (node.Id != ScenarioSpec.CampId) ids.Add(node.Id);
            var world = Create(ids, spec.TotalDays, spec.DailyCap, spec.MaxExpeditions, id => spec.PathFromCamp(id).ConvertAll(n => n.Id));
            for (int i = 0; i < world.Count; i++)
            {
                NodeSpec node = spec.Get(ids[i]);
                world.Survivors[i] = node.Survivors;
                world.Cache[i] = node.CacheReward;
                for (int d = 0; d < spec.TotalDays; d++) world.Difficulty[d, i] = node.Difficulty;
            }
            world.CumulativeCosts = spec.CumulativeCosts;
            // Reports change thresholds from the day after they are scheduled.
            foreach (IntelReportSpec report in spec.Reports)
            {
                int index = ids.IndexOf(report.NodeId);
                if (index < 0) continue;
                for (int d = report.AfterDay; d < spec.TotalDays; d++) world.Difficulty[d, index] = report.NewDifficulty;
            }
            return world;
        }

        public static World Create(List<string> ids, int days, int cap, int maxExpeditions,
            Func<string, List<string>> path)
        {
            var world = new World
            {
                Count = ids.Count,
                Ids = ids.ToArray(),
                Paths = new int[ids.Count][],
                Survivors = new int[ids.Count],
                Cache = new int[ids.Count],
                Difficulty = new int[Math.Max(1, days), ids.Count],
                Days = days,
                DailyCap = cap,
                MaxExpeditions = maxExpeditions,
            };
            if (ids.Count > 63) throw new InvalidOperationException("Bitmask search supports at most 63 nodes.");
            for (int i = 0; i < ids.Count; i++)
                world.Paths[i] = path(ids[i]).ConvertAll(id => ids.IndexOf(id)).ToArray();
            return world;
        }

        public int Index(string id) => Array.IndexOf(Ids, id);

        /// <summary>Points needed to reach node i on horizon day d.</summary>
        public int Needed(int day, int i)
        {
            int need = 0;
            foreach (int n in Paths[i])
                need = CumulativeCosts ? need + Difficulty[day, n] : Math.Max(need, Difficulty[day, n]);
            return need;
        }

        /// <summary>Applies one expedition; returns rescued count and updates masks/earned points.</summary>
        public int Run(int day, int target, int points, ref long rescued, ref long claimed, ref int earned)
        {
            int gained = 0;
            foreach (int n in Paths[target])
            {
                if (points < Difficulty[day, n]) break;
                if (CumulativeCosts) points -= Difficulty[day, n];
                long bit = 1L << n;
                if ((rescued & bit) == 0 && Survivors[n] > 0)
                {
                    rescued |= bit;
                    gained += Survivors[n];
                }
                if ((claimed & bit) == 0 && Cache[n] > 0)
                {
                    claimed |= bit;
                    earned += Cache[n];
                }
            }
            return gained;
        }
    }

    /// <summary>
    /// Exact full-information optimum by exhaustive search with memoisation.
    /// With known costs a team can pay exactly what its chosen route requires.
    /// Underfunding reaches a prefix that can be targeted directly,
    /// so each target has one useful allocation per day. Supports up to two
    /// expeditions per day (the current study rule).
    /// </summary>
    public sealed class OracleSolver
    {
        private readonly World world;
        private readonly Dictionary<(int, long, long, int), int> memo = new Dictionary<(int, long, long, int), int>();

        public OracleSolver(World world)
        {
            if (world.MaxExpeditions > 2)
                throw new NotSupportedException("The oracle enumerates at most two expeditions per day.");
            this.world = world;
        }

        /// <summary>Best total rescues from horizon day <paramref name="day"/> on.</summary>
        public int Best(int day, long rescued, long claimed, int pool) => Solve(day, rescued, claimed, pool, false, out _);

        public List<(int target, int points)> BestPlan(int day, long rescued, long claimed, int pool)
        {
            Solve(day, rescued, claimed, pool, true, out var plan);
            return plan;
        }

        private int Solve(int day, long rescued, long claimed, int pool, bool needPlan,
            out List<(int target, int points)> bestPlan)
        {
            bestPlan = new List<(int, int)>();
            if (day >= world.Days) return 0;
            var key = (day, rescued, claimed, pool);
            if (!needPlan && memo.TryGetValue(key, out int cached)) return cached;

            int budget = Math.Min(world.DailyCap, pool);
            var options = new List<(int target, int points)>();
            for (int i = 0; i < world.Count; i++)
            {
                int need = Math.Max(1, world.Needed(day, i));
                if (need > budget) continue;
                bool useful = false;
                foreach (int n in world.Paths[i])
                    if ((((rescued >> n) & 1) == 0 && world.Survivors[n] > 0) ||
                        (((claimed >> n) & 1) == 0 && world.Cache[n] > 0))
                        useful = true;
                if (useful) options.Add((i, need));
            }

            int best = Solve(day + 1, rescued, claimed, pool, false, out _);
            for (int a = 0; a < options.Count; a++)
            {
                var first = options[a];
                long r1 = rescued, c1 = claimed;
                int earned1 = 0;
                int gained1 = world.Run(day, first.target, first.points, ref r1, ref c1, ref earned1);
                int value = gained1 + Solve(day + 1, r1, c1, pool - first.points + earned1, false, out _);
                if (value > best)
                {
                    best = value;
                    bestPlan = new List<(int, int)> { first };
                }
                if (world.MaxExpeditions < 2) continue;
                for (int b = a + 1; b < options.Count; b++)
                {
                    var second = options[b];
                    if (first.points + second.points > budget) continue;
                    long r2 = r1, c2 = c1;
                    int earned2 = earned1;
                    int gained2 = world.Run(day, second.target, second.points, ref r2, ref c2, ref earned2);
                    value = gained1 + gained2 + Solve(day + 1, r2, c2, pool - first.points - second.points + earned2, false, out _);
                    if (value > best)
                    {
                        best = value;
                        bestPlan = new List<(int, int)> { first, second };
                    }
                }
            }
            memo[key] = best;
            return best;
        }
    }
}
