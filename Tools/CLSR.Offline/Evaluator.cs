using System;
using System.Collections.Generic;
using System.Text;
using CLSR.Simulation;

namespace CLSR.Offline
{
    public sealed class RunResult
    {
        public string Scenario;
        public int Variant;
        public string Agent;
        public int Rescued;
        public int Oracle;
        public int TotalSurvivors;
        public int PointsSpent;
        public int PointsGranted;
        public int Teams;
        public int BlockedTeams;
        public int PointsOnBlockedTeams;
        public int ThresholdsRevealed;
        public int Caches;
        public int IllegalPlans;
        public string Plans;
    }

    /// <summary>
    /// Runs synthetic agents through the real rules. Each agent sees only the
    /// PublicView and its plan goes through the same validation as a team's.
    /// </summary>
    public static class Evaluator
    {
        /// <summary>
        /// Variant 0 is the authored scenario. Other variants keep topology,
        /// thresholds, caches, reports and each branch's true total, and
        /// re-place that branch's survivors at random (a seeded robustness check).
        /// </summary>
        public static ScenarioSpec Variant(ScenarioSpec spec, int variant)
        {
            if (variant == 0) return spec;
            var rng = new Random(StableHash(spec.Name) ^ (variant * 7919));
            var survivors = new Dictionary<string, int>();
            foreach (NodeSpec node in spec.Nodes) survivors[node.Id] = 0;
            foreach (string root in spec.ChildrenOf(ScenarioSpec.CampId))
            {
                var sites = new List<string>();
                Collect(spec, root, sites);
                int total = 0;
                foreach (string id in sites) total += spec.Get(id).Survivors;
                for (int k = 0; k < total; k++) survivors[sites[rng.Next(sites.Count)]]++;
            }
            var nodes = new List<NodeSpec>();
            foreach (NodeSpec node in spec.Nodes)
                nodes.Add(new NodeSpec(node.Id, node.ParentId, node.Depth, node.Difficulty, survivors[node.Id], node.CacheReward));
            return new ScenarioSpec(spec.Name, spec.TotalDays, spec.TotalPoints, spec.DailyCap, spec.MaxExpeditions,
                spec.Briefing, nodes, spec.Branches, spec.Reports, spec.CumulativeCosts);
        }

        private static void Collect(ScenarioSpec spec, string id, List<string> ids)
        {
            ids.Add(id);
            foreach (string child in spec.ChildrenOf(id)) Collect(spec, child, ids);
        }

        public static int StableHash(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in text ?? "") hash = (hash ^ c) * 16777619;
                return hash & 0x7fffffff;
            }
        }

        public static int OracleBest(ScenarioSpec spec)
        {
            World world = World.FromTruth(spec);
            return new OracleSolver(world).Best(0, 0, 0, spec.TotalPoints);
        }

        public static RunResult Run(ScenarioSpec spec, IAgent agent, int variant, int seed)
        {
            var sim = new RescueSimulation(spec);
            var rng = new Random(seed);
            var result = new RunResult
            {
                Scenario = spec.Name,
                Variant = variant,
                Agent = agent.Name,
                TotalSurvivors = spec.TotalSurvivors,
            };
            var plans = new StringBuilder();
            while (!sim.GameOver)
            {
                // Teams acknowledge revised estimates before planning, as in the study flow.
                if (sim.HasPendingBranchReports) sim.AcknowledgeBranchReports();
                PublicView view = sim.View();
                List<Leg> plan = agent.Plan(view, rng) ?? new List<Leg>();
                if (sim.Validate(plan) != PlanError.None)
                {
                    result.IllegalPlans++;
                    plan = new List<Leg>();
                }
                DayOutcome outcome = sim.Submit(plan);
                plans.Append(plans.Length > 0 ? " | " : "").Append("D").Append(outcome.Day).Append(": ")
                    .Append(plan.Count == 0 ? "none" : string.Join(", ", plan));
                result.PointsSpent += outcome.PointsSpent;
                foreach (ExpeditionOutcome expedition in outcome.Expeditions)
                {
                    result.Teams++;
                    result.Caches += expedition.Caches.Count;
                    if (expedition.StoppedAt != null)
                    {
                        result.BlockedTeams++;
                        result.PointsOnBlockedTeams += expedition.Points;
                    }
                }
            }
            foreach (NodeSpec node in spec.Nodes)
                if (sim.IsExplored(node.Id)) result.ThresholdsRevealed++;
            result.Rescued = sim.TotalRescued;
            result.PointsGranted = sim.PointsGranted;
            result.Plans = plans.ToString();
            return result;
        }
    }
}
