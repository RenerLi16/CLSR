using System;
using System.Collections.Generic;

namespace CLSR.Simulation
{
    /// <summary>One expedition in a daily plan.</summary>
    public readonly struct Leg
    {
        public Leg(string target, int points)
        {
            Target = target;
            Points = points;
        }

        public string Target { get; }
        public int Points { get; }

        public override string ToString() => Points + " LP to " + Target;
    }

    public enum PlanError
    {
        None,
        GameOver,
        PendingBranchReports,
        TooManyExpeditions,
        NonPositivePoints,
        DuplicateTarget,
        UnknownTarget,
        OverDailyCap,
        OverPool,
    }

    public sealed class ExpeditionOutcome
    {
        public string Target;
        public int Points;
        public bool ReachedTarget;
        /// <summary>Node whose threshold stopped the team, or null.</summary>
        public string StoppedAt;
        /// <summary>The blocking threshold; revealed to players.</summary>
        public int RequiredPoints;
        public int Rescued;
        public int PointsEarned;
        public readonly List<string> Encountered = new List<string>();
        public readonly List<string> Reached = new List<string>();
        public readonly List<string> RescuedAt = new List<string>();
        public readonly List<string> Caches = new List<string>();
    }

    public sealed class DayOutcome
    {
        public int Day;
        public readonly List<ExpeditionOutcome> Expeditions = new List<ExpeditionOutcome>();
        public int PointsSpent;
        public int Rescued;
        public int PointsArrived;
        public readonly List<string> RouteReports = new List<string>();
        public bool BranchReportsPending;
        public bool GameOver;
    }

    /// <summary>
    /// Authoritative rules for one scenario run. Holds the hidden state and is
    /// the only place where expeditions are resolved. It is deterministic: the
    /// same scenario and the same sequence of plans always give the same result.
    /// Player-facing code should read <see cref="View"/>, never the truth.
    /// </summary>
    public sealed class RescueSimulation
    {
        private readonly HashSet<string> explored = new HashSet<string>();
        private readonly HashSet<string> reached = new HashSet<string>();
        private readonly HashSet<string> rescued = new HashSet<string>();
        private readonly HashSet<string> claimedCaches = new HashSet<string>();
        private readonly Dictionary<string, int> difficulty = new Dictionary<string, int>();
        private readonly Dictionary<string, int> revisedEstimates = new Dictionary<string, int>();
        private readonly List<IntelReportSpec> pendingBranchReports = new List<IntelReportSpec>();
        private readonly List<string> releasedReports = new List<string>();
        private readonly List<DayOutcome> history = new List<DayOutcome>();
        private readonly Dictionary<string, int> rescuedByBranch = new Dictionary<string, int>();

        public RescueSimulation(ScenarioSpec scenario)
        {
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Reset();
        }

        public ScenarioSpec Scenario { get; }
        public int Day { get; private set; }
        /// <summary>Undeployed logistics points. Deployed points are spent permanently.</summary>
        public int Pool { get; private set; }
        /// <summary>Storage-camp points earned today that arrive tomorrow.</summary>
        public int PendingPoints { get; private set; }
        public int PointsGranted { get; private set; }
        public int TotalRescued { get; private set; }
        public bool GameOver { get; private set; }
        public bool HasPendingBranchReports => pendingBranchReports.Count > 0;
        public IReadOnlyList<string> ReleasedReports => releasedReports;
        public IReadOnlyList<DayOutcome> History => history;
        public int TodayBudget => GameOver ? 0 : Math.Min(Scenario.DailyCap, Pool);

        public void Reset()
        {
            explored.Clear();
            reached.Clear();
            rescued.Clear();
            claimedCaches.Clear();
            revisedEstimates.Clear();
            pendingBranchReports.Clear();
            releasedReports.Clear();
            history.Clear();
            rescuedByBranch.Clear();
            difficulty.Clear();
            foreach (NodeSpec node in Scenario.Nodes)
                difficulty[node.Id] = node.Difficulty;
            Day = 1;
            Pool = Scenario.TotalPoints;
            PointsGranted = Pool;
            PendingPoints = 0;
            TotalRescued = 0;
            GameOver = false;
        }

        // ----- Discovery state (what has been legitimately revealed) -----

        /// <summary>DIFF is known once any expedition reaches the node, passed or blocked.</summary>
        public bool IsExplored(string id) => explored.Contains(id);
        /// <summary>HERE and BEYOND are known once a team has passed the node.</summary>
        public bool IsReached(string id) => reached.Contains(id);
        public bool IsRescued(string id) => rescued.Contains(id);
        public bool IsCacheClaimed(string id) => claimedCaches.Contains(id);
        public bool HasRevisedEstimate(string rootId) => revisedEstimates.ContainsKey(rootId);
        /// <summary>Survivors rescued so far in a first-level branch (announced in day results).</summary>
        public int RescuedInBranch(string rootId) => rescuedByBranch.TryGetValue(rootId, out int count) ? count : 0;

        public int BranchEstimate(BranchReportSpec branch) =>
            revisedEstimates.TryGetValue(branch.RootId, out int revised) ? revised : branch.ReportedSurvivors;

        // ----- Ground truth. Do not route these to participants. -----

        public int CurrentDifficulty(string id) => difficulty.TryGetValue(id, out int value) ? value : 0;

        public int RemainingHere(string id)
        {
            NodeSpec node = Scenario.Get(id);
            return node == null || rescued.Contains(id) ? 0 : node.Survivors;
        }

        public int RemainingInSubtree(string id)
        {
            int total = RemainingHere(id);
            foreach (string child in Scenario.ChildrenOf(id)) total += RemainingInSubtree(child);
            return total;
        }

        public int RemainingBeyond(string id)
        {
            int total = 0;
            foreach (string child in Scenario.ChildrenOf(id)) total += RemainingInSubtree(child);
            return total;
        }

