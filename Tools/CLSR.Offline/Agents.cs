using System;
using System.Collections.Generic;
using CLSR.Simulation;

namespace CLSR.Offline
{
    /// <summary>
    /// A synthetic player. Agents receive only the player-visible PublicView and
    /// must return a legal plan, exactly like a team. They never see the scenario.
    /// </summary>
    public interface IAgent
    {
        string Name { get; }
        string Description { get; }
        List<Leg> Plan(PublicView view, Random rng);
    }

    /// <summary>What a player can reasonably infer from public information.</summary>
    public sealed class Belief
    {
        private readonly PublicView view;
        private readonly Dictionary<string, List<PublicNode>> children = new Dictionary<string, List<PublicNode>>();
        private readonly Dictionary<string, double> expectedHere = new Dictionary<string, double>();
        private readonly Dictionary<string, int> subtreeSize = new Dictionary<string, int>();

        public Belief(PublicView view)
        {
            this.view = view;
            foreach (PublicNode node in view.Nodes)
            {
                if (!children.TryGetValue(node.ParentId, out var list)) children[node.ParentId] = list = new List<PublicNode>();
                list.Add(node);
            }
            foreach (PublicNode root in Children(ScenarioSpec.CampId))
            {
                double total = root.KnownBranchRemaining ??
                    (root.BranchEstimate.HasValue ? Math.Max(0, root.BranchEstimate.Value - root.RescuedInBranch) : (double?)null) ??
                    SubtreeSize(root.Id) * 0.5; // No report at all: assume a sparse branch.
                Allocate(root, total);
            }
        }

        public PublicView View => view;

        public List<PublicNode> Children(string id) =>
            children.TryGetValue(id, out var list) ? list : new List<PublicNode>();

        public int SubtreeSize(string id)
        {
            if (subtreeSize.TryGetValue(id, out int size)) return size;
            size = 1;
            foreach (PublicNode child in Children(id)) size += SubtreeSize(child.Id);
            subtreeSize[id] = size;
            return size;
        }

        private void Allocate(PublicNode node, double total)
        {
            if (node.KnownRemainingHere.HasValue)
            {
                expectedHere[node.Id] = node.KnownRemainingHere.Value;
                foreach (PublicNode child in Children(node.Id))
                    Allocate(child, child.KnownBranchRemaining ?? 0);
                return;
            }
            // Unreached: spread the subtree's expected survivors evenly over its sites.
            int size = SubtreeSize(node.Id);
            expectedHere[node.Id] = total / size;
            foreach (PublicNode child in Children(node.Id))
                Allocate(child, child.KnownBranchRemaining ?? total * SubtreeSize(child.Id) / size);
        }

        public double ExpectedHere(string id) => expectedHere.TryGetValue(id, out double value) ? value : 0;

        /// <summary>Thresholds never decrease away from camp, so known ancestors bound unknown nodes.</summary>
        public int LowerBound(string id)
        {
            int bound = 0;
            foreach (PublicNode node in view.PathFromCamp(id))
            {
                if (node.KnownDifficulty.HasValue) bound = Math.Max(bound, node.KnownDifficulty.Value);
                if (node.Id == id) break;
            }
            return bound;
        }

        /// <summary>Point estimate: known value, else max(lower bound, depth) (thresholds track depth on these maps).</summary>
        public int GuessDifficulty(PublicNode node) => node.KnownDifficulty ?? Math.Max(LowerBound(node.Id), node.Depth);

        /// <summary>Expected rescues and new information if the plan's guessed thresholds hold.</summary>
        public (double rescue, int info, int cache) Score(IReadOnlyList<Leg> plan)
        {
            var reached = new HashSet<string>();
            var encountered = new HashSet<string>();
            foreach (Leg leg in plan)
            {
                foreach (PublicNode node in view.PathFromCamp(leg.Target))
                {
                    encountered.Add(node.Id);
                    if (leg.Points < GuessDifficulty(node)) break;
                    reached.Add(node.Id);
                }
            }
            double rescue = 0;
            int info = 0, cache = 0;
            foreach (string id in encountered)
            {
                PublicNode node = view.Get(id);
                if (!node.KnownDifficulty.HasValue) info++;
                if (reached.Contains(id))
                {
                    rescue += ExpectedHere(id);
                    if (!node.KnownRemainingHere.HasValue) info += 1 + Children(id).Count;
                    cache += node.AvailableCacheReward ?? 0;
                }
            }
            return (rescue, info, cache);
        }

