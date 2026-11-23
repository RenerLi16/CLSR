using System.Collections.Generic;
using CLSR.Simulation;

// Explicit public projection: never serialize a scenario, MonoBehaviour, or answer form.
// The body is built from the simulation's PublicView, which cannot carry
// unrevealed values, by the engine-independent SupportSnapshot builder.
public static class ReviewSupportSnapshot
{
    public const string Version = SupportSnapshot.Version;

    public sealed class Judgment
    {
        public string Branch, Reason;
        public IReadOnlyList<ExpeditionAssignment> Plan;
    }

    public static string Build(string condition, ScenarioController scenario,
        RescueGameController game, IReadOnlyList<ExpeditionAssignment> provisional,
        IEnumerable<Judgment> judgments, ISet<string> visibleCaches)
    {
        PublicView view = scenario.Simulation.View(visibleCaches ?? new HashSet<string>());
        view.Pool = game.LogisticsRemaining;
        var anonymous = new List<SupportJudgment>();
        foreach (Judgment judgment in judgments)
            anonymous.Add(new SupportJudgment
            {
                Branch = judgment.Branch,
                Reason = judgment.Reason,
                Plan = RescueGameController.ToLegs(judgment.Plan)
            });
        return SupportSnapshot.Build(condition, view, RescueGameController.ToLegs(provisional), anonymous);
    }
}
