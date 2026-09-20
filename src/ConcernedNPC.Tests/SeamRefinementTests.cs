using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Tests;

/// <summary>Three refinements the first leaf asked the lead for, each of which
/// exists because the seam could not say something true.
///
/// A seam that cannot express a real case does not fail loudly. It makes the
/// nearest wrong answer look right: a job with no chest reported as a chest that
/// vanished, and a probe that broke reported as ground nobody has walked to
/// yet.</summary>
public class SeamRefinementTests
{
    [Fact]
    public void A_job_that_named_no_container_is_not_a_container_that_vanished()
    {
        // These were the same value until now, and the player's fix differs:
        // one is "choose a chest", the other is "your chest is gone".
        Assert.NotEqual(NpcContainerRefusal.Gone, NpcContainerRefusal.NotDesignated);

        // Refusals are reported lowest-value-first, so a plan missing its chest
        // must outrank a plan whose chest is merely switched off - otherwise a
        // player is told to enable a container they never picked.
        Assert.True(NpcContainerRefusal.NotDesignated < NpcContainerRefusal.NotEnabled);
        Assert.True(NpcContainerRefusal.NotThisContainer < NpcContainerRefusal.NotDesignated);
    }

    [Fact]
    public void Every_refusal_still_has_its_own_value_after_the_renumbering()
    {
        int[] values = (int[])Enum.GetValues(typeof(NpcContainerRefusal));
        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(0, (int)NpcContainerRefusal.None);
    }

    [Fact]
    public void A_probe_that_could_not_answer_is_counted_apart_from_ground_nobody_reached()
    {
        var unreachable = new AreaScanReport(AreaScanOutcome.NotLoaded, 10, 10, 0, 0, false);
        var broken = new AreaScanReport(AreaScanOutcome.NotLoaded, 10, 0, 0, 0, false, unreadable: 10);

        Assert.Equal(10, unreachable.NotLoaded);
        Assert.Equal(0, unreachable.Unreadable);
        Assert.Equal(0, broken.NotLoaded);
        Assert.Equal(10, broken.Unreadable);

        // The distinction is for the diagnosis, not the decision: walking over
        // there fixes one and fixes nothing about the other.
        Assert.False(unreachable.IsConclusive);
        Assert.False(broken.IsConclusive);
    }

    [Fact]
    public void A_pass_that_could_not_read_part_of_its_area_never_reports_it_empty()
    {
        var clean = new AreaScanReport(AreaScanOutcome.Empty, 10, 0, 10, 0, false);
        var withUnreadable = new AreaScanReport(AreaScanOutcome.Empty, 10, 0, 9, 0, false, unreadable: 1);

        Assert.True(clean.IsConclusive);

        // One point nobody could read is the difference between "there is
        // nothing there" and "there is nothing there that I could see", and a
        // finished job is claimed on the first.
        Assert.False(withUnreadable.IsConclusive);
    }

    [Fact]
    public void The_counters_have_to_add_up_including_the_ones_nobody_could_read()
    {
        Assert.True(new AreaScanReport(AreaScanOutcome.Empty, 10, 2, 5, 2, false, unreadable: 1).CountsAgree);

        // Eleven outcomes from ten candidates is a counting bug, and a counting
        // bug here is how an area gets called empty.
        Assert.False(new AreaScanReport(AreaScanOutcome.Empty, 10, 2, 5, 2, false, unreadable: 2).CountsAgree);
    }

    [Fact]
    public void A_sweep_reports_what_it_could_not_read_as_unreadable_not_as_unreached()
    {
        NpcCircleWorkArea area = Areas.Circle(0f, 0f, 20f);

        NpcAreaSweepPass unreadable =
            new NpcAreaSweep(area, RecordingProbe.Answering(AreaSampleVerdict.Unreadable), null, rings: 3, spokes: 4)
                .Next(100);
        NpcAreaSweepPass unreached =
            new NpcAreaSweep(area, RecordingProbe.Answering(AreaSampleVerdict.NotLoaded), null, rings: 3, spokes: 4)
                .Next(100);

        // Both are unknown, so both give the same outcome and neither is
        // conclusive - a role acts identically on them.
        Assert.Equal(AreaScanOutcome.NotLoaded, unreadable.Report.Outcome);
        Assert.Equal(AreaScanOutcome.NotLoaded, unreached.Report.Outcome);
        Assert.False(unreadable.Report.IsConclusive);
        Assert.False(unreached.Report.IsConclusive);

        // But the report says which, because walking over there fixes one of
        // them and nothing about the other.
        Assert.True(unreadable.Report.Unreadable > 0);
        Assert.Equal(0, unreadable.Report.NotLoaded);
        Assert.True(unreached.Report.NotLoaded > 0);
        Assert.Equal(0, unreached.Report.Unreadable);
    }
}
