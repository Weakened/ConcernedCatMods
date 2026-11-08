using TheConcernedCat.ConcernedLadders.Domain.Excavation;

namespace TheConcernedCat.ConcernedLadders.Tests.Excavation;

/// <summary>#487 slice 2: every refusal and outcome has its own words, and the plan line carries the measured numbers.</summary>
public sealed class ExcavationReportTests
{
    [Fact]
    public void EveryCommitRefusalHasItsOwnMessage()
    {
        ExcavationPlan plan = ExcavationPlanner.Plan(new VanillaTerrainModel((_, _) => 50f).Sample(ExcavationMode.Small, 0, 0), 0, 0, null);
        var seen = new HashSet<string>();
        foreach (ExcavationCommitRefusal refusal in Enum.GetValues<ExcavationCommitRefusal>())
        {
            if (refusal == ExcavationCommitRefusal.PlanRefused || refusal == ExcavationCommitRefusal.NothingToDig) continue; // the plan line
            string text = ExcavationReport.Commit(refusal, ExcavationReadFault.UnsupportedLattice, plan, null);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.True(seen.Add(text), refusal + " repeats another refusal's message.");
        }
    }

    [Fact]
    public void EveryReadFaultAndPlannerRefusalAndOutcomeHasItsOwnMessage()
    {
        var faults = Enum.GetValues<ExcavationReadFault>().Where(f => f != ExcavationReadFault.None).Select(ExcavationReport.ReadFault).ToList();
        Assert.Equal(faults.Count, faults.Distinct().Count());
        var refusals = Enum.GetValues<ExcavationRefusal>().Where(r => r != ExcavationRefusal.None).Select(r => ExcavationReport.Refusal(r, 2)).ToList();
        Assert.Equal(refusals.Count, refusals.Distinct().Count());
        ExcavationPlan plan = ExcavationPlanner.Plan(new VanillaTerrainModel((_, _) => 50f).Sample(ExcavationMode.Large, 3, -4), 3, -4, null);
        var outcomes = Enum.GetValues<ExcavationOutcome>().Select(o => ExcavationReport.Outcome(o, plan)).ToList();
        Assert.Equal(outcomes.Count, outcomes.Distinct().Count());
        Assert.All(outcomes, text => Assert.Contains("(3, -4)", text));
    }

    [Fact]
    public void TheOffSwitchSaysHowToTurnItOn()
    {
        string text = ExcavationReport.Commit(ExcavationCommitRefusal.Disabled, ExcavationReadFault.None, default, null);
        Assert.Contains("[Excavation] Enabled", text);
    }

    [Fact]
    public void AReadyPlanLineCarriesTheMeasuredNumbers()
    {
        ExcavationPlan plan = ExcavationPlanner.Plan(new VanillaTerrainModel((_, _) => 50f).Sample(ExcavationMode.Large, 0, 0), 0, 0, 52f);
        string text = ExcavationReport.Plan(plan);
        Assert.Contains("4x4x4 m", text);
        Assert.Contains("at 46.0 m", text);
        Assert.Contains("changed square 6x6 m", text);
        Assert.Contains("lowers 25 vertices", text);
        Assert.Contains("flood", text);
    }

    [Fact]
    public void AnObstructionIsNamed()
    {
        string text = ExcavationReport.Commit(ExcavationCommitRefusal.Obstructed, ExcavationReadFault.None, default, "rock4_coast");
        Assert.Contains("rock4_coast", text);
        Assert.Contains("never removes", text);
    }
}
