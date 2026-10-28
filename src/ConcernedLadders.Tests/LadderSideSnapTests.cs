using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests;

/// <summary>Owner revision 2026-10-26: Left side and Right side put the ladder's visible left or right rail on the
/// target's left or right corner (left and right as seen by a player facing the climbing side), against the real face;
/// and a snapped ladder keeps the player's build rotation (by quarter turns) about the selected anchor. Corner layouts
/// are the real ones read from the game (cla_snaps, Valheim 1.0.20).</summary>
public sealed class LadderSideSnapTests
{
    private static ClimbPoint P(float x, float y, float z) => new ClimbPoint(x, y, z);

    private static readonly IReadOnlyList<ClimbPoint> WoodFloor = new[] { P(1f, 0f, -1f), P(-1f, 0f, -1f), P(1f, 0f, 1f), P(-1f, 0f, 1f) };
    private static readonly IReadOnlyList<ClimbPoint> WoodWall = new[] { P(1f, 1f, 0f), P(-1f, 1f, 0f), P(1f, -1f, 0f), P(-1f, -1f, 0f) };
    private static readonly IReadOnlyList<ClimbPoint> StoneWall = new[]
    {
        P(1f, 0.5f, 0.5f), P(1f, 0.5f, -0.5f), P(1f, -0.5f, 0.5f), P(1f, -0.5f, -0.5f),
        P(-1f, 0.5f, 0.5f), P(-1f, 0.5f, -0.5f), P(-1f, -0.5f, 0.5f), P(-1f, -0.5f, -0.5f),
    };

    private static LadderEdgeRequest Request(IReadOnlyList<ClimbPoint> corners, ClimbPoint aim, ClimbPoint viewer, LadderSnapMode mode) =>
        new LadderEdgeRequest { Corners = corners, Aim = aim, Viewer = viewer, Mode = mode, Footprint = LadderFootprint.Section };

    private static LadderEdgeRequest Turned(LadderEdgeRequest request, float yawDegrees)
    {
        double yaw = yawDegrees * Math.PI / 180.0;
        request.HasPlayerFacing = true;
        request.PlayerFacingX = (float)Math.Sin(yaw);
        request.PlayerFacingZ = (float)Math.Cos(yaw);
        return request;
    }

    /// <summary>The ladder footprint's four corners (rails' outer edges, back and front) where the placement put them.</summary>
    private static (float x, float z)[] FootprintCorners(in LadderEdgePlacement placement, LadderFootprint footprint)
    {
        float fx = placement.FacingX, fz = placement.FacingZ, rx = fz, rz = -fx;
        var corners = new (float x, float z)[4];
        int index = 0;
        foreach (float x in new[] { -footprint.HalfWidth, footprint.HalfWidth })
        {
            foreach (float z in new[] { footprint.Back, footprint.Front })
            {
                corners[index++] = (placement.Origin.X + x * rx + z * fx, placement.Origin.Z + x * rz + z * fz);
            }
        }

        return corners;
    }

