using TheConcernedCat.ConcernedTeamster.Domain.Brake;

namespace ConcernedTeamster.Tests;

/// <summary>CT-012: the complete engage/release matrix. Engage requires an
/// explicit toggle plus every eligibility fact; every degraded fact
/// releases an engaged brake; no reachable state keeps the brake engaged
/// without its lifecycle owner confirming fresh facts.</summary>
public class BrakeLifecycleTests
{
    private static BrakeFacts Facts(
        bool capability = true,
        bool inWorld = true,
        bool exists = true,
        bool authority = true,
        bool attached = false,
        float distance = 2f)
    {
        return new BrakeFacts(capability, inWorld, exists, authority, attached, distance);
    }

    private static BrakeLifecycle Engaged(string cartId = "1:1")
    {
        var lifecycle = new BrakeLifecycle();
        Assert.Equal(BrakeAction.Engage, lifecycle.EvaluateToggle(cartId, Facts(), out _));
        lifecycle.MarkEngaged(cartId);
        return lifecycle;
    }

    // -- engage eligibility ---------------------------------------------

    [Fact]
    public void Toggle_AllFactsGood_Engages()
    {
        var lifecycle = new BrakeLifecycle();

        BrakeAction action = lifecycle.EvaluateToggle("1:1", Facts(), out string reason);

        Assert.Equal(BrakeAction.Engage, action);
        Assert.Equal("engaged by player", reason);
        Assert.False(lifecycle.IsEngaged); // only MarkEngaged confirms
    }

    [Theory]
    [InlineData(false, true, true, true, false, 2f, "capability")]
    [InlineData(true, false, true, true, false, 2f, "player")]
    [InlineData(true, true, false, true, false, 2f, "exists")]
    [InlineData(true, true, true, false, false, 2f, "control")]
    [InlineData(true, true, true, true, true, 2f, "pulled")]
    [InlineData(true, true, true, true, false, 5.5f, "far")]
    public void Toggle_AnyMissingFact_RefusesToEngage(
        bool capability, bool inWorld, bool exists, bool authority, bool attached,
        float distance, string reasonFragment)
    {
        var lifecycle = new BrakeLifecycle();
        var facts = new BrakeFacts(capability, inWorld, exists, authority, attached, distance);

        BrakeAction action = lifecycle.EvaluateToggle("1:1", facts, out string reason);

        Assert.Equal(BrakeAction.None, action);
        Assert.Contains(reasonFragment, reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(lifecycle.IsEngaged);
    }

    [Fact]
    public void Toggle_AtExactlyTheEngageDistance_StillEngages()
    {
        var lifecycle = new BrakeLifecycle();

        Assert.Equal(BrakeAction.Engage, lifecycle.EvaluateToggle(
            "1:1", Facts(distance: BrakeLifecycle.EngageMaxDistanceMeters), out _));
    }

    // -- explicit release and one-brake rule ----------------------------

    [Fact]
    public void Toggle_OnTheEngagedCart_Releases()
    {
        BrakeLifecycle lifecycle = Engaged("1:1");

        BrakeAction action = lifecycle.EvaluateToggle("1:1", Facts(), out string reason);

        Assert.Equal(BrakeAction.Release, action);
        Assert.Equal("released by player", reason);
        lifecycle.MarkReleased();
        Assert.False(lifecycle.IsEngaged);
    }

    [Fact]
    public void Toggle_OnAnotherCartWhileEngaged_IsRefused()
    {
        BrakeLifecycle lifecycle = Engaged("1:1");

        BrakeAction action = lifecycle.EvaluateToggle("2:2", Facts(), out string reason);

        Assert.Equal(BrakeAction.None, action);
        Assert.Contains("another cart", reason);
        Assert.Equal("1:1", lifecycle.EngagedCartId);
    }

    // -- every automatic release path -----------------------------------

    [Theory]
    [InlineData(true, false, true, true, false, 2f, "left the world")]
    [InlineData(false, true, true, true, false, 2f, "capability lost")]
    [InlineData(true, true, false, true, false, 2f, "no longer exists")]
    [InlineData(true, true, true, false, false, 2f, "authority")]
    [InlineData(true, true, true, true, true, 2f, "grabbed")]
    [InlineData(true, true, true, true, false, 12.5f, "left the cart behind")]
    public void Tick_AnyDegradedFact_Releases(
        bool capability, bool inWorld, bool exists, bool authority, bool attached,
        float distance, string reasonFragment)
    {
        BrakeLifecycle lifecycle = Engaged();
        var facts = new BrakeFacts(capability, inWorld, exists, authority, attached, distance);

        BrakeAction action = lifecycle.EvaluateTick(facts, out string reason);

        Assert.Equal(BrakeAction.Release, action);
        Assert.Contains(reasonFragment, reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tick_FactsStillGood_StaysEngaged()
    {
        BrakeLifecycle lifecycle = Engaged();

        Assert.Equal(BrakeAction.None, lifecycle.EvaluateTick(
            Facts(distance: BrakeLifecycle.AutoReleaseDistanceMeters), out _));
        Assert.True(lifecycle.IsEngaged);
    }

    [Fact]
    public void Tick_WhileReleased_DoesNothing()
    {
        var lifecycle = new BrakeLifecycle();

        Assert.Equal(BrakeAction.None, lifecycle.EvaluateTick(BrakeFacts.Unavailable, out _));
    }

    [Fact]
    public void Tick_UnavailableFacts_ReleaseImmediately()
    {
        // The adapter's fail-closed facts (capability gone, nothing known)
        // must release — the fail-closed case of the acceptance criteria.
        BrakeLifecycle lifecycle = Engaged();

        Assert.Equal(BrakeAction.Release, lifecycle.EvaluateTick(BrakeFacts.Unavailable, out _));
    }

    // -- physics-failure discipline --------------------------------------

    [Fact]
    public void EngageIsOnlyConfirmedByMarkEngaged_FailedPhysicsLeavesReleased()
    {
        var lifecycle = new BrakeLifecycle();
        lifecycle.EvaluateToggle("1:1", Facts(), out _);

        // The service only calls MarkEngaged after TryEngage succeeded; if
        // the physics call fails, no Mark happens and the machine stays
        // released — no state believes a brake holds that does not.
        Assert.False(lifecycle.IsEngaged);
        Assert.Equal(BrakeAction.None, lifecycle.EvaluateTick(Facts(), out _));
    }

    [Fact]
    public void MarkReleased_AlwaysClearsEvenAfterCartDestruction()
    {
        BrakeLifecycle lifecycle = Engaged();
        Assert.Equal(BrakeAction.Release, lifecycle.EvaluateTick(
            Facts(exists: false), out _));

        // Release marking never depends on the restore succeeding — the
        // destroyed cart's constraints died with it.
        lifecycle.MarkReleased();
        Assert.False(lifecycle.IsEngaged);
        Assert.Null(lifecycle.EngagedCartId);
    }
}