        /// <summary>Samples a complete world consistent with everything public.</summary>
        public World Sample(Random rng, int horizonDays)
        {
            var ids = new List<string>();
            foreach (PublicNode node in view.Nodes) ids.Add(node.Id);
            var world = World.Create(ids, horizonDays, view.DailyCap, view.MaxExpeditions,
                id => view.PathFromCamp(id).ConvertAll(n => n.Id));
            for (int i = 0; i < ids.Count; i++)
            {
                PublicNode node = view.Get(ids[i]);
                world.Cache[i] = node.AvailableCacheReward ?? 0;
            }

            // Thresholds: sample top-down so they stay non-decreasing along every path.
            var sampledDifficulty = new Dictionary<string, int>();
            foreach (PublicNode root in Children(ScenarioSpec.CampId)) SampleDifficulty(root, 0, rng, sampledDifficulty);
            for (int i = 0; i < ids.Count; i++)
                for (int d = 0; d < horizonDays; d++) world.Difficulty[d, i] = sampledDifficulty[ids[i]];

            // Survivors: exact where known, otherwise distribute each unknown subtree's total.
            foreach (PublicNode root in Children(ScenarioSpec.CampId)) SampleSurvivors(root, world, rng);
            return world;
        }

        private void CollectExpected(PublicNode node, ref double total)
        {
            total += ExpectedHere(node.Id);
            foreach (PublicNode child in Children(node.Id)) CollectExpected(child, ref total);
        }

        private void SampleDifficulty(PublicNode node, int parent, Random rng, Dictionary<string, int> result)
        {
            int value;
            if (node.KnownDifficulty.HasValue) value = node.KnownDifficulty.Value;
            else
            {
                int guess = Math.Max(Math.Max(parent, LowerBound(node.Id)), node.Depth);
                double roll = rng.NextDouble();
                value = roll < 0.25 ? guess - 1 : roll < 0.75 ? guess : guess + 1;
                value = Math.Max(value, Math.Max(parent, LowerBound(node.Id)));
            }
            result[node.Id] = value;
            foreach (PublicNode child in Children(node.Id)) SampleDifficulty(child, value, rng, result);
        }

        private void SampleSurvivors(PublicNode node, World world, Random rng)
        {
            if (node.KnownRemainingHere.HasValue)
            {
                world.Survivors[world.Index(node.Id)] = node.KnownRemainingHere.Value;
                foreach (PublicNode child in Children(node.Id)) SampleSurvivors(child, world, rng);
                return;
            }
            // An unreached subtree: place its expected total (rounded stochastically) uniformly at random.
            double expected = 0;
            CollectExpected(node, ref expected);
            int count = (int)Math.Floor(expected);
            if (rng.NextDouble() < expected - count) count++;
            var sites = new List<string>();
            CollectIds(node, sites);
            foreach (string id in sites) world.Survivors[world.Index(id)] = 0;
            for (int k = 0; k < count; k++) world.Survivors[world.Index(sites[rng.Next(sites.Count)])]++;
        }

        private void CollectIds(PublicNode node, List<string> ids)
        {
            ids.Add(node.Id);
            foreach (PublicNode child in Children(node.Id)) CollectIds(child, ids);
        }
    }

    public sealed class RandomAgent : IAgent
    {
        public string Name => "random";
        public string Description => "Uniformly random legal plan (including sending nobody).";
        public List<Leg> Plan(PublicView view, Random rng)
        {
            List<List<Leg>> plans = PlanSpace.Enumerate(view);
            return plans[rng.Next(plans.Count)];
        }
    }

    /// <summary>Base for agents that pick the best plan under a deterministic score.</summary>
    public abstract class ScoringAgent : IAgent
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        protected abstract double Value(Belief belief, List<Leg> plan, int pointsUsed);

