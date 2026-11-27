using System;
using System.Collections.Generic;
using System.Linq;
using CLSR.Presentation;
using CLSR.Simulation;

namespace CLSR.Offline.Tests
{
    public static class PresentationTests
    {
        private static bool Close(float a, float b, float tolerance = 1e-3f) => Math.Abs(a - b) <= tolerance;

        [Test]
        public static void ContinuousZoomMatchesTheOriginalSteps()
        {
            // The old controls: level n -> 1 + 0.15n, then x1.25 per close-up step beyond 4.
            for (int level = -2; level <= 8; level++)
            {
                float old = 1f + Math.Min(level, 4) * 0.15f;
                if (level > 4) old *= (float)Math.Pow(1.25, level - 4);
                Assert.True(Close(old, MapViewMath.Magnification(level)), "level " + level);
            }
            Assert.True(MapViewMath.Magnification(2.5f) > MapViewMath.Magnification(2f) &&
                MapViewMath.Magnification(2.5f) < MapViewMath.Magnification(3f), "monotone between steps");
            Assert.Equal(-2f, MapViewMath.ClampLevel(-9f, 6f), "lower clamp");
            Assert.Equal(6f, MapViewMath.ClampLevel(9f, 6f), "upper clamp");
        }

        [Test]
        public static void CursorAnchoredZoomKeepsThePointUnderTheCursor()
        {
            var rng = new Random(7);
            for (int i = 0; i < 500; i++)
            {
                float aspect = 1.2f + (float)rng.NextDouble();
                float oldSize = 2f + 10f * (float)rng.NextDouble();
                float newSize = oldSize / (0.5f + 2f * (float)rng.NextDouble());
                float u = (float)rng.NextDouble(), board = 0.4f + 0.2f * (float)rng.NextDouble();
                float mapCenter = 20f * (float)rng.NextDouble() - 10f;

                // World point under the cursor before zooming (camera centred at 0).
                float anchor = MapViewMath.ScreenToLocal(u, oldSize, aspect);
                float pan = MapViewMath.AnchoredPan(mapCenter, anchor, u, board, newSize, aspect);
                float shift = MapViewMath.CameraShift(mapCenter, board, newSize, aspect, pan);
                // After moving the camera by 'shift', the same screen position shows:
                float shown = shift + MapViewMath.ScreenToLocal(u, newSize, aspect);
                Assert.True(Close(anchor, shown), $"anchor drifted: {anchor} vs {shown}");
            }
        }

        [Test]
        public static void ZoomSmoothingConvergesAndSnaps()
        {
            float value = 0f;
            for (int frame = 0; frame < 120; frame++) value = MapViewMath.Smooth(value, 3f, 1f / 60f);
            Assert.Equal(3f, value, "snaps to target within two seconds");
            Assert.Equal(1.5f, MapViewMath.Smooth(1.5f, 3f, 0f), "no time, no change");
            float halfway = MapViewMath.Smooth(0f, 1f, 0.05f);
            Assert.True(halfway > 0f && halfway < 1f, "eases rather than jumps");
        }

        private static readonly (int w, int h)[] Resolutions =
        {
            (1024, 768), (1280, 720), (1280, 800), (1366, 768), (1440, 900), (1536, 864), (1600, 900),
            (1680, 1050), (1920, 1080), (1920, 1200), (2560, 1440), (2560, 1600), (2880, 1800), (3024, 1964), (3840, 2160),
        };

        [Test]
        public static void HudPanelsStayOnScreenAndNeverOverlap()
        {
            foreach (var (w, h) in Resolutions)
            {
                var layout = new TacticalLayout(w, h);
                var panels = layout.PersistentPanels().ToList();
                foreach (var panel in panels)
                    Assert.True(panel.Value.Inside(w, h), $"{w}x{h}: {panel.Key} {panel.Value} off screen");
                for (int i = 0; i < panels.Count; i++)
                    for (int j = i + 1; j < panels.Count; j++)
                        Assert.True(!panels[i].Value.Overlaps(panels[j].Value),
                            $"{w}x{h}: {panels[i].Key} {panels[i].Value} overlaps {panels[j].Key} {panels[j].Value}");
            }
        }

        [Test]
        public static void MapRemainsTheDominantElement()
        {
            foreach (var (w, h) in Resolutions)
            {
                var layout = new TacticalLayout(w, h);
                // Sample the screen and count points not covered by any HUD panel during planning.
                int free = 0, total = 0;
                for (float x = 0.5f; x < w; x += w / 96f)
                    for (float y = 0.5f; y < h; y += h / 54f)
                    {
                        total++;
                        if (!layout.IsOverHud(x, y, interventionVisible: true, menuOpen: false)) free++;
                    }
                double share = (double)free / total;
                // Worst case: every planning panel plus the reconsideration message are on screen.
                Assert.True(share > 0.60, $"{w}x{h}: map visible on only {share:P0} of the screen");
                Assert.True(layout.IsOverHud(layout.Command.X + 2, layout.Command.Y + 2, false, false), "command area blocks map clicks");
                Assert.True(!layout.IsOverHud(w * 0.5f, h * 0.5f, false, false), "screen centre is map");
            }
        }

