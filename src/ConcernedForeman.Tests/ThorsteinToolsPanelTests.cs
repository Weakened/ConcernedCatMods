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

    [Fact]
    public void Only_a_preview_that_named_a_usable_tool_arms_the_second_click()
    {
        // S4 review B2: a refusal ("hold the axe first", "it is broken") must not
        // arm, or the next click would give a tool the player never saw named.
        const string prefix = "Give him your ";
        Assert.Equal("axe", ThorsteinToolsPanelPlan.ArmAfterPreview("axe", "Give him your Axe (quality 1, ...", prefix));
        Assert.Null(ThorsteinToolsPanelPlan.ArmAfterPreview("axe", "Hold the axe you want to give him in your hand first.", prefix));
        Assert.Null(ThorsteinToolsPanelPlan.ArmAfterPreview("axe", "Your Axe is broken, so he could not work with it.", prefix));
        Assert.Null(ThorsteinToolsPanelPlan.ArmAfterPreview("axe", null, prefix));
    }

    [Fact]
    public void An_armed_give_is_dropped_when_its_button_has_nothing_to_do()
    {
        // S4 review B2: a give made from the console between the two clicks must
        // not leave a one-click give behind.
        ThorsteinToolsPanelButtons axeGiven = Buttons(axe: true);
        Assert.Null(ThorsteinToolsPanelPlan.KeepArmed("axe", axeGiven));
        Assert.Equal("hammer", ThorsteinToolsPanelPlan.KeepArmed("hammer", axeGiven));
        Assert.Null(ThorsteinToolsPanelPlan.KeepArmed("hammer", Buttons(present: false)));
        Assert.Null(ThorsteinToolsPanelPlan.KeepArmed(null, Buttons()));
    }
}
