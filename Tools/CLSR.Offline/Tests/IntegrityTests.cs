using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CLSR.Simulation;

namespace CLSR.Offline.Tests
{
    /// <summary>Hidden-state protection: nothing unrevealed may reach the public view or AI payloads.</summary>
    public static class VisibilityTests
    {
        // Same map, but hidden values inside unexplored parts differ.
        private static ScenarioSpec AlteredCedar()
        {
            ScenarioSpec cedar = Scenarios.Cedar();
            var nodes = new List<NodeSpec>();
            foreach (NodeSpec n in cedar.Nodes)
            {
                int survivors = n.Survivors, difficulty = n.Difficulty;
                if (n.Id == "R3") survivors = 0;      // swap R3/R4 inside the River Road junction subtree
                if (n.Id == "R4") survivors = 1;
                if (n.Id == "N3") difficulty = 5;     // a harder unexplored threshold
                if (n.Id == "N2") survivors = 0;      // North Ridge actually empty
                if (n.Id == "N4") survivors = 0;
                nodes.Add(new NodeSpec(n.Id, n.ParentId, n.Depth, difficulty, survivors, n.CacheReward));
            }
            return new ScenarioSpec(cedar.Name, cedar.TotalDays, cedar.TotalPoints, cedar.DailyCap, cedar.MaxExpeditions,
                cedar.Briefing, nodes, cedar.Branches, cedar.Reports);
        }

        [Test]
        public static void NothingHiddenIsVisibleBeforeAnyDispatch()
        {
            PublicView view = new RescueSimulation(Scenarios.Cedar()).View();
            foreach (PublicNode node in view.Nodes)
            {
                Assert.Null(node.KnownDifficulty, node.Id + " difficulty");
                Assert.Null(node.KnownRemainingHere, node.Id + " here");
                Assert.Null(node.KnownRemainingBeyond, node.Id + " beyond");
                Assert.Null(node.KnownBranchRemaining, node.Id + " branch total");
            }
            Assert.Null(view.FinalTotalSurvivors, "true total hidden during play");
        }

        [Test]
        public static void PracticeMapPublishesExactFirstLevelTotalsOnly()
        {
            PublicView view = new RescueSimulation(Scenarios.Practice()).View();
            Assert.Equal(2, view.Get("A").KnownBranchRemaining, "A branch total public on the practice map");
            Assert.Null(view.Get("C").KnownBranchRemaining, "deeper totals stay hidden");
            Assert.Null(view.Get("A").KnownRemainingHere, "site counts stay hidden");
        }

        [Test]
        public static void BlockedTeamRevealsOnlyTheThreshold()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            sim.Submit(Scenarios.Plan("W2", 1));
            PublicView view = sim.View();
            Assert.Equal(NodeDiscovery.Blocked, view.Get("W2").Discovery, "W2 blocked");
            Assert.Equal(2, view.Get("W2").KnownDifficulty, "blocking threshold known");
            Assert.Null(view.Get("W2").KnownRemainingHere, "W2 count hidden");
            Assert.Null(view.Get("W3").KnownDifficulty, "beyond the block stays unknown");
            Assert.Equal(3, view.Get("W2").KnownBranchRemaining, "passing W1 reveals W2's branch total");
            Assert.Equal(0, view.Get("W1").KnownRemainingHere, "W1 rescued, now 0");
            Assert.Equal(3, view.Get("W1").KnownRemainingBeyond, "W1 beyond");
        }

        [Test]
        public static void HiddenDifferencesNeverChangeTheViewOrAiPayload()
        {
            var a = new RescueSimulation(Scenarios.Cedar());
            var b = new RescueSimulation(AlteredCedar());
            var plans = new[] { Scenarios.Plan("W2", 2, "C2", 2), Scenarios.Plan("R1", 1, "N1", 1) };
            foreach (var plan in plans)
            {
                Assert.Equal(TestRunner.Dump(a.View()), TestRunner.Dump(b.View()), "views before " + string.Join(",", plan));
                foreach (string condition in new[] { StudyProtocol.DirectRecommendation, StudyProtocol.ConstructiveDissent })
                    Assert.Equal(SupportSnapshot.Build(condition, a.View(), plan, null),
                        SupportSnapshot.Build(condition, b.View(), plan, null), "payload " + condition);
                a.Submit(plan);
                b.Submit(plan);
                if (a.HasPendingBranchReports) a.AcknowledgeBranchReports();
                if (b.HasPendingBranchReports) b.AcknowledgeBranchReports();
            }
            // Agents see only the view, so a deterministic agent must plan identically.
            Assert.Equal(TestRunner.Dump(new GreedyRescueAgent().Plan(a.View(), new Random(1))),
                TestRunner.Dump(new GreedyRescueAgent().Plan(b.View(), new Random(1))), "same decision on same evidence");

            // Sanity check of the test: revealing R2 exposes the difference.
            a.Submit(Scenarios.Plan("R2", 2));
            b.Submit(Scenarios.Plan("R2", 2));
            Assert.True(TestRunner.Dump(a.View()) != TestRunner.Dump(b.View()), "difference visible once legitimately revealed");
        }

