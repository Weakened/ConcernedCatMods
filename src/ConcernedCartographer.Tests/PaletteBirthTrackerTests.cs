using TheConcernedCat.ConcernedCartographer.Atlas;

namespace ConcernedCartographer.Tests;

/// <summary>#96 Enhanced Pin Palette: the managed-from-birth state
/// machine. A palette-created pin must be claimed exactly once, when its
/// vanilla naming flow closes; pins created without an armed palette
/// selection are never claimed.</summary>
public class PaletteBirthTrackerTests
{
    private sealed class FakePin
    {
    }

    [Fact]
    public void ArmedBirth_IsClaimedExactlyOnce_WhenNamingCloses()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        tracker.Arm("cc:harbor", "Travel");
        var pin = new FakePin();

        Assert.Null(tracker.Observe(null));
        Assert.Null(tracker.Observe(pin));   // naming opened
        Assert.Null(tracker.Observe(pin));   // still typing
        Assert.Same(pin, tracker.Observe(null));  // naming closed -> born
        Assert.Null(tracker.Observe(null));  // claimed only once
        Assert.Equal("cc:harbor", tracker.IconId);
        Assert.Equal("Travel", tracker.Category);
    }

    [Fact]
    public void UnarmedBirth_IsNeverClaimed()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        var pin = new FakePin();

        Assert.Null(tracker.Observe(pin));
        Assert.Null(tracker.Observe(null));
    }

    [Fact]
    public void DisarmDuringNaming_CancelsTheClaim()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        tracker.Arm("vanilla:house", "Base");
        var pin = new FakePin();

        tracker.Observe(pin);
        tracker.Disarm();

        Assert.Null(tracker.Observe(null));
    }

    [Fact]
    public void ArmingMidNaming_DoesNotClaimAPrePaletteVanillaPin()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        var prePalette = new FakePin();

        tracker.Observe(prePalette);         // naming started while unarmed
        tracker.Arm("vanilla:fire", "Camp"); // player picks an icon mid-typing
        Assert.Null(tracker.Observe(prePalette));
        Assert.Null(tracker.Observe(null));  // that pin stays plain vanilla
    }

    [Fact]
    public void SameFrameSwap_ClaimsTheFirstAndTracksTheSecond()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        tracker.Arm("vanilla:house", "Base");
        var first = new FakePin();
        var second = new FakePin();

        tracker.Observe(first);
        Assert.Same(first, tracker.Observe(second)); // vanilla closed+reopened in one frame
        Assert.Same(second, tracker.Observe(null));
    }

    [Fact]
    public void RepeatedPlacements_EachClaimedOnce()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        tracker.Arm("cc:resource", "Resources");

        for (int index = 0; index < 3; index++)
        {
            var pin = new FakePin();
            Assert.Null(tracker.Observe(pin));
            Assert.Same(pin, tracker.Observe(null));
        }
    }

    [Fact]
    public void Reset_DropsInFlightState_KeepsTheArmedSelection()
    {
        var tracker = new PaletteBirthTracker<FakePin>();
        tracker.Arm("cc:danger", "Danger");
        tracker.Observe(new FakePin());

        tracker.Reset();

        Assert.Null(tracker.Observe(null));
        Assert.True(tracker.IsArmed);
        Assert.Equal("cc:danger", tracker.IconId);
    }
}
