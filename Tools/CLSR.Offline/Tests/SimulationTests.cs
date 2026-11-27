using System.Collections.Generic;
using System.IO;
using System.Linq;
using CLSR.Simulation;

namespace CLSR.Offline.Tests
{
    public static class Scenarios
    {
        public static ScenarioSpec Load(string file) =>
            ScenarioAssetLoader.Load(Path.Combine(TestRunner.RepoRoot, "Assets", "Scenario Data", file));

        public static ScenarioSpec Cedar() => Load("S3_CedarValley.asset");
        public static ScenarioSpec Blackridge() => Load("S2_Blackridge.asset");
        public static ScenarioSpec Practice() => Load("S1_Mountain.asset");

        public static List<Leg> Plan(params object[] pairs)
        {
            var plan = new List<Leg>();
            for (int i = 0; i < pairs.Length; i += 2) plan.Add(new Leg((string)pairs[i], (int)pairs[i + 1]));
            return plan;
        }
    }

    public static class SimulationTests
    {
        [Test]
        public static void ShippedScenariosLoadAndValidate()
        {
            foreach (var (file, spec) in Program.LoadScenarios(TestRunner.RepoRoot))
                Assert.Equal(0, spec.Validate().Count, file + " validation errors: " + string.Join("; ", spec.Validate()));

            ScenarioSpec cedar = Scenarios.Cedar();
            Assert.Equal("Cedar Valley", cedar.Name, "name");
            Assert.Equal(3, cedar.TotalDays, "days");
            Assert.Equal(10, cedar.TotalPoints, "pool");
            Assert.Equal(5, cedar.DailyCap, "daily cap");
            Assert.Equal(2, cedar.MaxExpeditions, "expedition cap");
            Assert.Equal(17, cedar.Nodes.Count, "nodes incl. camp");
            Assert.Equal(21, cedar.TotalSurvivors, "true survivors");
            Assert.True(cedar.FindBranch("N1").Note.Contains("N3 grants +5"), "cache named in the initial report");
            Assert.Equal(4, Scenarios.Blackridge().Branches.Count, "Blackridge branches");
            Assert.True(Scenarios.Blackridge().Reports[0].Message.StartsWith("Second-pass aerial sweep"), "single-quoted message");
        }

        [Test]
        public static void FormalMapOptimaMatchInstructorVerifier()
        {
            void Check(ScenarioSpec spec, int optimum, int singleTeam, int smallerBudget)
            {
                Assert.True(spec.CumulativeCosts, spec.Name + " uses crossing costs");
                World world = World.FromTruth(spec);
                int Best() => new OracleSolver(world).Best(0, 0, 0, spec.TotalPoints);
                Assert.Equal(optimum, Best(), spec.Name + " optimum");
                world.MaxExpeditions = 1;
                Assert.Equal(singleTeam, Best(), spec.Name + " single team");
                world.MaxExpeditions = spec.MaxExpeditions;
                world.DailyCap--;
                Assert.Equal(smallerBudget, Best(), spec.Name + " daily cap minus one");
            }

            Check(Scenarios.Cedar(), 14, 13, 13);
            Check(Scenarios.Blackridge(), 18, 16, 16);
        }

        [Test]
        public static void FormalMapExpeditionsPayEachCrossingAndRevealLocalDifficulty()
        {
            foreach (ScenarioSpec spec in new[] { Scenarios.Cedar(), Scenarios.Blackridge() })
            foreach (NodeSpec target in spec.Nodes)
            {
                if (target.Id == ScenarioSpec.CampId) continue;
                var route = spec.PathFromCamp(target.Id);
                for (int points = 1; points <= spec.DailyCap; points++)
                {
                    var sim = new RescueSimulation(spec);
                    var leg = sim.Submit(Scenarios.Plan(target.Id, points)).Expeditions[0];
                    int distance = 0, rescued = 0;
                    foreach (NodeSpec site in route)
                    {
                        distance += site.Difficulty;
                        Assert.Equal(site.Difficulty, sim.View().Get(site.Id).KnownDifficulty, "local cost");
                        if (distance > points)
                        {
                            Assert.Equal(site.Id, leg.StoppedAt, "first unaffordable site");
                            Assert.Equal(distance - points, leg.RequiredPoints, "shortfall");
                            break;
                        }
                        rescued += site.Survivors;
                    }
                    Assert.Equal(rescued, leg.Rescued, "rescues along affordable prefix");
                    Assert.Equal(distance <= points, leg.ReachedTarget, "destination reached");
                }
            }
        }