    [Theory]
    [InlineData((int)LadderSnapMode.LeftSide, 0.6f)]
    [InlineData((int)LadderSnapMode.RightSide, -0.6f)]
    public void OnAFloorTheChosenRailMeetsThatCornerOfTheEdge(int modeValue, float originX)
    {
        // Standing outside the +Z edge looking at it (along -Z), the player's left is +X.
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodFloor, P(0.2f, 0f, 0.9f), P(0f, -3f, 5f), (LadderSnapMode)modeValue), out var placement));
        Assert.Equal(originX, placement.Origin.X, 4);
        Assert.Equal(-2f, placement.Origin.Y, 4); // Hangs below the edge, like Auto.
        Assert.Equal(1f + 0.01f - 0.08f, placement.Origin.Z, 4); // Rails' back 1 cm outside the floor's edge.
        Assert.Equal(0f, placement.FacingX, 4); Assert.Equal(1f, placement.FacingZ, 4);
        Assert.Equal(originX > 0f ? 1f : -1f, placement.Joint.X, 4); // The joint is the corner itself.
        // The ladder's outer rail edge on that side is exactly at the corner; the other rail is 0.8 m inward.
        float outer = originX > 0f ? placement.Origin.X + 0.4f : placement.Origin.X - 0.4f;
        Assert.Equal(originX > 0f ? 1f : -1f, outer, 4);
    }

    [Fact]
    public void LeftAndRightFollowTheViewerOnEitherFaceOfAWall()
    {
        // From +Z (looking along -Z) the player's left is +X; from -Z it is -X.
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.LeftSide), out var front));
        Assert.Equal(0.6f, front.Origin.X, 4);
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodWall, P(0f, 0f, -0.15f), P(0f, 0.5f, -5f), LadderSnapMode.LeftSide), out var back));
        Assert.Equal(-0.6f, back.Origin.X, 4);
        Assert.Equal(-1f, back.FacingZ, 4);
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodWall, P(0f, 0f, -0.15f), P(0f, 0.5f, -5f), LadderSnapMode.RightSide), out var backRight));
        Assert.Equal(0.6f, backRight.Origin.X, 4);
        // Side modes stand on the wall's bottom edge, like Auto.
        Assert.Equal(-1f, front.Origin.Y, 4);
        Assert.Equal(-1f, front.Joint.Y, 4);
    }

    [Fact]
    public void AWallWhoseCornersLieOnItsCentrePlaneUsesTheAimedBodyFace()
    {
        // A wood wall's corner snap points sit on its centre plane; its face is 0.15 m out. Aimed at the wall's top
        // (the hit carries no depth), the collider's bounds still put the rails' back on the face, not inside it.
        LadderEdgeRequest request = Request(WoodWall, P(0.3f, 1f, 0f), P(0f, 0.5f, 5f), LadderSnapMode.RightSide);
        request.HasBody = true;
        request.BodyMin = P(-1f, -1f, -0.15f);
        request.BodyMax = P(1f, 1f, 0.15f);
        Assert.True(LadderEdgeSnap.TryPlace(request, out var placement));
        Assert.Equal(0.15f + 0.01f - 0.08f, placement.Origin.Z, 4);
        Assert.Equal(-0.6f, placement.Origin.X, 4);
        Assert.Equal(0.15f, placement.Joint.Z, 4);
    }

    [Fact]
    public void WithoutABodyTheCentrePlaneCornersAloneWouldBuryTheRails()
    {
        // The reason the body face is used: the same aim without bounds rests on the centre plane.
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodWall, P(0.3f, 1f, 0f), P(0f, 0.5f, 5f), LadderSnapMode.RightSide), out var placement));
        Assert.Equal(0.01f - 0.08f, placement.Origin.Z, 4);
    }

    [Fact]
    public void ABodyFarProudOfTheCornersIsNotTakenForTheFace()
    {
        LadderEdgeRequest request = Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.Auto);
        request.HasBody = true;
        request.BodyMin = P(-1f, -1f, -2f);
        request.BodyMax = P(1f, 1f, 2f); // A stray big collider: beyond MaximumBodyOverhang.
        Assert.True(LadderEdgeSnap.TryPlace(request, out var placement));
        Assert.Equal(0.15f + 0.01f - 0.08f, placement.Origin.Z, 4); // The aimed face stands.
    }

    [Theory]
    [InlineData((int)LadderSnapMode.LeftSide, 0.6f)]
    [InlineData((int)LadderSnapMode.RightSide, -0.6f)]
    public void AThickStoneWallTakesTheRailOnItsCornerAgainstItsOwnFace(int modeValue, float originX)
    {
        Assert.True(LadderEdgeSnap.TryPlace(Request(StoneWall, P(0f, 0f, 0.5f), P(0f, 0f, 4f), (LadderSnapMode)modeValue), out var placement));
        Assert.Equal(originX, placement.Origin.X, 4);
        Assert.Equal(0.5f + 0.01f - 0.08f, placement.Origin.Z, 4);
        Assert.Equal(-0.5f, placement.Origin.Y, 4);
    }

    [Fact]
    public void AStoneWallAlongZUsesTheSameViewerFrame()
    {
        // The same wall turned a quarter: it runs along Z and the player stands at +X looking along -X; their left
        // is -Z.
        var alongZ = new List<ClimbPoint>();
        foreach (ClimbPoint p in StoneWall) { alongZ.Add(P(p.Z, p.Y, p.X)); }
        Assert.True(LadderEdgeSnap.TryPlace(Request(alongZ, P(0.5f, 0f, 0f), P(4f, 0f, 0f), LadderSnapMode.LeftSide), out var placement));
        Assert.Equal(1f, placement.FacingX, 4);
        Assert.Equal(0.43f, placement.Origin.X, 4);
        Assert.Equal(-0.6f, placement.Origin.Z, 4);
    }

    [Fact]
    public void SideModesStandOnAFloorEdgeWhenToldThereIsNoRoomBelow()
    {
        LadderEdgeRequest request = Request(WoodFloor, P(0.9f, 0f, 0f), P(5f, 0f, 0f), LadderSnapMode.LeftSide);
        request.StandOnFloorEdge = true;
        Assert.True(LadderEdgeSnap.TryPlace(request, out var placement));
        Assert.Equal(0f, placement.Origin.Y, 4);
        // Outside the +X edge looking along -X, the left is -Z.
        Assert.Equal(-0.6f, placement.Origin.Z, 4);
        Assert.Equal(1f - 0.07f, placement.Origin.X, 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(90f, 0f)]
    [InlineData(180f, 0f)]
    [InlineData(-90f, 0f)]
    [InlineData(22.5f, 22.5f)]
    [InlineData(-22.5f, -22.5f)]
    [InlineData(112.5f, 22.5f)]
    [InlineData(45f, -45f)]
    [InlineData(-45f, -45f)]
    [InlineData(157.5f, -22.5f)]
    [InlineData(0.3f, 0f)]
    public void TheBuildRotationIsKeptByQuarterTurnsAndNeverFacesTheSurface(float playerYaw, float expectedTurn)
    {
        Assert.True(LadderEdgeSnap.TryPlace(Turned(Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.Auto), playerYaw), out var placement));
        Assert.Equal(expectedTurn, placement.TurnDegrees, 3);
        double yaw = Math.Atan2(placement.FacingX, placement.FacingZ) * 180.0 / Math.PI;
        Assert.Equal(expectedTurn, (float)yaw, 3);
        Assert.True(placement.FacingZ > 0.7f); // Within 45 degrees of straight out of the +Z face.
    }

    [Theory]
    [InlineData((int)LadderSnapMode.Auto)]
    [InlineData((int)LadderSnapMode.Bottom)]
    [InlineData((int)LadderSnapMode.LeftSide)]
    [InlineData((int)LadderSnapMode.RightSide)]
    public void ARotatedLadderStillRestsOnTheFaceAndKeepsItsAnchor(int modeValue)
    {
        var mode = (LadderSnapMode)modeValue;
        foreach (float yaw in new[] { 22.5f, -22.5f, 45f, -45f })
        {
            LadderEdgeRequest request = Turned(Request(StoneWall, P(0f, 0f, 0.5f), P(0f, 0f, 4f), mode), yaw);
            Assert.True(LadderEdgeSnap.TryPlace(request, out var placement));
            var corners = FootprintCorners(placement, request.Footprint);
            float deepest = float.MaxValue, left = float.MaxValue, right = float.MinValue;
            foreach (var (x, z) in corners)
            {
                deepest = Math.Min(deepest, z);
                // Along the viewer's right (-X here, the player looks along -Z).
                left = Math.Min(left, -x);
                right = Math.Max(right, -x);
            }

            // The deepest footprint corner, not the centre, rests 1 cm off the face: nothing sinks into the wall.
            Assert.Equal(0.5f + 0.01f, deepest, 4);
            if (mode == LadderSnapMode.LeftSide) { Assert.Equal(-1f, left, 4); }
            else if (mode == LadderSnapMode.RightSide) { Assert.Equal(1f, right, 4); }
            else { Assert.Equal(0f, (left + right) * 0.5f, 4); }
            Assert.Equal(-0.5f, placement.Origin.Y, 4);
        }
    }

    [Fact]
    public void ARotatedLadderOnAFloorEdgeStaysOutsideTheEdge()
    {
        LadderEdgeRequest request = Turned(Request(WoodFloor, P(0f, 0f, 0.95f), P(0f, -3f, 5f), LadderSnapMode.RightSide), 22.5f);
        Assert.True(LadderEdgeSnap.TryPlace(request, out var placement));
        float deepest = float.MaxValue, right = float.MinValue;
        foreach (var (x, z) in FootprintCorners(placement, request.Footprint))
        {
            deepest = Math.Min(deepest, z);
            right = Math.Max(right, -x);
        }

        Assert.Equal(1.01f, deepest, 4);
        Assert.Equal(1f, right, 4); // The right corner (-X from outside +Z) is the anchor.
        Assert.Equal(22.5f, placement.TurnDegrees, 3);
    }

    [Fact]
    public void WithoutAPlayerRotationTheLadderFacesStraightOut()
    {
        Assert.True(LadderEdgeSnap.TryPlace(Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.LeftSide), out var placement));
        Assert.Equal(0f, placement.TurnDegrees);
        Assert.Equal(0f, placement.FacingX); Assert.Equal(1f, placement.FacingZ);
    }

    [Fact]
    public void NonFiniteInputsAreRefused()
    {
        LadderEdgeRequest request = Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.LeftSide);
        request.HasBody = true;
        request.BodyMin = P(float.NaN, 0f, 0f);
        request.BodyMax = P(1f, 1f, 1f);
        Assert.False(LadderEdgeSnap.TryPlace(request, out _));
        Assert.False(LadderEdgeSnap.TryPlace(Request(WoodWall, P(float.PositiveInfinity, 0f, 0f), P(0f, 0f, 5f), LadderSnapMode.Auto), out _));
        LadderEdgeRequest badFootprint = Request(WoodWall, P(0f, 0f, 0.15f), P(0f, 0.5f, 5f), LadderSnapMode.Auto);
        badFootprint.Footprint = new LadderFootprint(0.4f, 0.3f, 0.2f);
        Assert.False(LadderEdgeSnap.TryPlace(badFootprint, out _));
    }

    [Fact]
    public void SnapPointsNameTheirModes()
    {
        Assert.True(LadderSnapPoints.TryModeOf(P(0f, 0f, 0f), out var bottom)); Assert.Equal(LadderSnapMode.Bottom, bottom);
        Assert.True(LadderSnapPoints.TryModeOf(P(0f, 2f, 0f), out var top)); Assert.Equal(LadderSnapMode.Top, top);
        Assert.True(LadderSnapPoints.TryModeOf(P(0f, -100f, 0f), out var left)); Assert.Equal(LadderSnapMode.LeftSide, left);
        Assert.True(LadderSnapPoints.TryModeOf(P(0f, -200f, 0f), out var right)); Assert.Equal(LadderSnapMode.RightSide, right);
        // The removed 0.2.4 wall-corner points name nothing.
        Assert.False(LadderSnapPoints.TryModeOf(P(-1f, 0f, -0.08f), out _));
        Assert.False(LadderSnapPoints.TryModeOf(P(1f, 0f, -0.08f), out _));
        Assert.True(LadderSnapPoints.IsSide(LadderSnapMode.LeftSide));
        Assert.False(LadderSnapPoints.IsSide(LadderSnapMode.Auto));
    }

    [Fact]
    public void SideSnapPointsCannotPairInTheGamesSnapSearch()
    {
        // The game pairs points within 0.5 m among pieces within 10 m of the ghost. A side point is 100 m below its
        // section, and the two side points are 100 m apart vertically, with the centre points (2 m apart) on the
        // section: no side point can come within reach of a floor, a wall, a centre point or the other side point
        // of any ladder the search can see; only the same point of a ladder within 0.5 m, which is the same pairing
        // the centre points already make.
        ClimbPoint[] centre = { LadderSnapPoints.Bottom, LadderSnapPoints.Top };
        ClimbPoint[] side = { LadderSnapPoints.LeftSide, LadderSnapPoints.RightSide };
        foreach (ClimbPoint s in side)
        {
            Assert.Equal(0f, s.X); Assert.Equal(0f, s.Z); // Rotation about Y never moves it sideways.
            foreach (ClimbPoint c in centre) { Assert.True(c.Y - s.Y >= 100f); }
        }

        Assert.True(Math.Abs(LadderSnapPoints.LeftSide.Y - LadderSnapPoints.RightSide.Y) >= 100f);
    }
}
