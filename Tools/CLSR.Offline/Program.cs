using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CLSR.Simulation;

namespace CLSR.Offline
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string command = args.Length > 0 ? args[0] : "help";
            string root = FindRepoRoot();
            switch (command)
            {
                case "test":
                    return Tests.TestRunner.Run(root);
                case "eval":
                    return Evaluate(root, args);
                default:
                    Console.WriteLine("Usage: dotnet run -c Release -- test | eval [--variants N] [--out DIR]");
                    return command == "help" ? 0 : 2;
            }
        }

        public static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "Scenario Data")))
                dir = Path.GetDirectoryName(dir);
            if (dir != null) return dir;
            dir = Directory.GetCurrentDirectory();
            while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets", "Scenario Data")))
                dir = Path.GetDirectoryName(dir);
            return dir ?? throw new DirectoryNotFoundException("Run from inside the CLSR repository.");
        }

        public static List<(string file, ScenarioSpec spec)> LoadScenarios(string root)
        {
            var list = new List<(string, ScenarioSpec)>();
            foreach (string path in Directory.GetFiles(Path.Combine(root, "Assets", "Scenario Data"), "*.asset").OrderBy(p => p))
                list.Add((Path.GetFileName(path), ScenarioAssetLoader.Load(path)));
            return list;
        }

        public static IAgent[] Agents() => new IAgent[]
        {
            new RandomAgent(),
            new GreedyRescueAgent(),
            new ExplorerAgent(),
            new CacheAwareAgent(),
            new RiskAwareAgent(),
            new LookaheadAgent(),
        };

        private static int Evaluate(string root, string[] args)
        {
            int variants = 100;
            int randomRepeats = 20;
            string outDir = Path.Combine(root, "Tools", "CLSR.Offline", "results");
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i] == "--variants") variants = int.Parse(args[i + 1]);
                if (args[i] == "--out") outDir = args[i + 1];
            }
            Directory.CreateDirectory(outDir);

            var results = new List<RunResult>();
            var landscapes = new Dictionary<string, string>();
            var oracle = new Dictionary<(string, int), int>();
            var started = DateTime.UtcNow;
            foreach (var (file, spec) in LoadScenarios(root))
            {
                List<string> errors = spec.Validate();
                if (errors.Count > 0)
                {
                    Console.WriteLine($"{file}: invalid scenario, skipped: {string.Join("; ", errors)}");
                    continue;
                }
                landscapes[spec.Name] = DayOneLandscape(spec);
                for (int v = 0; v <= variants; v++)
                {
                    ScenarioSpec world = Evaluator.Variant(spec, v);
                    int best = Evaluator.OracleBest(world);
                    oracle[(spec.Name, v)] = best;
                    IAgent[] agents = Agents();
                    for (int a = 0; a < agents.Length; a++)
                    {
                        int repeats = agents[a] is RandomAgent ? randomRepeats : 1;
                        for (int r = 0; r < repeats; r++)
                        {
                            RunResult run = Evaluator.Run(world, agents[a], v, 1000 * v + 37 * a + r);
                            run.Oracle = best;
                            results.Add(run);
                        }
                    }
                }
                Console.WriteLine($"{spec.Name}: done ({(DateTime.UtcNow - started).TotalSeconds:0}s)");
            }

            File.WriteAllText(Path.Combine(outDir, "runs.csv"), Csv(results));
            string report = Report(results, variants, randomRepeats, landscapes);
            File.WriteAllText(Path.Combine(outDir, "benchmark.md"), report);
            Console.WriteLine(report);
            return 0;
        }

        private static string Csv(List<RunResult> results)
        {
            var csv = new StringBuilder("scenario,variant,agent,rescued,oracle,total_survivors,points_spent,points_granted,teams,blocked_teams,points_on_blocked,thresholds_revealed,caches,illegal_plans,plans\n");
            foreach (RunResult r in results)
                csv.Append($"\"{r.Scenario}\",{r.Variant},{r.Agent},{r.Rescued},{r.Oracle},{r.TotalSurvivors},{r.PointsSpent},{r.PointsGranted},{r.Teams},{r.BlockedTeams},{r.PointsOnBlockedTeams},{r.ThresholdsRevealed},{r.Caches},{r.IllegalPlans},\"{r.Plans}\"\n");
            return csv.ToString();
        }

        private static double Mean(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        private static double Sd(IEnumerable<double> values)
        {
            var list = values.ToList();
            if (list.Count < 2) return 0;
            double mean = list.Average();
            return Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1));
        }

        /// <summary>
        /// How decisive is the first day? For every legal Day-1 plan, the best
        /// total still achievable with full information afterwards.
        /// </summary>
        public static string DayOneLandscape(ScenarioSpec spec)
        {
            World world = World.FromTruth(spec);
            var solver = new OracleSolver(world);
            int optimum = solver.Best(0, 0, 0, spec.TotalPoints);
            var values = new List<int>();
            foreach (List<Leg> plan in PlanSpace.Enumerate(new RescueSimulation(spec).View()))
            {
                long rescued = 0, claimed = 0;
                int earned = 0, gained = 0, used = 0;
                foreach (Leg leg in plan)
                {
                    gained += world.Run(0, world.Index(leg.Target), leg.Points, ref rescued, ref claimed, ref earned);
                    used += leg.Points;
                }
                values.Add(gained + solver.Best(1, rescued, claimed, spec.TotalPoints - used + earned));
            }
            values.Sort();
            int atOptimum = values.Count(v => v == optimum);
            int withinOne = values.Count(v => v >= optimum - 1);
            return $"Day-1 decision landscape (authored map, full information afterwards): {values.Count} legal plans; " +
                $"{atOptimum} ({100.0 * atOptimum / values.Count:0.0}%) keep the optimum {optimum} reachable, " +
                $"{withinOne} ({100.0 * withinOne / values.Count:0.0}%) stay within one survivor; " +
                $"median {values[values.Count / 2]}, worst {values[0]}.";
        }

        public static string Report(List<RunResult> results, int variants, int randomRepeats,
            Dictionary<string, string> landscapes = null)
        {
            var md = new StringBuilder();
            md.AppendLine("# CLSR offline strategy benchmark");
            md.AppendLine();
            md.AppendLine("Generated by `Tools/CLSR.Offline` (`dotnet run -c Release -- eval`). Agents see only the");
            md.AppendLine("player-visible `PublicView` and submit plans through the game's own validation.");
            md.AppendLine($"Variant 0 is each authored scenario; variants 1–{variants} keep the map, thresholds, caches,");
            md.AppendLine("reports and each branch's true total but re-place that branch's survivors at random.");
            md.AppendLine($"`random` is repeated {randomRepeats}× per variant. The oracle is the exact optimum with full");
            md.AppendLine("knowledge of the hidden map — a ceiling no player can be expected to reach.");
            md.AppendLine();
            md.AppendLine("| Agent | Strategy |");
            md.AppendLine("| --- | --- |");
            foreach (IAgent agent in Agents()) md.AppendLine($"| `{agent.Name}` | {agent.Description} |");
            md.AppendLine();

            foreach (var scenario in results.GroupBy(r => r.Scenario))
            {
                var authoredOracle = scenario.First(r => r.Variant == 0).Oracle;
                int total = scenario.First(r => r.Variant == 0).TotalSurvivors;
                md.AppendLine($"## {scenario.Key}");
                md.AppendLine();
                md.AppendLine($"Authored map: {total} survivors; full-information optimum {authoredOracle}. " +
                    $"Oracle mean over variants: {Mean(scenario.Where(r => r.Variant > 0).GroupBy(r => r.Variant).Select(g => (double)g.First().Oracle)):0.00}.");
                md.AppendLine();
                if (landscapes != null && landscapes.TryGetValue(scenario.Key, out string landscape))
                {
                    md.AppendLine(landscape);
                    md.AppendLine();
                }
                md.AppendLine("| Agent | Authored | Variants mean ± sd | min–max | % of oracle | LP spent | LP on blocked teams | Thresholds revealed | Caches |");
                md.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
                foreach (var agent in scenario.GroupBy(r => r.Agent))
                {
                    var authored = agent.Where(r => r.Variant == 0).ToList();
                    var varied = agent.Where(r => r.Variant > 0).ToList();
                    string authoredText = authored.Count == 1 ? authored[0].Rescued.ToString()
                        : $"{Mean(authored.Select(r => (double)r.Rescued)):0.0}";
                    double pct = 100 * Mean(varied.Select(r => r.Oracle == 0 ? 1 : (double)r.Rescued / r.Oracle));
                    md.AppendLine($"| `{agent.Key}` | {authoredText} | {Mean(varied.Select(r => (double)r.Rescued)):0.00} ± {Sd(varied.Select(r => (double)r.Rescued)):0.00} | " +
                        $"{(varied.Count > 0 ? varied.Min(r => r.Rescued) : 0)}–{(varied.Count > 0 ? varied.Max(r => r.Rescued) : 0)} | {pct:0}% | " +
                        $"{Mean(agent.Select(r => (double)r.PointsSpent)):0.0} | {Mean(agent.Select(r => (double)r.PointsOnBlockedTeams)):0.0} | " +
                        $"{Mean(agent.Select(r => (double)r.ThresholdsRevealed)):0.0} | {Mean(agent.Select(r => (double)r.Caches)):0.0} |");
                }
                md.AppendLine();

                // Paired comparison on identical variants: does a simple heuristic dominate?
                var byAgent = scenario.Where(r => r.Agent != "random").GroupBy(r => r.Agent)
                    .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Variant, r => r.Rescued));
                if (byAgent.ContainsKey("greedy-rescue") && byAgent.ContainsKey("lookahead"))
                {
                    md.AppendLine("Paired comparisons over the same variants (wins / ties / losses for the first agent):");
                    md.AppendLine();
                    foreach (string other in byAgent.Keys.Where(k => k != "greedy-rescue"))
                    {
                        int win = 0, tie = 0, loss = 0;
                        var diffs = new List<double>();
                        foreach (var pair in byAgent["greedy-rescue"])
                        {
                            if (!byAgent[other].TryGetValue(pair.Key, out int otherScore)) continue;
                            diffs.Add(pair.Value - otherScore);
                            if (pair.Value > otherScore) win++;
                            else if (pair.Value == otherScore) tie++;
                            else loss++;
                        }
                        double se = diffs.Count > 1 ? Sd(diffs) / Math.Sqrt(diffs.Count) : 0;
                        md.AppendLine($"- `greedy-rescue` vs `{other}`: {win} / {tie} / {loss}; mean difference {Mean(diffs):+0.00;-0.00;0.00} (SE {se:0.00})");
                    }
                    md.AppendLine();
                }

                // Distinct plans indicate whether the decision space is genuinely open.
                var authoredPlans = scenario.Where(r => r.Variant == 0 && r.Agent != "random")
                    .Select(r => $"- `{r.Agent}` ({r.Rescued}): {r.Plans}");
                md.AppendLine("Authored-map plans:");
                md.AppendLine();
                foreach (string line in authoredPlans) md.AppendLine(line);
                md.AppendLine();
            }
            return md.ToString();
        }
    }
}