        [Test]
        public static void ValidationRejectsDecreasingThresholdsAndBrokenParents()
        {
            var nodes = new List<NodeSpec>
            {
                new NodeSpec("Camp", "", 0, 0, 0, 0),
                new NodeSpec("A", "Camp", 1, 3, 1, 0),
                new NodeSpec("B", "A", 2, 2, 1, 0),
                new NodeSpec("C", "Missing", 2, 3, 0, 0),
            };
            var errors = new ScenarioSpec("bad", 2, 4, 4, 2, "", nodes, null, null).Validate();
            Assert.True(errors.Exists(e => e.Contains("Difficulty decreases")), "decreasing threshold reported");
            Assert.True(errors.Exists(e => e.Contains("missing parent")), "missing parent reported");
        }

        [Test]
        public static void RoutesFollowTheUniquePathFromCamp()
        {
            var path = Scenarios.Cedar().PathFromCamp("R5").ConvertAll(n => n.Id);
            Assert.Equal("R1,R2,R4,R5", string.Join(",", path), "R5 route");
            Assert.Equal(0, Scenarios.Cedar().PathFromCamp("Camp").Count, "camp is not a destination");
            Assert.Equal(0, Scenarios.Cedar().PathFromCamp("Nowhere").Count, "unknown node");
        }

        [Test]
        public static void LegalityChecksEveryRule()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            Assert.Equal(PlanError.None, sim.Validate(Scenarios.Plan()), "empty plan");
            Assert.Equal(PlanError.None, sim.Validate(Scenarios.Plan("W2", 2, "R1", 2)), "two teams within cap");
            Assert.Equal(PlanError.TooManyExpeditions, sim.Validate(Scenarios.Plan("W1", 1, "R1", 1, "C1", 1)), "three teams");
            Assert.Equal(PlanError.NonPositivePoints, sim.Validate(Scenarios.Plan("W1", 0)), "zero points");
            Assert.Equal(PlanError.DuplicateTarget, sim.Validate(Scenarios.Plan("W1", 1, "W1", 1)), "duplicate target");
            Assert.Equal(PlanError.UnknownTarget, sim.Validate(Scenarios.Plan("Camp", 1)), "camp target");
            Assert.Equal(PlanError.UnknownTarget, sim.Validate(Scenarios.Plan("Z9", 1)), "unknown target");
            Assert.Equal(PlanError.OverDailyCap, sim.Validate(Scenarios.Plan("W1", 6)), "one team over cap");
            Assert.Equal(PlanError.OverDailyCap, sim.Validate(Scenarios.Plan("W1", 4, "R1", 2)), "plan over cap");

            sim.Submit(Scenarios.Plan("N1", 5));
            sim.AcknowledgeBranchReports();
            sim.Submit(Scenarios.Plan("R1", 4));
            Assert.Equal(1, sim.Pool, "one point left on day 3");
            Assert.Equal(PlanError.OverPool, sim.Validate(Scenarios.Plan("W1", 2)), "over remaining pool");
            sim.Submit(Scenarios.Plan());
            Assert.Equal(PlanError.GameOver, sim.Validate(Scenarios.Plan()), "after the last day");
        }

