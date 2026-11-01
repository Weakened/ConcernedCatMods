using TheConcernedCat.ConcernedForeman.Domain.Npc;

namespace ConcernedForeman.Tests;

/// <summary>#295 S4: the tools panel greys out what has nothing to do, and a
/// give moves nothing until it is clicked twice for the same tool.</summary>
public sealed class ThorsteinToolsPanelTests
{
    private static ThorsteinToolsPanelButtons Buttons(
        bool present = true, bool axe = false, bool hammer = false, bool uncertain = false) =>
        ThorsteinToolsPanelPlan.Decide(new ThorsteinToolsPanelFacts(present, axe, hammer, uncertain));

    [Fact]
    public void Without_his_worker_body_only_bringing_him_over_is_offered()
    {
        ThorsteinToolsPanelButtons b = Buttons(present: false);

        Assert.True(b.BringHim);
        Assert.False(b.SendHome || b.GiveAxe || b.GiveHammer || b.TakeAxe || b.TakeHammer);
    }

    [Fact]
    public void Give_is_offered_for_what_he_lacks_and_take_back_for_what_he_holds()
    {
        ThorsteinToolsPanelButtons b = Buttons(axe: true);

        Assert.False(b.GiveAxe);
        Assert.True(b.GiveHammer);
        Assert.True(b.TakeAxe);
        Assert.False(b.TakeHammer);
        Assert.True(b.SendHome);
        Assert.False(b.BringHim);
    }

    [Fact]
    public void An_unresolved_handover_moves_nothing_more()
    {
        ThorsteinToolsPanelButtons b = Buttons(axe: true, uncertain: true);

        Assert.False(b.GiveAxe || b.GiveHammer || b.TakeAxe || b.TakeHammer);
        Assert.True(b.SendHome);
    }

    [Fact]
    public void A_give_needs_a_second_click_on_the_same_tool()
    {
        Assert.False(ThorsteinToolsPanelPlan.IsConfirmation(null, "axe"));
        Assert.False(ThorsteinToolsPanelPlan.IsConfirmation("hammer", "axe"));
        Assert.True(ThorsteinToolsPanelPlan.IsConfirmation("axe", "axe"));
    }
}