        [Test]
        public static void ControlsKeepAUsableSize()
        {
            foreach (var (w, h) in Resolutions)
            {
                var layout = new TacticalLayout(w, h);
                // Command grid cells and toolbar cells are at least ~30 px on laptop screens.
                Assert.True(layout.Command.Width / 3f >= 40f && layout.Command.Height / 3f >= 30f, $"{w}x{h}: command cells too small");
                Assert.True(layout.Toolbar.Height / 4f >= 26f && layout.Toolbar.Width >= 40f, $"{w}x{h}: toolbar cells too small");
            }
        }
    }

    public static class StrategyTests
    {
        [Test]
        public static void OracleMatchesBruteForceOnThePracticeMap()
        {
            ScenarioSpec spec = Scenarios.Practice();
            int best = 0;
            var view = new RescueSimulation(spec).View();
            foreach (List<Leg> plan in PlanSpace.Enumerate(view))
            {
                var sim = new RescueSimulation(spec);
                sim.Submit(plan);
                best = Math.Max(best, sim.TotalRescued);
            }
            Assert.Equal(best, Evaluator.OracleBest(spec), "oracle optimum");
        }

        [Test]
        public static void OracleMatchesBruteForceOverTwoDaysOfCedarValley()
        {
            // Truncate Cedar Valley to two days so exhaustive search over legal plans is cheap.
            ScenarioSpec cedar = Scenarios.Cedar();
            var spec = new ScenarioSpec(cedar.Name, 2, cedar.TotalPoints, cedar.DailyCap, cedar.MaxExpeditions,
                cedar.Briefing, cedar.Nodes, cedar.Branches, new List<IntelReportSpec>(), cedar.CumulativeCosts);
            var first = PlanSpace.Enumerate(new RescueSimulation(spec).View());
            int best = 0;
            foreach (var day1 in first)
            {
                var sim = new RescueSimulation(spec);
                sim.Submit(day1);
                // Day 2 with full knowledge: try the oracle's useful options via brute force too.
                foreach (var day2 in PlanSpace.Enumerate(sim.View()))
                {
                    var copy = new RescueSimulation(spec);
                    copy.Submit(day1);
                    copy.Submit(day2);
                    best = Math.Max(best, copy.TotalRescued);
                }
            }
            Assert.Equal(best, Evaluator.OracleBest(spec), "two-day optimum");
        }

        [Test]
        public static void PlanSpaceIsExactlyTheLegalPlans()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            var plans = PlanSpace.Enumerate(sim.View());
            foreach (var plan in plans)
                Assert.Equal(PlanError.None, sim.Validate(plan), "enumerated plan is legal");
            // 16 sites: 1 empty + 16*5 singles + C(16,2) * 10 point splits within the cap of 5.
            Assert.Equal(1 + 16 * 5 + 120 * 10, plans.Count, "complete enumeration");
        }

        [Test]
        public static void EveryAgentPlaysLegallyOnEveryScenario()
        {
            foreach (var (file, spec) in Program.LoadScenarios(TestRunner.RepoRoot))
                for (int variant = 0; variant <= 3; variant++)
                    foreach (IAgent agent in Program.Agents())
                    {
                        RunResult run = Evaluator.Run(Evaluator.Variant(spec, variant), agent, variant, 11);
                        Assert.Equal(0, run.IllegalPlans, $"{agent.Name} on {file} variant {variant}");
                        Assert.True(run.Rescued <= Evaluator.OracleBest(Evaluator.Variant(spec, variant)),
                            $"{agent.Name} beat the full-information optimum");
                    }
        }

        [Test]
        public static void VariantsPreserveEverythingButPlacement()
        {
            ScenarioSpec spec = Scenarios.Cedar();
            ScenarioSpec variant = Evaluator.Variant(spec, 5);
            Assert.Equal(spec.TotalSurvivors, variant.TotalSurvivors, "total");
            foreach (string root in spec.ChildrenOf(ScenarioSpec.CampId))
                Assert.Equal(new RescueSimulation(spec).RemainingInSubtree(root),
                    new RescueSimulation(variant).RemainingInSubtree(root), "branch total " + root);
            foreach (NodeSpec node in spec.Nodes)
            {
                Assert.Equal(node.Difficulty, variant.Get(node.Id).Difficulty, "threshold " + node.Id);
                Assert.Equal(node.CacheReward, variant.Get(node.Id).CacheReward, "cache " + node.Id);
            }
            Assert.Equal(TestRunner.Dump(Evaluator.Variant(spec, 5)), TestRunner.Dump(variant), "seeded and repeatable");
            Assert.Equal(0, variant.Validate().Count, "variant valid");
        }
    }
}
