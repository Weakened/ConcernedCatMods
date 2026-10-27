using System;
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
    public void BottomModeAimedAtTheRailsStacksOnTheColumnTop()
    {
        // Owner session 2026-10-26: Bottom mode aimed at a 34-38 column left a ghost overlapping it.
        Assert.True(LadderSnap.TryExtendColumn(35.5f, P(220f, 34f, -276.47f), P(220f, 38f, -276.47f), LadderSnap.Extend.Above, false,
            out ClimbPoint origin, out bool above));
        Assert.True(above);
        Assert.Equal(220f, origin.X, 4); Assert.Equal(38f, origin.Y, 4); Assert.Equal(-276.47f, origin.Z, 4);
    }

    [Fact]
    public void TopModeHangsTheNewSectionBelowTheColumn()
    {
        Assert.True(LadderSnap.TryExtendColumn(37.5f, P(220f, 34f, -276.47f), P(220f, 38f, -276.47f), LadderSnap.Extend.Below, true,
            out ClimbPoint origin, out bool above));
        Assert.False(above);
        Assert.Equal(32f, origin.Y, 4);
    }

    [Theory]
    [InlineData(34.3f, false)] // Aimed low on the column: the nearer end is the bottom.
    [InlineData(37.2f, true)]  // Aimed high: the top.
    public void AutoExtendsTheNearerEnd(float aim, bool expectedAbove)
    {
        Assert.True(LadderSnap.TryExtendColumn(aim, P(220f, 34f, -276.47f), P(220f, 40f, -276.47f), LadderSnap.Extend.Nearest, false,
            out _, out bool above));
        Assert.Equal(expectedAbove, above);
    }

    [Fact]
    public void AutoNeverBuriesASectionUnderAColumnThatStandsOnTheGround()
    {
        // In game 2026-10-26: Auto aimed at the foot of a ladder on the ground hung the new section into the terrain.
        Assert.True(LadderSnap.TryExtendColumn(34.6f, P(220f, 34.52f, -276.23f), P(220f, 38.52f, -276.23f), LadderSnap.Extend.Nearest, true,
            out ClimbPoint origin, out bool above));
        Assert.True(above);
        Assert.Equal(38.52f, origin.Y, 4);
    }

    [Theory]
    [InlineData(float.NaN, 38f)]
    [InlineData(35f, 35f)] // Not a measured column.
    public void AnUnmeasurableAimOrColumnStaysNative(float aim, float top) =>
        Assert.False(LadderSnap.TryExtendColumn(aim, P(220f, 34f, -276.47f), P(220f, top, -276.47f), LadderSnap.Extend.Nearest, false, out _, out _));

    [Theory]
    [InlineData(38f, true)]   // Column top 2.3 m above the eye: reachable.
    [InlineData(43.5f, false)] // Aimed at the lower rails of a tall column whose top is 7.8 m up: refused.
    [InlineData(float.NaN, false)]
    public void ExtensionHonoursTheGamesPlacementReach(float jointHeight, bool expected) =>
        Assert.Equal(expected, LadderSnap.WithinPlacementReach(P(220f, 35.7f, -279f), P(220f, jointHeight, -276.2f), 5f));

    [Theory]
    [InlineData(199f, 36f, -296.08f)] // The reproduced defect: a corner-to-corner snap one metre off the column.
    [InlineData(200f, 34f, -296f)]    // Bottom on bottom is an overlap, not a stack.
    [InlineData(200.05f, 36f, -296f)] // Five centimetres off is not the game's exact snap.
    public void AnythingButAnExactCentreToCentreStackIsLeftToTheGame(float x, float y, float z) =>
        Assert.False(LadderSnap.TryAlignStack(P(x, y, z), P(x, y + 2f, z), P(200f, 34f, -296f), P(200f, 36f, -296f), out _, out _));
}