        // ----- Actions -----

        public PlanError Validate(IReadOnlyList<Leg> plan)
        {
            if (GameOver) return PlanError.GameOver;
            if (HasPendingBranchReports) return PlanError.PendingBranchReports;
            if (plan == null) return PlanError.None;
            if (plan.Count > Scenario.MaxExpeditions) return PlanError.TooManyExpeditions;
            int spent = 0;
            var targets = new HashSet<string>();
            foreach (Leg leg in plan)
            {
                if (leg.Points <= 0) return PlanError.NonPositivePoints;
                if (leg.Target == null || leg.Target == ScenarioSpec.CampId || !Scenario.Contains(leg.Target) ||
                    Scenario.PathFromCamp(leg.Target).Count == 0)
                    return PlanError.UnknownTarget;
                if (!targets.Add(leg.Target)) return PlanError.DuplicateTarget;
                if (leg.Points > Scenario.DailyCap) return PlanError.OverDailyCap;
                spent += leg.Points;
            }
            if (spent > Scenario.DailyCap) return PlanError.OverDailyCap;
            if (spent > Pool) return PlanError.OverPool;
            return PlanError.None;
        }

        /// <summary>
        /// Resolves the whole day at once. An empty plan ends the day. Returns
        /// null (and changes nothing) for an illegal plan.
        /// </summary>
        public DayOutcome Submit(IReadOnlyList<Leg> plan)
        {
            if (Validate(plan) != PlanError.None) return null;
            plan = plan ?? Array.Empty<Leg>();

            var outcome = new DayOutcome { Day = Day };
            foreach (Leg leg in plan) outcome.PointsSpent += leg.Points;
            Pool -= outcome.PointsSpent;

            // Every expedition travels against the thresholds in force today.
            foreach (Leg leg in plan)
            {
                ExpeditionOutcome expedition = Resolve(leg);
                outcome.Expeditions.Add(expedition);
                outcome.Rescued += expedition.Rescued;
            }

            history.Add(outcome);
            if (Day >= Scenario.TotalDays)
            {
                GameOver = true;
                outcome.GameOver = true;
                return outcome;
            }

            int finished = Day;
            Day++;
            foreach (IntelReportSpec report in Scenario.Reports)
            {
                if (report.AfterDay != finished) continue;
                if (report.NodeId.Length > 0 && difficulty.ContainsKey(report.NodeId))
                    difficulty[report.NodeId] = report.NewDifficulty;
                if (report.Revisions.Count > 0)
                {
                    pendingBranchReports.Add(report);
                    outcome.BranchReportsPending = true;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(report.Message))
                {
                    releasedReports.Add(report.Message);
                    outcome.RouteReports.Add(report.Message);
                }
            }

            if (PendingPoints > 0)
            {
                Pool += PendingPoints;
                PointsGranted += PendingPoints;
                outcome.PointsArrived = PendingPoints;
                PendingPoints = 0;
            }
            return outcome;
        }

        private ExpeditionOutcome Resolve(Leg leg)
        {
            var result = new ExpeditionOutcome { Target = leg.Target, Points = leg.Points };
            List<NodeSpec> path = Scenario.PathFromCamp(leg.Target);
            string root = path.Count > 0 ? path[0].Id : "";
            foreach (NodeSpec node in path)
            {
                int threshold = CurrentDifficulty(node.Id);
                bool canPass = leg.Points >= threshold;
                explored.Add(node.Id);
                result.Encountered.Add(node.Id);
                if (!canPass)
                {
                    result.StoppedAt = node.Id;
                    result.RequiredPoints = threshold;
                    return result;
                }

                reached.Add(node.Id);
                result.Reached.Add(node.Id);
                if (node.Survivors > 0 && rescued.Add(node.Id))
                {
                    result.Rescued += node.Survivors;
                    result.RescuedAt.Add(node.Id);
                    TotalRescued += node.Survivors;
                    rescuedByBranch.TryGetValue(root, out int soFar);
                    rescuedByBranch[root] = soFar + node.Survivors;
                }
                if (node.CacheReward > 0 && claimedCaches.Add(node.Id))
                {
                    result.Caches.Add(node.Id);
                    result.PointsEarned += node.CacheReward;
                    PendingPoints += node.CacheReward;
                }
            }
            result.ReachedTarget = true;
            return result;
        }

        /// <summary>Messages of revised-estimate reports waiting for the team to acknowledge.</summary>
        public List<string> PendingBranchReportMessages()
        {
            var messages = new List<string>();
            foreach (IntelReportSpec report in pendingBranchReports)
            {
                var changes = new List<string>();
                foreach (EstimateRevisionSpec revision in report.Revisions)
                {
                    BranchReportSpec branch = Scenario.FindBranch(revision.RootId);
                    if (branch != null)
                        changes.Add(branch.Name + ": " + BranchEstimate(branch) + " → " + revision.ReportedSurvivors);
                }
                messages.Add((report.Message + " " + string.Join(" · ", changes)).Trim());
            }
            return messages;
        }

        public void AcknowledgeBranchReports()
        {
            releasedReports.AddRange(PendingBranchReportMessages());
            foreach (IntelReportSpec report in pendingBranchReports)
            foreach (EstimateRevisionSpec revision in report.Revisions)
                revisedEstimates[revision.RootId] = revision.ReportedSurvivors;
            pendingBranchReports.Clear();
        }

        /// <summary>
        /// The player-visible projection. <paramref name="publicCaches"/> lists
        /// storage camps whose reward is announced on the map; pass null to
        /// treat every authored cache as public.
        /// </summary>
        public PublicView View(ICollection<string> publicCaches = null) => PublicView.From(this, publicCaches);
    }
}
