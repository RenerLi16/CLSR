using System.Collections.Generic;

namespace CLSR.Simulation
{
    public enum NodeDiscovery
    {
        Unexplored,
        /// <summary>A team reached the node but could not pass it.</summary>
        Blocked,
        /// <summary>A team passed the node.</summary>
        Reached,
    }

    /// <summary>What players may know about one site. Null means "not yet revealed".</summary>
    public sealed class PublicNode
    {
        public string Id;
        public string ParentId;
        public int Depth;
        public NodeDiscovery Discovery;
        public bool SurvivorsRescuedHere;
        /// <summary>Minimum points to pass (revealed when any team reaches the node).</summary>
        public int? KnownDifficulty;
        /// <summary>Survivors still waiting here (revealed when a team passes the node).</summary>
        public int? KnownRemainingHere;
        /// <summary>Survivors still waiting further along (revealed when a team passes the node).</summary>
        public int? KnownRemainingBeyond;
        /// <summary>
        /// Remaining survivors in the branch rooted here. Revealed when a team
        /// passes the parent junction (or, on maps without reports, for first-level branches).
        /// </summary>
        public int? KnownBranchRemaining;
        /// <summary>Announced storage reward that has not been claimed yet.</summary>
        public int? AvailableCacheReward;
        public bool CacheClaimed;
        /// <summary>Public estimate for a first-level branch (initial or revised).</summary>
        public int? BranchEstimate;
        public bool BranchEstimateRevised;
        public string BranchName;
        public string BranchNote;
        public int? InitialBranchReport;
        /// <summary>First-level nodes only: survivors already rescued in this branch.</summary>
        public int RescuedInBranch;
    }

    /// <summary>
    /// Player-visible game state. UI, participant-facing AI payloads and offline
    /// agents read only this object; it never carries unrevealed values.
    /// </summary>
    public sealed class PublicView
    {
        private readonly Dictionary<string, PublicNode> byId = new Dictionary<string, PublicNode>();

        public string ScenarioName;
        public int Day;
        public int TotalDays;
        public int Pool;
        public int DailyCap;
        public int MaxExpeditions;
        public int PendingPoints;
        public int PointsGranted;
        public int TotalRescued;
        public bool GameOver;
        public bool BranchReportsPending;
        /// <summary>The true scenario total; only published after the scenario ends.</summary>
        public int? FinalTotalSurvivors;
        public readonly List<PublicNode> Nodes = new List<PublicNode>();
        public readonly List<string> ReleasedReports = new List<string>();

        public int TodayBudget => GameOver ? 0 : System.Math.Min(DailyCap, Pool);

        public PublicNode Get(string id) => id != null && byId.TryGetValue(id, out PublicNode node) ? node : null;

        public IEnumerable<PublicNode> ChildrenOf(string id)
        {
            foreach (PublicNode node in Nodes)
                if (node.ParentId == id) yield return node;
        }

        /// <summary>Path from the first node after Camp to the target (topology is public).</summary>
        public List<PublicNode> PathFromCamp(string id)
        {
            var path = new List<PublicNode>();
            var seen = new HashSet<string>();
            PublicNode current = Get(id);
            while (current != null && seen.Add(current.Id))
            {
                path.Add(current);
                if (current.ParentId == ScenarioSpec.CampId)
                {
                    path.Reverse();
                    return path;
                }
                current = Get(current.ParentId);
            }
            return new List<PublicNode>();
        }

        internal static PublicView From(RescueSimulation sim, ICollection<string> publicCaches)
        {
            ScenarioSpec spec = sim.Scenario;
            var view = new PublicView
            {
                ScenarioName = spec.Name,
                Day = sim.Day,
                TotalDays = spec.TotalDays,
                Pool = sim.Pool,
                DailyCap = spec.DailyCap,
                MaxExpeditions = spec.MaxExpeditions,
                PendingPoints = sim.PendingPoints,
                PointsGranted = sim.PointsGranted,
                TotalRescued = sim.TotalRescued,
                GameOver = sim.GameOver,
                BranchReportsPending = sim.HasPendingBranchReports,
                FinalTotalSurvivors = sim.GameOver ? spec.TotalSurvivors : (int?)null,
            };
            view.ReleasedReports.AddRange(sim.ReleasedReports);

            foreach (NodeSpec def in spec.Nodes)
            {
                if (def.Id == ScenarioSpec.CampId) continue;
                string id = def.Id;
                bool passed = sim.IsReached(id);
                bool parentPassed = sim.IsReached(def.ParentId);
                bool firstLevel = def.ParentId == ScenarioSpec.CampId;
                var node = new PublicNode
                {
                    Id = id,
                    ParentId = def.ParentId,
                    Depth = def.Depth,
                    Discovery = passed ? NodeDiscovery.Reached :
                        sim.IsExplored(id) ? NodeDiscovery.Blocked : NodeDiscovery.Unexplored,
                    SurvivorsRescuedHere = sim.IsRescued(id),
                    KnownDifficulty = sim.IsExplored(id) ? sim.CurrentDifficulty(id) : (int?)null,
                    KnownRemainingHere = passed ? sim.RemainingHere(id) : (int?)null,
                    KnownRemainingBeyond = passed ? sim.RemainingBeyond(id) : (int?)null,
                    KnownBranchRemaining = parentPassed || (firstLevel && spec.ExactRootTotalsPublic)
                        ? sim.RemainingInSubtree(id) : (int?)null,
                    CacheClaimed = sim.IsCacheClaimed(id),
                    RescuedInBranch = firstLevel ? sim.RescuedInBranch(id) : 0,
                };
                bool cacheAnnounced = def.CacheReward > 0 &&
                    (publicCaches == null || publicCaches.Contains(id));
                if (cacheAnnounced && !node.CacheClaimed) node.AvailableCacheReward = def.CacheReward;

                BranchReportSpec branch = firstLevel ? spec.FindBranch(id) : null;
                if (branch != null)
                {
                    node.BranchName = branch.Name;
                    node.BranchNote = branch.Note;
                    node.InitialBranchReport = branch.ReportedSurvivors;
                    node.BranchEstimate = sim.BranchEstimate(branch);
                    node.BranchEstimateRevised = sim.HasRevisedEstimate(id);
                }
                view.Nodes.Add(node);
                view.byId[id] = node;
            }
            return view;
        }
    }
}