        [Test]
        public static void IllegalPlanChangesNothing()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            string before = TestRunner.Dump(sim.View());
            Assert.Null(sim.Submit(Scenarios.Plan("W1", 4, "R1", 2)), "illegal plan rejected");
            Assert.Equal(before, TestRunner.Dump(sim.View()), "state unchanged");
            Assert.Equal(1, sim.Day, "still day 1");
        }

        [Test]
        public static void PointsAreSpentEvenWhenBlockedAndUnusedPointsCarryOver()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            DayOutcome day1 = sim.Submit(Scenarios.Plan("W2", 1));
            Assert.Equal(1, day1.PointsSpent, "spent");
            Assert.Equal("W2", day1.Expeditions[0].StoppedAt, "blocked at W2");
            Assert.Equal(1, day1.Expeditions[0].RequiredPoints, "one point short at W2");
            Assert.Equal(0, day1.Expeditions[0].Rescued, "W1 is empty");
            Assert.Equal(9, sim.Pool, "blocked team's point is gone; the rest carries over");
            Assert.Equal(2, sim.Day, "advanced");
            Assert.Equal(5, sim.TodayBudget, "daily cap applies again on day 2");
        }

        [Test]
        public static void StoragePointsArriveTheNextDayOnce()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            DayOutcome day1 = sim.Submit(Scenarios.Plan("N3", 3, "R1", 1));
            Assert.Equal(5, day1.Expeditions[0].PointsEarned, "N3 cache claimed");
            Assert.Equal(5, day1.PointsArrived, "arrives at the start of day 2");
            Assert.Equal(10 - 4 + 5, sim.Pool, "pool after storage");
            Assert.Equal(15, sim.PointsGranted, "granted total");
            sim.AcknowledgeBranchReports();
            DayOutcome day2 = sim.Submit(Scenarios.Plan("N3", 3));
            Assert.Equal(0, day2.Expeditions[0].PointsEarned, "cache pays once");
            Assert.Equal(0, day2.PointsArrived, "nothing new arrives");
        }

        [Test]
        public static void NoDuplicateRescues()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            DayOutcome day1 = sim.Submit(Scenarios.Plan("R2", 2, "R3", 2));
            Assert.Equal(1, day1.Expeditions[0].Rescued, "first team rescues R1 and R2");
            Assert.Equal(0, day1.Expeditions[1].Rescued, "second team finds R2 already rescued");
            Assert.Equal(1, sim.TotalRescued, "total");
            sim.AcknowledgeBranchReports();
            DayOutcome day2 = sim.Submit(Scenarios.Plan("R2", 2));
            Assert.Equal(0, day2.Rescued, "no rescue twice on a later day");
            Assert.Equal(1, sim.TotalRescued, "total unchanged");
        }

        [Test]
        public static void ResolutionIsDeterministic()
        {
            var plans = new[] { Scenarios.Plan("W2", 2, "C2", 2), Scenarios.Plan("R4", 3), Scenarios.Plan("C3", 3) };
            string Run()
            {
                var sim = new RescueSimulation(Scenarios.Cedar());
                var text = new System.Text.StringBuilder();
                foreach (var plan in plans)
                {
                    DayOutcome outcome = sim.Submit(plan);
                    Assert.True(outcome != null, "plan resolves");
                    text.Append(TestRunner.Dump(outcome));
                    text.Append(TestRunner.Dump(sim.View()));
                    if (outcome.BranchReportsPending) sim.AcknowledgeBranchReports();
                }
                return text.ToString();
            }
            Assert.Equal(Run(), Run(), "identical runs");
            var reset = new RescueSimulation(Scenarios.Cedar());
            reset.Submit(plans[0]);
            reset.Reset();
            Assert.Equal(TestRunner.Dump(new RescueSimulation(Scenarios.Cedar()).View()), TestRunner.Dump(reset.View()), "reset restores the start");
        }

        [Test]
        public static void ScoringCountsEverySurvivorExactlyOnce()
        {
            var spec = Scenarios.Cedar();
            var sim = new RescueSimulation(spec);
            sim.Submit(Scenarios.Plan("W3", 3, "R1", 1));
            Assert.Equal(6, sim.Pool, "six points left after spending four");
            sim.AcknowledgeBranchReports();
            DayOutcome day2 = sim.Submit(Scenarios.Plan("C3", 3));
            Assert.Equal(6, day2.Rescued, "one each at C1 and C2, four at C3");
            sim.Submit(Scenarios.Plan("R3", 3));
            int expected = 0;
            foreach (NodeSpec node in spec.Nodes) if (sim.IsRescued(node.Id)) expected += node.Survivors;
            Assert.Equal(expected, sim.TotalRescued, "score equals survivors at rescued sites");
            Assert.Equal(12, sim.TotalRescued, "three from West Woods, six from Cliff Pass, three from River Road");
            Assert.True(sim.GameOver, "finished after three days");
        }

        [Test]
        public static void RouteReportAppliesFromTheFollowingDay()
        {
            ScenarioSpec cedar = Scenarios.Cedar();
            var legacy = new ScenarioSpec(cedar.Name, cedar.TotalDays, cedar.TotalPoints, cedar.DailyCap, cedar.MaxExpeditions,
                cedar.Briefing, cedar.PathFromCamp("C3").ConvertAll(n => new NodeSpec(n.Id, n.ParentId, n.Depth, n.Depth, 1, 0))
                    .Prepend(cedar.Get("Camp")).ToList(), null,
                new[] { new IntelReportSpec(2, "C3", 2, null, "Rockfall cleared.") });
            var sim = new RescueSimulation(legacy);
            sim.Submit(Scenarios.Plan("C3", 2));
            Assert.Equal("C3", sim.History[0].Expeditions[0].StoppedAt, "C3 needs 3 on day 1");
            DayOutcome day2 = sim.Submit(Scenarios.Plan());
            Assert.Equal(1, day2.RouteReports.Count, "report released after day 2");
            Assert.Equal(2, sim.CurrentDifficulty("C3"), "C3 now needs 2");
            Assert.Equal(2, sim.View().Get("C3").KnownDifficulty, "explored node shows the new threshold");
            DayOutcome day3 = sim.Submit(Scenarios.Plan("C3", 2));
            Assert.True(day3.Expeditions[0].ReachedTarget, "C3 reachable with 2 on day 3");
        }

        [Test]
        public static void CedarValleyReportChangesEstimatesAndPreservesAccess()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            DayOutcome day1 = sim.Submit(Scenarios.Plan("C3", 2));
            Assert.Equal("C3", day1.Expeditions[0].StoppedAt, "C3 requires three LP");
            Assert.True(day1.BranchReportsPending, "Cedar correction arrives after day 1");
            Assert.Equal(0, day1.RouteReports.Count, "correction waits for the separate report screen");
            Assert.Equal(0, sim.ReleasedReports.Count, "the AI has not received the correction");
            Assert.Equal(6, sim.View().Get("R1").BranchEstimate, "River Road before acknowledgment");
            Assert.Equal(1, sim.View().Get("C1").BranchEstimate, "Cliff Pass before acknowledgment");
            Assert.Equal(PlanError.PendingBranchReports, sim.Validate(Scenarios.Plan()), "Day 2 planning waits");
            string message = sim.PendingBranchReportMessages()[0];
            Assert.True(message.Contains("River Road: 6 → 4") && message.Contains("Cliff Pass: 1 → 7"), "both estimate changes listed");
            sim.AcknowledgeBranchReports();
            Assert.Equal(4, sim.View().Get("R1").BranchEstimate, "River Road after acknowledgment");
            Assert.Equal(7, sim.View().Get("C1").BranchEstimate, "Cliff Pass after acknowledgment");
            Assert.Equal(1, sim.View().Get("C3").KnownDifficulty, "report preserves local crossing cost");
            Assert.Equal(0, sim.Submit(Scenarios.Plan()).RouteReports.Count, "no Day 2 access report");
            Assert.Equal("C3", sim.Submit(Scenarios.Plan("C3", 2)).Expeditions[0].StoppedAt, "C3 still requires three on Day 3");
            sim.Reset();
            Assert.Equal(6, sim.View().Get("R1").BranchEstimate, "restart restores River Road estimate");
            Assert.Equal(1, sim.View().Get("C1").BranchEstimate, "restart restores Cliff Pass estimate");
            Assert.Equal(0, sim.ReleasedReports.Count, "restart clears report history");
        }

        [Test]
        public static void RevisedEstimatesWaitForAcknowledgement()
        {
            var sim = new RescueSimulation(Scenarios.Blackridge());
            DayOutcome day1 = sim.Submit(Scenarios.Plan());
            Assert.True(day1.BranchReportsPending, "revision pending after day 1");
            Assert.Equal(PlanError.PendingBranchReports, sim.Validate(Scenarios.Plan()), "planning blocked");
            Assert.Equal(5, sim.View().Get("North Pass").BranchEstimate, "old estimate until acknowledged");
            string message = sim.PendingBranchReportMessages()[0];
            Assert.True(message.Contains("North Pass: 5 → 3"), "message lists the change: " + message);
            sim.AcknowledgeBranchReports();
            Assert.Equal(3, sim.View().Get("North Pass").BranchEstimate, "revised estimate");
            Assert.True(sim.View().Get("North Pass").BranchEstimateRevised, "marked revised");
            Assert.Equal(1, sim.ReleasedReports.Count, "released once acknowledged");
        }
    }
}
