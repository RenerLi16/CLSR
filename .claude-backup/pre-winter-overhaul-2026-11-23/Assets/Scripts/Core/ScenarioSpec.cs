using System;
using System.Collections.Generic;

namespace CLSR.Simulation
{
    // Engine-independent scenario definition: the ground truth for one map.
    // Nothing in this folder references UnityEngine, so the same rules run in
    // the game, in the offline evaluator (Tools/CLSR.Offline) and in tests.

    public sealed class NodeSpec
    {
        public NodeSpec(string id, string parentId, int depth, int difficulty, int survivors, int cacheReward)
        {
            Id = id;
            ParentId = parentId ?? "";
            Depth = depth;
            Difficulty = difficulty;
            Survivors = survivors;
            CacheReward = cacheReward;
        }

        public string Id { get; }
        public string ParentId { get; }
        public int Depth { get; }
        /// <summary>Logistics points an expedition needs to pass this node (hidden).</summary>
        public int Difficulty { get; }
        /// <summary>Survivors located at this node (hidden).</summary>
        public int Survivors { get; }
        /// <summary>Points granted the day after a team first passes this node.</summary>
        public int CacheReward { get; }
    }

    public sealed class BranchReportSpec
    {
        public BranchReportSpec(string name, string rootId, int reportedSurvivors, string note)
        {
            Name = name ?? rootId;
            RootId = rootId;
            ReportedSurvivors = reportedSurvivors;
            Note = note ?? "";
        }

        public string Name { get; }
        public string RootId { get; }
        public int ReportedSurvivors { get; }
        public string Note { get; }
    }

    public sealed class EstimateRevisionSpec
    {
        public EstimateRevisionSpec(string rootId, int reportedSurvivors)
        {
            RootId = rootId;
            ReportedSurvivors = reportedSurvivors;
        }

        public string RootId { get; }
        public int ReportedSurvivors { get; }
    }

    public sealed class IntelReportSpec
    {
        public IntelReportSpec(int afterDay, string nodeId, int newDifficulty,
            IReadOnlyList<EstimateRevisionSpec> revisions, string message)
        {
            AfterDay = afterDay;
            NodeId = nodeId ?? "";
            NewDifficulty = newDifficulty;
            Revisions = revisions ?? Array.Empty<EstimateRevisionSpec>();
            Message = message ?? "";
        }

        /// <summary>The report arrives at the start of the day after this one.</summary>
        public int AfterDay { get; }
        /// <summary>Node whose difficulty changes, or empty.</summary>
        public string NodeId { get; }
        public int NewDifficulty { get; }
        /// <summary>Revised branch estimates. Reports with revisions wait for acknowledgement.</summary>
        public IReadOnlyList<EstimateRevisionSpec> Revisions { get; }
        public string Message { get; }
    }

    public sealed class ScenarioSpec
    {
        public const string CampId = "Camp";

        private readonly Dictionary<string, NodeSpec> byId = new Dictionary<string, NodeSpec>();
        private readonly Dictionary<string, List<string>> children = new Dictionary<string, List<string>>();

        public ScenarioSpec(string name, int totalDays, int totalPoints, int dailyCap, int maxExpeditions,
            string briefing, IReadOnlyList<NodeSpec> nodes, IReadOnlyList<BranchReportSpec> branches,
            IReadOnlyList<IntelReportSpec> reports)
        {
            Name = name ?? "";
            TotalDays = totalDays;
            TotalPoints = totalPoints;
            DailyCap = dailyCap;
            MaxExpeditions = maxExpeditions;
            Briefing = briefing ?? "";
            Nodes = nodes ?? Array.Empty<NodeSpec>();
            Branches = branches ?? Array.Empty<BranchReportSpec>();
            Reports = reports ?? Array.Empty<IntelReportSpec>();

            foreach (NodeSpec node in Nodes)
            {
                if (string.IsNullOrEmpty(node.Id) || byId.ContainsKey(node.Id)) continue;
                byId.Add(node.Id, node);
            }
            foreach (NodeSpec node in Nodes)
            {
                if (node.Id == CampId || string.IsNullOrEmpty(node.ParentId)) continue;
                if (!children.TryGetValue(node.ParentId, out List<string> list))
                    children[node.ParentId] = list = new List<string>();
                if (!list.Contains(node.Id)) list.Add(node.Id);
            }
        }

        public string Name { get; }
        public int TotalDays { get; }
        public int TotalPoints { get; }
        public int DailyCap { get; }
        public int MaxExpeditions { get; }
        public string Briefing { get; }
        /// <summary>Authored node order, including Camp.</summary>
        public IReadOnlyList<NodeSpec> Nodes { get; }
        public IReadOnlyList<BranchReportSpec> Branches { get; }
        public IReadOnlyList<IntelReportSpec> Reports { get; }

