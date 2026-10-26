using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner correction 2026-10-26: a stacked section is one aligned column. Each test fails against 0.2.3, which
/// had no stack alignment at all (the ghost kept the hammer's last rotation).</summary>
public sealed class LadderSnapTests
{
    private static ClimbPoint P(float x, float y, float z) => new ClimbPoint(x, y, z);

    [Fact]
    public void AGhostSnappedOnTopTakesTheTopAsItsOrigin()
    {
        // Ghost rotated 112.5 degrees (as reproduced in game) but its bottom-centre already on the placed top-centre.
        Assert.True(LadderSnap.TryAlignStack(P(200f, 36f, -296f), P(200.3f, 38f, -296.2f), P(200f, 34f, -296f), P(200f, 36f, -296f),
            out ClimbPoint origin, out bool above));
        Assert.True(above);
        Assert.Equal(200f, origin.X, 4); Assert.Equal(36f, origin.Y, 4); Assert.Equal(-296f, origin.Z, 4);
    }

    [Fact]
    public void AGhostHungBelowEndsAtTheBottomOfThePlacedSection()
    {
        Assert.True(LadderSnap.TryAlignStack(P(5f, 6f, 1f), P(5f, 8f, 1f), P(5f, 8f, 1f), P(5f, 10f, 1f), out ClimbPoint origin, out bool above));
        Assert.False(above);
        Assert.Equal(6f, origin.Y, 4);
    }

    [Theory]
    [InlineData(199f, 36f, -296.08f)] // The reproduced defect: a corner-to-corner snap one metre off the column.
    [InlineData(200f, 34f, -296f)]    // Bottom on bottom is an overlap, not a stack.
    [InlineData(200.05f, 36f, -296f)] // Five centimetres off is not the game's exact snap.
    public void AnythingButAnExactCentreToCentreStackIsLeftToTheGame(float x, float y, float z) =>
        Assert.False(LadderSnap.TryAlignStack(P(x, y, z), P(x, y + 2f, z), P(200f, 34f, -296f), P(200f, 36f, -296f), out _, out _));
}