        [Test]
        public static void FinalTotalIsPublishedOnlyAfterTheLastDay()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            sim.Submit(Scenarios.Plan());
            sim.AcknowledgeBranchReports();
            sim.Submit(Scenarios.Plan());
            Assert.Null(sim.View().FinalTotalSurvivors, "hidden on day 3");
            sim.Submit(Scenarios.Plan());
            Assert.Equal(15, sim.View().FinalTotalSurvivors, "published at the end");
        }

        [Test]
        public static void UnannouncedCachesStayHidden()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            PublicView view = sim.View(new HashSet<string> { "W2" });
            Assert.Equal(1, view.Get("W2").AvailableCacheReward, "announced cache");
            Assert.Null(view.Get("N3").AvailableCacheReward, "cache without a marker");
        }

        [Test]
        public static void HudSourcesReadOnlyThePublicView()
        {
            // The participant-facing HUD, hover card and payload must not call truth accessors.
            string[] files = {
                "Assets/Scripts/ReviewStudyFlow.TacticalHud.cs", "Assets/Scripts/ReviewStudyFlow.LitePanels.cs",
                "Assets/Scripts/ReviewSupportSnapshot.cs", "Assets/Scripts/Core/SupportSnapshot.cs",
            };
            var forbidden = new Regex(@"CountRemaining|CountTotalSurvivors|\bTotalSurvivors\b|survivorCount|\.difficulty\b|RemainingHere\(|RemainingBeyond\(|RemainingInSubtree\(|CurrentDifficulty\(");
            foreach (string file in files)
            {
                Match match = forbidden.Match(TestRunner.Source(file));
                Assert.True(!match.Success, $"{file} reads hidden state via '{match.Value}'");
            }
        }
    }

    public static class ResearchIntegrityTests
    {
        private static List<SupportJudgment> Judgments() => new List<SupportJudgment>
        {
            new SupportJudgment { Branch = "W1", Reason = "access", Plan = Scenarios.Plan("W2", 2) },
            new SupportJudgment { Branch = "N1", Reason = "branch_count", Plan = Scenarios.Plan("N3", 4) },
            new SupportJudgment { Plan = Scenarios.Plan() },
        };

        [Test]
        public static void BothAiConditionsReceiveIdenticalInputs()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            sim.Submit(Scenarios.Plan("W2", 1));
            sim.AcknowledgeBranchReports();
            PublicView view = sim.View(new HashSet<string> { "W2", "N3" });
            string direct = SupportSnapshot.Build(StudyProtocol.DirectRecommendation, view, Scenarios.Plan("R1", 2), Judgments());
            string dissent = SupportSnapshot.Build(StudyProtocol.ConstructiveDissent, view, Scenarios.Plan("R1", 2), Judgments());
            Assert.Equal(direct.Replace(StudyProtocol.DirectRecommendation, "X"),
                dissent.Replace(StudyProtocol.ConstructiveDissent, "X"), "only the condition differs");
        }

        [Test]
        public static void PayloadIsAnonymousStructuredJsonAcceptedByTheBackend()
        {
            var sim = new RescueSimulation(Scenarios.Cedar());
            sim.Submit(Scenarios.Plan("W2", 2, "R1", 2));
            sim.AcknowledgeBranchReports();
            string payload = SupportSnapshot.Build(StudyProtocol.DirectRecommendation,
                sim.View(new HashSet<string> { "W2", "N3" }), Scenarios.Plan("C2", 2), Judgments());
            using JsonDocument json = JsonDocument.Parse(payload);
            foreach (JsonElement judgment in json.RootElement.GetProperty("context").GetProperty("initial_judgments").EnumerateArray())
            {
                var keys = judgment.EnumerateObject().Select(p => p.Name).OrderBy(k => k).ToArray();
                Assert.Equal("branch,plan,reason", string.Join(",", keys), "judgment fields");
            }
            foreach (string banned in new[] { "confidence", "explanation", "player", "name", "survivorCount", "truth" })
                Assert.True(!payload.Contains("\"" + banned), "payload must not contain " + banned);

            // Cross-check with the Python contract the backend enforces, when Python is available.
            string script = "import json,sys; from backend.support import validate_request; validate_request(json.load(sys.stdin)); print('ok')";
            try
            {
                var start = new ProcessStartInfo("python3", "-B -c \"" + script + "\"")
                {
                    WorkingDirectory = TestRunner.RepoRoot,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                using Process process = Process.Start(start);
                process.StandardInput.Write(payload);
                process.StandardInput.Close();
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(20000);
                Assert.True(output.Trim() == "ok", "backend validator rejected payload: " + output);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine("      (python3 not found; backend cross-check skipped)");
            }
        }

        [Test]
        public static void NoAiMessageCarriesNoStrategicContent()
        {
            string text = StudyProtocol.NeutralMessage.ToLowerInvariant();
            // "No ... recommendation is provided" is procedural; any advice, question or map term is not.
            foreach (string word in new[] { "recommend", "should", "consider", "branch", "route", "point", "survivor", "try", "focus" })
                Assert.True(!Regex.IsMatch(text, @"\b" + word + @"\b"), "neutral message contains '" + word + "'");
            Assert.True(!text.Contains("?"), "neutral message asks a question");
            Assert.True(!Regex.IsMatch(text, @"\d"), "neutral message contains a number");
        }

        [Test]
        public static void InterventionTimingIsSharedByAllConditions()
        {
            Assert.Equal(60, StudyProtocol.InitialDiscussionSeconds, "initial discussion");
            Assert.Equal(15, StudyProtocol.InterventionReadingSeconds, "reading interval");
            Assert.Equal(45, StudyProtocol.FurtherDiscussionSeconds, "further discussion");
            Assert.Equal(3, StudyProtocol.Players, "players");
            Assert.Equal(2, StudyProtocol.ScenariosPerSession, "scenarios per session");

            string flow = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.cs");
            string support = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.Support.cs");
            Assert.True(flow.Contains("Timed(Phase.Discuss, StudyProtocol.InitialDiscussionSeconds)"), "discussion timer");
            Assert.True(flow.Contains("Timed(Phase.Reconsider, StudyProtocol.FurtherDiscussionSeconds)"), "reconsider timer");
            // Every condition (neutral, sample, mock reply, failure) goes through ShowIntervention's single timer.
            Assert.Equal(1, Regex.Matches(flow + support, @"Timed\(Phase\.Intervention").Count, "one intervention timer");
            Assert.True(support.Contains("Timed(Phase.Intervention, StudyProtocol.InterventionReadingSeconds)"), "reading timer constant");
            Assert.Equal(4, Regex.Matches(support, @"ShowIntervention\(""").Count, "neutral, offline sample, mock reply, failure");
        }

        [Test]
        public static void AiCannotSubmitOrEditADeployment()
        {
            string support = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.Support.cs");
            foreach (string call in new[] { "TrySetPlan", "ConfirmPlan", "Deploy(", "draft", "finalPlan =", "provisional =" })
                Assert.True(!support.Contains(call), "support code touches the plan via " + call);
            Assert.True(!Regex.IsMatch(support, @"class SupportReply[\s\S]*?recommended_plan"), "reply plan is never parsed");
        }

        [Test]
        public static void PrivateAnswersStayOutOfPayloadsAndLogs()
        {
            string forms = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.Forms.cs");
            Assert.True(Regex.IsMatch(forms, @"InitialResponse\(string branch, string reason, List<ExpeditionAssignment> plan\)"),
                "initial responses keep only branch, reason and plan");
            string logging = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.Logging.cs");
            Assert.True(!Regex.IsMatch(logging, @"answers\s*[\[.]") && !logging.Contains("Value(\""), "event log never reads form answers");
            string flow = TestRunner.Source("Assets/Scripts/ReviewStudyFlow.cs");
            Assert.True(Regex.IsMatch(flow, @"private bool NextPlayer\(\)\s*\{\s*LogFormComplete\(\);\s*ResetForm\(\);"),
                "answers are cleared before the next player's form");
        }

        [Test]
        public static void EventLogIsValidJsonLinesAndRejectsUnsupportedValues()
        {
            var log = new ResearchLog("test-session");
            log.Add(0.5, "phase", "from", "Observe", "to", "Initial", "day", 1);
            log.Add(12.25, "intervention_shown", "message", "Line \"one\"\nline two", "model", null, "counts_as_ai_message", true,
                "plan", (IReadOnlyList<Leg>)Scenarios.Plan("W2", 2));
            string[] lines = log.ToJsonLines().TrimEnd('\n').Split('\n');
            Assert.Equal(2, lines.Length, "one line per event");
            using (JsonDocument first = JsonDocument.Parse(lines[1]))
            {
                Assert.Equal(1, first.RootElement.GetProperty("seq").GetInt32(), "sequence");
                Assert.Equal("Line \"one\"\nline two", first.RootElement.GetProperty("message").GetString(), "exact message");
                Assert.Equal(JsonValueKind.Null, first.RootElement.GetProperty("model").ValueKind, "null model");
                Assert.Equal("W2", first.RootElement.GetProperty("plan")[0].GetProperty("target").GetString(), "plan");
                Assert.Equal(ResearchLog.SchemaVersion, first.RootElement.GetProperty("schema").GetString(), "schema");
            }
            bool threw = false;
            try { log.Add(1, "bad", "value", new object()); } catch (ArgumentException) { threw = true; }
            Assert.True(threw, "unsupported value rejected");
            threw = false;
            try { log.Add(1, "bad", "dangling"); } catch (ArgumentException) { threw = true; }
            Assert.True(threw, "odd field list rejected");
        }
    }
}