        /// <summary>
        /// Maps without public branch reports (the practice map) show exact
        /// remaining totals for each first-level branch instead.
        /// </summary>
        public bool ExactRootTotalsPublic => Branches.Count == 0;

        public bool Contains(string id) => id != null && byId.ContainsKey(id);

        public NodeSpec Get(string id) => id != null && byId.TryGetValue(id, out NodeSpec node) ? node : null;

        public IReadOnlyList<string> ChildrenOf(string id) =>
            id != null && children.TryGetValue(id, out List<string> list) ? list : (IReadOnlyList<string>)Array.Empty<string>();

        public BranchReportSpec FindBranch(string rootId)
        {
            foreach (BranchReportSpec branch in Branches)
                if (branch.RootId == rootId) return branch;
            return null;
        }

        /// <summary>Nodes from the first one after Camp to the target; empty if unreachable or Camp.</summary>
        public List<NodeSpec> PathFromCamp(string targetId)
        {
            var reverse = new List<NodeSpec>();
            var seen = new HashSet<string>();
            NodeSpec current = Get(targetId);
            while (current != null && current.Id != CampId)
            {
                if (!seen.Add(current.Id)) return new List<NodeSpec>();
                reverse.Add(current);
                current = Get(current.ParentId);
            }
            if (current == null) return new List<NodeSpec>();
            reverse.Reverse();
            return reverse;
        }

        public int TotalSurvivors
        {
            get
            {
                int total = 0;
                foreach (NodeSpec node in Nodes) total += node.Survivors;
                return total;
            }
        }

        /// <summary>Authoring errors. An empty list means the scenario can be played.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (TotalDays < 1) errors.Add("Scenario must contain at least one day.");
            if (DailyCap < 1 || MaxExpeditions < 1)
                errors.Add("Scenario must allow at least one logistics point and one expedition per day.");
            if (TotalPoints < 0) errors.Add("Scenario logistics pool cannot be negative.");

            var seen = new HashSet<string>();
            foreach (NodeSpec node in Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    errors.Add("Scenario contains a node with no ID.");
                    continue;
                }
                if (!seen.Add(node.Id)) errors.Add("Duplicate scenario node ID: " + node.Id);
                if (node.Difficulty < 0 || node.Survivors < 0 || node.CacheReward < 0)
                    errors.Add("Node " + node.Id + " contains a negative value.");
            }

            if (!byId.ContainsKey(CampId))
            {
                errors.Add("Scenario must contain the Camp node.");
                return errors;
            }

            foreach (NodeSpec node in Nodes)
            {
                if (node.Id == CampId || string.IsNullOrWhiteSpace(node.Id)) continue;
                NodeSpec parent = Get(node.ParentId);
                if (parent == null)
                {
                    errors.Add("Node " + node.Id + " has missing parent " + node.ParentId + ".");
                    continue;
                }
                if (node.Depth != parent.Depth + 1) errors.Add("Node " + node.Id + " has an invalid depth.");
                if (node.Difficulty < parent.Difficulty)
                    errors.Add("Difficulty decreases from " + parent.Id + " to " + node.Id + ".");
                if (PathFromCamp(node.Id).Count == 0)
                    errors.Add("Node " + node.Id + " does not connect back to Camp.");
            }

            foreach (BranchReportSpec branch in Branches)
            {
                NodeSpec root = Get(branch.RootId);
                if (root == null || root.ParentId != CampId)
                    errors.Add("Branch report " + branch.Name + " does not name a first-level node.");
            }

            foreach (IntelReportSpec report in Reports)
            {
                if (report.AfterDay < 1 || report.AfterDay >= TotalDays)
                    errors.Add("Intelligence report after day " + report.AfterDay + " would never be released.");
                if (report.NodeId.Length > 0)
                {
                    NodeSpec node = Get(report.NodeId);
                    if (node == null)
                    {
                        errors.Add("Intelligence report names unknown node " + report.NodeId + ".");
                        continue;
                    }
                    // A revised threshold must keep requirements non-decreasing away from Camp.
                    NodeSpec parent = Get(node.ParentId);
                    if (parent != null && parent.Id != CampId && report.NewDifficulty < parent.Difficulty)
                        errors.Add("Intelligence report makes " + node.Id + " easier than its parent.");
                    foreach (string child in ChildrenOf(node.Id))
                        if (Get(child).Difficulty < report.NewDifficulty)
                            errors.Add("Intelligence report makes " + node.Id + " harder than child " + child + ".");
                }
                foreach (EstimateRevisionSpec revision in report.Revisions)
                    if (FindBranch(revision.RootId) == null)
                        errors.Add("Estimate revision names unknown branch root " + revision.RootId + ".");
            }
            return errors;
        }
    }
}