        public List<Leg> Plan(PublicView view, Random rng)
        {
            var belief = new Belief(view);
            List<Leg> best = new List<Leg>();
            double bestValue = double.NegativeInfinity;
            foreach (List<Leg> plan in PlanSpace.Enumerate(view))
            {
                int used = 0;
                foreach (Leg leg in plan) used += leg.Points;
                double value = Value(belief, plan, used);
                if (value > bestValue + 1e-9)
                {
                    bestValue = value;
                    best = plan;
                }
            }
            return best;
        }
    }

    public sealed class GreedyRescueAgent : ScoringAgent
    {
        public override string Name => "greedy-rescue";
        public override string Description =>
            "Maximises today's expected rescues from branch reports and revealed counts (thresholds guessed = depth); ties use fewer points.";
        protected override double Value(Belief belief, List<Leg> plan, int used) =>
            belief.Score(plan).rescue - 0.001 * used;
    }

    public sealed class ExplorerAgent : ScoringAgent
    {
        public override string Name => "explorer";
        public override string Description =>
            "Maximises newly revealed thresholds and counts per day; rescues only break ties.";
        protected override double Value(Belief belief, List<Leg> plan, int used)
        {
            var score = belief.Score(plan);
            return score.info + 0.1 * score.rescue - 0.001 * used;
        }
    }

    public sealed class CacheAwareAgent : ScoringAgent
    {
        public override string Name => "cache-aware";
        public override string Description =>
            "Greedy rescue plus 0.5 survivor-equivalents per storage point while later days remain.";
        protected override double Value(Belief belief, List<Leg> plan, int used)
        {
            var score = belief.Score(plan);
            bool later = belief.View.Day < belief.View.TotalDays;
            return score.rescue + (later ? 0.5 * score.cache : 0) - 0.001 * used;
        }
    }

    /// <summary>Monte-Carlo agents evaluate candidate plans on worlds sampled from the belief.</summary>
    public abstract class SamplingAgent : IAgent
    {
        protected const int Candidates = 60;
        protected readonly int samples;

        protected SamplingAgent(int samples)
        {
            this.samples = samples;
        }

        public abstract string Name { get; }
        public abstract string Description { get; }

        protected List<List<Leg>> Shortlist(Belief belief)
        {
            var all = PlanSpace.Enumerate(belief.View);
            var scored = new List<(double key, List<Leg> plan)>();
            foreach (var plan in all)
            {
                var score = belief.Score(plan);
                int used = 0;
                foreach (Leg leg in plan) used += leg.Points;
                scored.Add((score.rescue + 0.3 * score.info + 0.3 * score.cache - 0.001 * used, plan));
            }
            scored.Sort((a, b) => b.key.CompareTo(a.key));
            var list = new List<List<Leg>> { new List<Leg>() };
            for (int i = 0; i < scored.Count && list.Count < Candidates; i++)
                if (scored[i].plan.Count > 0) list.Add(scored[i].plan);
            return list;
        }

        public abstract List<Leg> Plan(PublicView view, Random rng);
    }

    public sealed class RiskAwareAgent : SamplingAgent
    {
        public RiskAwareAgent(int samples = 64) : base(samples) { }
        public override string Name => "risk-aware";
        public override string Description =>
            "Today's mean rescues over sampled thresholds/placements, minus 0.5 per point lost on blocked teams.";

        public override List<Leg> Plan(PublicView view, Random rng)
        {
            var belief = new Belief(view);
            var worlds = new List<World>();
            for (int k = 0; k < samples; k++) worlds.Add(belief.Sample(rng, 1));
            List<Leg> best = new List<Leg>();
            double bestValue = double.NegativeInfinity;
            foreach (var plan in Shortlist(belief))
            {
                double total = 0;
                foreach (World world in worlds)
                {
                    long rescued = 0, claimed = 0;
                    int earned = 0;
                    foreach (Leg leg in plan)
                    {
                        int target = world.Index(leg.Target);
                        total += world.Run(0, target, leg.Points, ref rescued, ref claimed, ref earned);
                        if (leg.Points < world.Needed(0, target)) total -= 0.5 * leg.Points;
                    }
                }
                double value = total / worlds.Count - 0.001 * Used(plan);
                if (value > bestValue + 1e-9)
                {
                    bestValue = value;
                    best = plan;
                }
            }
            return best;
        }

        private static int Used(List<Leg> plan)
        {
            int used = 0;
            foreach (Leg leg in plan) used += leg.Points;
            return used;
        }
    }

    /// <summary>
    /// Information-aware planner (hindsight optimisation): for each candidate
    /// plan, average over sampled worlds today's rescues plus the best possible
    /// continuation in that world. This values exploration and storage
    /// investment, but it is optimistic because it assumes later days are
    /// played with that world's full information.
    /// </summary>
    public sealed class LookaheadAgent : SamplingAgent
    {
        public LookaheadAgent(int samples = 40) : base(samples) { }
        public override string Name => "lookahead";
        public override string Description =>
            "Monte-Carlo hindsight planner: mean over sampled worlds of today's rescues plus that world's optimal remaining days.";

        public override List<Leg> Plan(PublicView view, Random rng)
        {
            var belief = new Belief(view);
            int horizon = view.TotalDays - view.Day + 1;
            var worlds = new List<(World world, OracleSolver solver)>();
            for (int k = 0; k < samples; k++)
            {
                World world = belief.Sample(rng, horizon);
                worlds.Add((world, new OracleSolver(world)));
            }

            List<Leg> best = new List<Leg>();
            double bestValue = double.NegativeInfinity;
            foreach (var plan in Shortlist(belief))
            {
                double total = 0;
                int used = 0;
                foreach (Leg leg in plan) used += leg.Points;
                foreach (var (world, solver) in worlds)
                {
                    long rescued = 0, claimed = 0;
                    int earned = 0, gained = 0;
                    foreach (Leg leg in plan)
                        gained += world.Run(0, world.Index(leg.Target), leg.Points, ref rescued, ref claimed, ref earned);
                    total += gained + solver.Best(1, rescued, claimed, view.Pool - used + earned + view.PendingPoints);
                }
                double value = total / worlds.Count - 0.001 * used;
                if (value > bestValue + 1e-9)
                {
                    bestValue = value;
                    best = plan;
                }
            }
            return best;
        }
    }
}
