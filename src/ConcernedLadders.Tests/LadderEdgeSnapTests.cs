using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner decision 2026-10-26: Auto, Top and Bottom put a ladder in the middle of floors and walls, the way
/// walls snap to floors. Corner layouts are the real ones read from the game (cla_snaps, Valheim 1.0.20).</summary>
public sealed class LadderEdgeSnapTests
{
    private static ClimbPoint P(float x, float y, float z) => new ClimbPoint(x, y, z);

    private static readonly IReadOnlyList<ClimbPoint> WoodFloor = new[] { P(1f, 0f, -1f), P(-1f, 0f, -1f), P(1f, 0f, 1f), P(-1f, 0f, 1f) };
    private static readonly IReadOnlyList<ClimbPoint> WoodWall = new[] { P(1f, 1f, 0f), P(-1f, 1f, 0f), P(1f, -1f, 0f), P(-1f, -1f, 0f) };
    private static readonly IReadOnlyList<ClimbPoint> HalfWall = new[] { P(1f, 0.5f, 0f), P(-1f, 0.5f, 0f), P(1f, -0.5f, 0f), P(-1f, -0.5f, 0f) };
    private static readonly IReadOnlyList<ClimbPoint> StoneWall = new[]
    {
        P(1f, 0.5f, 0.5f), P(1f, 0.5f, -0.5f), P(1f, -0.5f, 0.5f), P(1f, -0.5f, -0.5f),
        P(-1f, 0.5f, 0.5f), P(-1f, 0.5f, -0.5f), P(-1f, -0.5f, 0.5f), P(-1f, -0.5f, -0.5f),
    };
    private static readonly IReadOnlyList<ClimbPoint> StoneFloor = new[]
    {
        P(1f, 0.5f, 1f), P(1f, -0.5f, 1f), P(1f, 0.5f, -1f), P(1f, -0.5f, -1f),
        P(-1f, 0.5f, 1f), P(-1f, -0.5f, 1f), P(-1f, 0.5f, -1f), P(-1f, -0.5f, -1f),
    };
    private static readonly IReadOnlyList<ClimbPoint> Beam = new[] { P(-1f, 0f, 0f), P(1f, 0f, 0f) };

    [Theory]
    [InlineData(0, -2f)]
    [InlineData(2, -2f)]
    [InlineData(1, 0f)]
    public void AFloorTakesTheLadderInTheMiddleOfTheNearestEdgeFacingOut(int modeValue, float originY)
    {
        // Aimed near the +Z edge, off-centre: the ladder still centres on that edge.
        Assert.True(LadderEdgeSnap.TryPlace(WoodFloor, P(0.6f, 0f, 0.9f), P(0f, -3f, 5f), (LadderSnapMode)modeValue,
            out ClimbPoint origin, out float fx, out float fz, out ClimbPoint joint));
        Assert.Equal(0f, origin.X, 4);
        Assert.Equal(originY, origin.Y, 4);
        Assert.Equal(1f - 0.07f, origin.Z, 4); // Rails' back 1 cm outside the floor's edge.
        Assert.Equal(0f, fx, 4); Assert.Equal(1f, fz, 4);
        Assert.Equal(0f, joint.X, 4); Assert.Equal(1f, joint.Z, 4);
    }

    [Fact]
    public void EachFloorEdgeIsAvailable()
    {
        Assert.True(LadderEdgeSnap.TryPlace(WoodFloor, P(-0.95f, 0f, 0.3f), P(-5f, 0f, 0f), LadderSnapMode.Auto, out var origin, out float fx, out float fz, out _));
        Assert.Equal(-1f + 0.07f, origin.X, 4); Assert.Equal(0f, origin.Z, 4);
        Assert.Equal(-1f, fx, 4); Assert.Equal(0f, fz, 4);
    }

    [Fact]
    public void AStoneFloorUsesItsTopLevel()
    {
        Assert.True(LadderEdgeSnap.TryPlace(StoneFloor, P(0f, 0.5f, -0.9f), P(0f, 0f, -5f), LadderSnapMode.Top, out var origin, out _, out float fz, out var joint));
        Assert.Equal(0.5f, joint.Y, 4);
        Assert.Equal(-1.5f, origin.Y, 4);
        Assert.Equal(-1f, fz, 4);
    }

    [Theory]
    [InlineData(5f, 1f)]
    [InlineData(-5f, -1f)]
    public void AWallTakesTheLadderCentredOnTheFaceTowardTheViewer(float viewerZ, float side)
    {
        // A wood wall's corners sit on its centre plane; the aimed point is on the 0.15 m face.
        Assert.True(LadderEdgeSnap.TryPlace(WoodWall, P(0.4f, 0f, 0.15f * side), P(0f, 0.5f, viewerZ), LadderSnapMode.Auto,
            out ClimbPoint origin, out float fx, out float fz, out ClimbPoint joint));
        Assert.Equal(0f, origin.X, 4);
        Assert.Equal(-1f, origin.Y, 4); // Standing on the wall's bottom edge.
        Assert.Equal((0.15f - 0.07f) * side, origin.Z, 4);
        Assert.Equal(0f, fx, 4); Assert.Equal(side, fz, 4);
        Assert.Equal(-1f, joint.Y, 4);
    }

    [Fact]
    public void TopModeHangsTheLadderFromTheWallsTopEdge()
    {
        Assert.True(LadderEdgeSnap.TryPlace(HalfWall, P(0f, 0f, 0.15f), P(0f, 0f, 4f), LadderSnapMode.Top, out var origin, out _, out _, out var joint));
        Assert.Equal(0.5f - 2f, origin.Y, 4);
        Assert.Equal(0.5f, joint.Y, 4);
    }

    [Fact]
    public void AThickWallUsesItsOwnFace()
    {
        Assert.True(LadderEdgeSnap.TryPlace(StoneWall, P(0f, 0f, 0.5f), P(0f, 0f, 4f), LadderSnapMode.Bottom, out var origin, out _, out _, out _));
        Assert.Equal(0.5f - 0.07f, origin.Z, 4);
        Assert.Equal(-0.5f, origin.Y, 4);
    }

    [Fact]
    public void ABeamIsNeitherFloorNorWall() =>
        Assert.False(LadderEdgeSnap.TryPlace(Beam, P(0f, 0f, 0f), P(0f, 0f, 4f), LadderSnapMode.Auto, out _, out _, out _, out _));
}
