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

    [Fact]
    public void AGhostAimedAtTheLadderBodyExtendsTheColumnAbove()
    {
        // Owner session 2026-10-26: Bottom mode, ghost origin at 36.4 on a 34-38 column: it overlapped the ladder.
        Assert.True(LadderSnap.TryExtendColumn(P(220.05f, 36.4f, -276.47f), P(220f, 34f, -276.47f), P(220f, 38f, -276.47f), 0.4f,
            LadderSnap.Extend.Above, out ClimbPoint origin, out bool above));
        Assert.True(above);
        Assert.Equal(220f, origin.X, 4); Assert.Equal(38f, origin.Y, 4);
    }

    [Fact]
    public void TopModeHangsTheNewSectionBelowTheColumn()
    {
        Assert.True(LadderSnap.TryExtendColumn(P(220f, 37.5f, -276.47f), P(220f, 34f, -276.47f), P(220f, 38f, -276.47f), 0.4f,
            LadderSnap.Extend.Below, out ClimbPoint origin, out bool above));
        Assert.False(above);
        Assert.Equal(32f, origin.Y, 4);
    }

    [Theory]
    [InlineData(34.3f, false)] // Low on the column: the nearer end is the bottom.
    [InlineData(37.2f, true)]  // High on the column: the top.
    public void AutoExtendsTheNearerEnd(float y, bool expectedAbove)
    {
        Assert.True(LadderSnap.TryExtendColumn(P(220f, y, -276.47f), P(220f, 34f, -276.47f), P(220f, 40f, -276.47f), 0.4f,
            LadderSnap.Extend.Nearest, out _, out bool above));
        Assert.Equal(expectedAbove, above);
    }

    [Theory]
    [InlineData(220.6f, 36f)] // Beside the column, not on it: native placement.
    [InlineData(220f, 38f)]   // Already exactly on top: no overlap, the stack rule handles it.
    [InlineData(220f, 31.5f)] // Clear below the column.
    public void AGhostNotOverlappingTheColumnStaysNative(float x, float y) =>
        Assert.False(LadderSnap.TryExtendColumn(P(x, y, -276.47f), P(220f, 34f, -276.47f), P(220f, 38f, -276.47f), 0.4f,
            LadderSnap.Extend.Nearest, out _, out _));

    [Theory]
    [InlineData(199f, 36f, -296.08f)] // The reproduced defect: a corner-to-corner snap one metre off the column.
    [InlineData(200f, 34f, -296f)]    // Bottom on bottom is an overlap, not a stack.
    [InlineData(200.05f, 36f, -296f)] // Five centimetres off is not the game's exact snap.
    public void AnythingButAnExactCentreToCentreStackIsLeftToTheGame(float x, float y, float z) =>
        Assert.False(LadderSnap.TryAlignStack(P(x, y, z), P(x, y + 2f, z), P(200f, 34f, -296f), P(200f, 36f, -296f), out _, out _));
}
