using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-005 #486 slice 4, the ladder integration of the cellar doors, mirroring the trapdoor's (CL-004 #485 slice
/// 4, <see cref="HatchLadderTests"/>). The rules are game-free (<see cref="CellarLadder"/>, with the trapdoor's
/// <see cref="HatchLadder.AllowsRoute"/>, <see cref="HatchLadder.LegSpan"/> and <see cref="HatchLadder.IsLandingSurface"/>
/// reused) and tested directly against the cover's own geometry; the Unity adapters that ask them
/// (<c>LadderSnapAlignment</c>, <c>LadderRuntime</c>, <c>LadderClearance</c>, <c>CellarLeaves</c>) are pinned by source:
/// each asks in the place it must, and asks nothing while no cellar door exists, so with <c>CellarDoors/Enabled</c> off
/// every ladder path is the one it was before.</summary>
public sealed class CellarLadderTests
{
    // The installed game's player capsule (docs/mods/concerned-ladders/animation/NATIVE_ANIMATION_BASELINE.md).
    private const float Radius = 0.49f;
    private const float Height = 1.85f;
    private const float Margin = 0.05f;

    // LadderClearance: the query capsule floats this far over the feet and is swept this much thinner, and a landing
    // floats this far over the surface it found.
    private const float GroundClearance = 0.05f;
    private const float QuerySkin = 0.02f;
    private const float LandingSkin = 0.02f;

    private static float FrontLane => CellarGeometry.PlannedLadderOrigin.Z +
        ClimbPolicy.LaneDepth(LadderFace.Front, Radius, LadderFootprint.Section.Back, LadderFootprint.Section.Front);

    private static float RearLane => CellarGeometry.PlannedLadderOrigin.Z +
        ClimbPolicy.LaneDepth(LadderFace.Rear, Radius, LadderFootprint.Section.Back, LadderFootprint.Section.Front);

    /// <summary>Where the floor around the cover is stood on: the wood floor's top plus the landing skin.</summary>
    private static float FloorFeet => CellarGeometry.FloorTopY + LandingSkin;

    // ---- Anchor ----

    [Theory]
    [InlineData((int)LadderSnapMode.Auto, true)]
    [InlineData((int)LadderSnapMode.Top, true)]
    [InlineData((int)LadderSnapMode.Bottom, false)]
    [InlineData((int)LadderSnapMode.LeftSide, false)]
    [InlineData((int)LadderSnapMode.RightSide, false)]
    public void OnlyAutoAndTopHangALadderInTheCellarDoor(int mode, bool anchors) =>
        Assert.Equal(anchors, CellarLadder.UsesAnchor((LadderSnapMode)mode));

    [Fact]
    public void TheAnchorHangsTheLadderFromTheFrontPanelFacingTheRearHeader()
    {
        LadderEdgePlacement anchor = CellarLadder.Anchor;
        Assert.Equal(CellarGeometry.PlannedLadderOrigin.X, anchor.Origin.X);
        Assert.Equal(CellarGeometry.PlannedLadderOrigin.Y, anchor.Origin.Y);
        Assert.Equal(CellarGeometry.PlannedLadderOrigin.Z, anchor.Origin.Z);
        // Climbing side toward the rear header (+z); the ladder's top is the snap plane, as on a trapdoor.
        Assert.Equal(0f, anchor.FacingX);
        Assert.Equal(1f, anchor.FacingZ);
        Assert.Equal(CellarGeometry.SnapPlaneY, anchor.Origin.Y + LadderEdgeSnap.SectionHeight, 5);
        Assert.True(anchor.OnFloor);
        Assert.Equal(0f, anchor.TurnDegrees);
        // The joint the reach is measured to is the middle of the front panel's inner face, at the snap plane.
        Assert.Equal(new ClimbPoint(0f, CellarGeometry.SnapPlaneY, CellarGeometry.ApertureFrontZ), anchor.Joint);
        // The rails' back face rests the edge snap's own gap inside the panel, never in it.
        Assert.Equal(CellarGeometry.ApertureFrontZ + LadderEdgeSnap.Gap, anchor.Origin.Z + LadderFootprint.Section.Back, 5);
        // A front-lane climber fits the opening once the pair is open.
        Assert.True(CellarGeometry.ClimberMargins(Radius, out float front, out float rear, out float side));
        Assert.True(front > 0f && rear > 0f && side > 0f, "A climber in the anchored ladder's front lane must fit the opening.");
    }

    [Fact]
    public void TheAnchorIsWhereTheEdgeSnapWouldHangALadderFromTheOpeningsFrontEdge()
    {
        float face = CellarGeometry.ApertureFrontZ;
        var corners = new List<ClimbPoint>
        {
            new ClimbPoint(-0.85f, 0f, face - 1.65f), new ClimbPoint(0.85f, 0f, face - 1.65f),
            new ClimbPoint(-0.85f, 0f, face), new ClimbPoint(0.85f, 0f, face),
        };
        var request = new LadderEdgeRequest
        {
            Corners = corners,
            Aim = new ClimbPoint(0f, 0f, face - 0.05f),
            Viewer = new ClimbPoint(0f, 1.6f, 1f),
            Mode = LadderSnapMode.Auto,
            Footprint = LadderFootprint.Section,
        };
        Assert.True(LadderEdgeSnap.TryPlace(request, out LadderEdgePlacement edge));
        LadderEdgePlacement anchor = CellarLadder.Anchor;
        Assert.Equal(edge.Origin.X, anchor.Origin.X, 5);
        Assert.Equal(edge.Origin.Y, anchor.Origin.Y, 5);
        Assert.Equal(edge.Origin.Z, anchor.Origin.Z, 5);
        Assert.Equal(edge.FacingX, anchor.FacingX, 5);
        Assert.Equal(edge.FacingZ, anchor.FacingZ, 5);
        Assert.Equal(edge.Joint.Z, anchor.Joint.Z, 5);
    }

    [Fact]
    public void TheRoomTestLooksUnderTheFrameInsideTheLaddersFootprint()
    {
        ClimbPoint probe = CellarLadder.RoomProbe;
        Assert.True(probe.Y < CellarGeometry.FrameBottomY, "The room test must start below the cover's own colliders.");
        Assert.True(probe.Z > CellarGeometry.PlannedLadderOrigin.Z + LadderFootprint.Section.Back);
        Assert.True(probe.Z < CellarGeometry.PlannedLadderOrigin.Z + LadderFootprint.Section.Front);
        Assert.True(probe.Z > CellarGeometry.ApertureFrontZ, "Inside the opening, not under the front panel.");
        Assert.Equal(0f, probe.X);
        Assert.InRange(probe.Y - CellarLadder.RoomProbeDepth, CellarGeometry.PlannedLadderOrigin.Y, CellarGeometry.PlannedLadderOrigin.Y + 0.25f);
    }

    // ---- Passage ----

    [Fact]
    public void ATopExitAndADescendingEntryCrossTheOpening()
    {
        // The rise out of the front lane, feet a metre under the snap plane: through the cover.
        Assert.True(CellarLadder.Crosses(0f, FrontLane, Radius, -1f, CellarLadder.OverPanelY + Height + Margin));
        // The cross leg over the panel, at the panel lift, from the lane: still inside the cover's depth there.
        HatchLadder.LegSpan(CellarLadder.OverPanelY, CellarLadder.OverPanelY, Height + Margin, out float low, out float high);
        Assert.True(CellarLadder.Crosses(0f, FrontLane, Radius, low, high));
        // The descending entry's lowering into the lane.
        Assert.True(CellarLadder.Crosses(0f, FrontLane, Radius, -1.3f, FloorFeet + Height + Margin));
    }

    [Fact]
    public void TheRearLaneAndTheFloorAroundAreNotTheOpening()
    {
        // The rear lane lies under the front panel and the floor in front of it, as a trapdoor's lies under its band.
        Assert.False(CellarLadder.Crosses(0f, RearLane, Radius, -1f, 0.3f + Height));
        // Standing on the floor where the top exit lands, and beside the module.
        foreach (float distance in TopExitDistances)
        {
            Assert.False(CellarLadder.Crosses(0f, TopExitZ(distance), Radius, FloorFeet, FloorFeet + Height + Margin));
        }

        Assert.False(CellarLadder.Crosses(1.5f, 0f, Radius, FloorFeet, FloorFeet + Height));
        Assert.False(CellarLadder.Crosses(0f, 1.5f, Radius, FloorFeet, FloorFeet + Height));
    }

    [Theory]
    [InlineData(-0.8f)]
    [InlineData(-0.3f)]
    [InlineData(0.2f)]
    [InlineData(0.75f)]
    public void StandingOnTheClosedLeavesIsNotACrossingButAnyRealReachIntoTheCoverIs(float z)
    {
        // The sloped top: a landing floats the skin over it at the capsule's centre.
        float onLeaves = CellarGeometry.TopAt(z) + LandingSkin;
        Assert.False(CellarLadder.Crosses(0f, z, Radius, onLeaves, onLeaves + Height));
        // Within the contact slack it is still contact.
        Assert.False(CellarLadder.Crosses(0f, z, Radius, CellarGeometry.TopAt(z) - 0.01f, 3f));
        // Below the slope's top there it is inside the cover.
        Assert.True(CellarLadder.Crosses(0f, z, Radius, CellarGeometry.TopAt(z) - HatchLadder.ContactSlack - 0.01f, 3f));
        // A head that just meets the frame's underside, and one that reaches into it.
        Assert.False(CellarLadder.Crosses(0f, z, Radius, CellarGeometry.FrameBottomY - Height, CellarGeometry.FrameBottomY));
        Assert.True(CellarLadder.Crosses(0f, z, Radius, -2f, CellarGeometry.FrameBottomY + HatchLadder.ContactSlack + 0.01f));
    }

    [Fact]
    public void TheCoversDepthFollowsTheSlopeAndIsClampedToTheOpening()
    {
        // Higher toward the rear: a height inside the cover at the rear is above it at the front.
        float y = CellarGeometry.TopAt(0f);
        Assert.True(CellarLadder.Crosses(0f, 0.5f, Radius, y, y + Height));
        Assert.False(CellarLadder.Crosses(0f, -0.6f, Radius, y, y + Height));
        // In front of the opening (within the radius) the depth is the panel's inner top; behind it, the header's.
        float front = CellarGeometry.TopAt(CellarGeometry.ApertureFrontZ);
        Assert.True(CellarLadder.Crosses(0f, CellarGeometry.ApertureFrontZ - 0.2f, Radius, front - 0.05f, front + Height));
        Assert.False(CellarLadder.Crosses(0f, CellarGeometry.ApertureFrontZ - 0.2f, Radius, front, front + Height));
        float rear = CellarGeometry.TopAt(CellarGeometry.ApertureRearZ);
        Assert.True(CellarLadder.Crosses(0f, CellarGeometry.ApertureRearZ + 0.2f, Radius, rear - 0.05f, rear + Height));
        Assert.False(CellarLadder.Crosses(0f, CellarGeometry.ApertureRearZ + 0.2f, Radius, rear, rear + Height));
    }

    [Fact]
    public void TheOpeningIsWidenedByTheRadiusOnEverySideAndAtTheCorners()
    {
        float side = CellarGeometry.HalfAperture + Radius;
        float front = CellarGeometry.ApertureFrontZ - Radius;
        float rear = CellarGeometry.ApertureRearZ + Radius;
        Assert.True(CellarLadder.Crosses(side - 0.01f, 0f, Radius, -1f, 0.2f));
        Assert.True(CellarLadder.Crosses(-(side - 0.01f), 0f, Radius, -1f, 0.2f));
        Assert.True(CellarLadder.Crosses(0f, front + 0.01f, Radius, -1f, 0.2f));
        Assert.True(CellarLadder.Crosses(0f, rear - 0.01f, Radius, -1f, 0.2f));
        Assert.True(CellarLadder.Crosses(side - 0.01f, front + 0.01f, Radius, -1f, 0.2f));
        Assert.False(CellarLadder.Crosses(side + 0.01f, 0f, Radius, -1f, 0.2f));
        Assert.False(CellarLadder.Crosses(0f, front - 0.01f, Radius, -1f, 0.2f));
        Assert.False(CellarLadder.Crosses(0f, rear + 0.01f, Radius, -1f, 0.2f));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0.4f, -1f, 1f)]
    [InlineData(0f, float.PositiveInfinity, 0.4f, -1f, 1f)]
    [InlineData(5f, 5f, float.NaN, -1f, 1f)]
    [InlineData(5f, 5f, -0.1f, -1f, 1f)]
    [InlineData(5f, 5f, 0.4f, float.NaN, 1f)]
    [InlineData(5f, 5f, 0.4f, -1f, float.NegativeInfinity)]
    public void ARouteThatCannotBeMeasuredIsHeldToThePassageRule(float x, float z, float radius, float low, float high) =>
        Assert.True(CellarLadder.Crosses(x, z, radius, low, high));

    [Fact]
    public void TheHeightOrderDoesNotMatter() =>
        Assert.Equal(CellarLadder.Crosses(0f, FrontLane, Radius, -1f, 2f), CellarLadder.Crosses(0f, FrontLane, Radius, 2f, -1f));

    [Fact]
    public void ARouteThroughTheOpeningNeedsBothLeavesFullyOpenAndStayingOpen()
    {
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(0f, false)), "Closed.");
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(0.5f, true)), "Opening, or held part open.");
        Assert.True(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(1f, true)), "Open.");
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(1f, false)), "Open but asked to close.");
        Assert.True(HatchLadder.AllowsRoute(false, false), "A route that does not cross the opening is never held.");
    }

    // ---- Landing ----

    [Fact]
    public void AClosedLeafAndAnUprightLeafsTopEdgeBothPassTheSlopeTestSoTheLeafRuleIsWhatRefusesThem()
    {
        // The landing search accepts a surface whose normal has y >= 0.65.
        foreach (CellarLeaf leaf in new[] { CellarLeaf.Left, CellarLeaf.Right })
        {
            // Closed: the leaf's top face lies in the slope. Open: its free (seam) edge face, upright, faces up the
            // slope's normal. Both normals from the model's own corners.
            Assert.True(FaceNormalY(leaf, 0f, 0, 1, 4) >= 0.65, "A closed leaf's top face passes the slope test.");
            Assert.True(FaceNormalY(leaf, CellarGeometry.OpenAngleDegrees, 4, 5, 6) >= 0.65, "An upright leaf's top edge passes it too.");
        }

        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0f, false)), "Closed and staying closed.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0f, true)), "Asked to open.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0.3f, false)), "Closing.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(1f, true)), "Open: not even its top edge.");
        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Frame, false), "The frame is an ordinary piece.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Unknown, true), "A cover whose pivots are missing is never a landing.");
    }

    // ---- Over the front panel ----

    [Fact]
    public void ThePanelLiftCarriesTheFeetOverThePanelsHighestPoint()
    {
        float panelTop = CellarGeometry.Frame.Single(prism => prism.Name == "front_panel").Profile.Max(point => point.Y);
        Assert.Equal(CellarGeometry.TopAt(CellarGeometry.ApertureFrontZ), panelTop, 5);
        Assert.Equal(panelTop + CellarLadder.PanelClearance, CellarLadder.OverPanelY, 5);
        Assert.True(CellarLadder.PanelClearance > 0f);
        // 0.40 to 0.47 m above the snap plane, where the anchored ladder's top is.
        Assert.InRange(panelTop - CellarGeometry.SnapPlaneY, 0.47f, 0.48f);
    }

    [Fact]
    public void TheStandardStepLiftWouldPassTheQueryButLetTheBodyMeetThePanel()
    {
        // Why the standard lifts are not offered over the frame. From the floor in front, a 0.3 m step leaves the feet
        // below the panel's top; the query capsule floats over the feet and is swept thinner, so it passes over the
        // panel, while the real body's bottom, at the feet, would meet the panel's inner edge.
        float panelTop = CellarGeometry.TopAt(CellarGeometry.ApertureFrontZ);
        float stepped = FloorFeet + LadderRules.StepLift;
        Assert.True(stepped < panelTop, "The real body's bottom would be below the panel's top.");
        Assert.True(stepped + GroundClearance + QuerySkin > panelTop, "Yet the query capsule would clear it.");
        Assert.True(panelTop - stepped > 0.05f, "By more than a graze.");
        // The panel lift from the same floor puts the real body's bottom over the panel.
        Assert.True(CellarLadder.TryPanelLift(FloorFeet, CellarLadder.OverPanelY, out float lift));
        Assert.Equal(CellarLadder.OverPanelY, FloorFeet + lift, 5);
        Assert.True(FloorFeet + lift > panelTop);
        Assert.True(lift > LadderRules.StepLift);
    }

    [Fact]
    public void NoPanelLiftWhenNoCoverIsCrossedOrNoneIsNeeded()
    {
        Assert.False(CellarLadder.TryPanelLift(0f, float.NegativeInfinity, out float none));
        Assert.Equal(0f, none);
        Assert.False(CellarLadder.TryPanelLift(float.NaN, 0.5f, out _));
        Assert.False(CellarLadder.TryPanelLift(0.6f, 0.5f, out _), "Already over the panel's height.");
        Assert.False(CellarLadder.TryPanelLift(0.5f, 0.5f, out _));
        Assert.True(CellarLadder.TryPanelLift(0.4f, 0.5f, out float some));
        Assert.Equal(0.1f, some, 5);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.405f)]
    [InlineData(0.6f)]
    public void OverTheFrameEveryLiftTriedReachesThePanelLift(float panel)
    {
        float[] lifts = CellarLadder.Lifts(panel);
        Assert.NotEmpty(lifts);
        Assert.All(lifts, lift => Assert.True(lift >= panel, "No lift may leave the feet below the panel's height."));
        Assert.Equal(lifts.Distinct().Count(), lifts.Length);
        Assert.Equal(lifts.OrderBy(lift => lift).ToArray(), lifts);
        Assert.True(lifts.Length <= 2);
        // Below the standard step the standard step is still tried after it; at or above it, only the panel lift.
        if (panel < LadderRules.StepLift) { Assert.Equal(LadderRules.StepLift, lifts[lifts.Length - 1]); }
        else { Assert.Equal(new[] { panel }, lifts); }
    }

    [Fact]
    public void AnUnmeasurablePanelLiftOffersNoLiftAtAll()
    {
        Assert.Empty(CellarLadder.Lifts(float.NaN));
        Assert.Empty(CellarLadder.Lifts(float.PositiveInfinity));
    }

    [Fact]
    public void OnlyARouteBetweenTheOpeningAndTheFloorsLevelOutsideItPassesOverTheFrame()
    {
        float lane = FrontLane;
        // The top exit: from the lane near the ladder's top to the floor in front.
        foreach (float distance in TopExitDistances)
        {
            Assert.True(CellarLadder.LeavesOverFrame(0f, -0.5f, lane, 0f, FloorFeet, TopExitZ(distance)));
            Assert.True(CellarLadder.LeavesOverFrame(0f, 0f, lane, 0.4f, FloorFeet, TopExitZ(distance)));
        }

        // A landing beside the module, over a cheek, is over the frame too.
        Assert.True(CellarLadder.LeavesOverFrame(0f, -0.5f, lane, 1.5f, FloorFeet, lane));
        // Not: to a landing inside the opening, down in the dig under the frame, from outside the opening, or from far
        // down a longer shaft.
        Assert.False(CellarLadder.LeavesOverFrame(0f, -0.5f, lane, 0.3f, FloorFeet, 0.2f));
        Assert.False(CellarLadder.LeavesOverFrame(0f, -1.5f, lane, 0f, -1.95f, -1.6f));
        Assert.False(CellarLadder.LeavesOverFrame(0f, FloorFeet, -1.6f, 0f, FloorFeet, -2f));
        Assert.False(CellarLadder.LeavesOverFrame(0f, -2.5f, lane, 0f, FloorFeet, -1.9f));
        Assert.False(CellarLadder.LeavesOverFrame(0f, CellarLadder.OverPanelY + 0.01f, lane, 0f, FloorFeet, -1.9f));
        Assert.False(CellarLadder.LeavesOverFrame(float.NaN, -0.5f, lane, 0f, FloorFeet, -1.9f));
        Assert.False(CellarLadder.LeavesOverFrame(0f, -0.5f, lane, 0f, float.NaN, -1.9f));
    }

    [Fact]
    public void TheTopExitsLandingIsClearOfTheCoverSoItsSettleIsNotACrossing()
    {
        // LadderRuntime.TryExitTop's first candidates lie straight in front of the run's top at radius + 0.45 and
        // radius + 0.85 m. At the game's radius both stand clear of the cover's widened opening and of its front panel.
        foreach (float distance in TopExitDistances)
        {
            float z = TopExitZ(distance);
            Assert.True(z < CellarGeometry.ApertureFrontZ - Radius, "Outside the widened opening.");
            Assert.True(z + Radius < -CellarGeometry.HalfModule, "The capsule clears the front panel's outer face.");
            HatchLadder.LegSpan(CellarLadder.OverPanelY, FloorFeet, Height + Margin, out float low, out float high);
            Assert.False(CellarLadder.Crosses(0f, z, Radius, low, high));
        }
    }

    [Fact]
    public void ADescendingEntrysHandsReachOverThePanelNotThroughIt()
    {
        // LadderRuntime's mount: from the chest to the ladder's mid-depth. At the run's top + 0.2 m (the floor lip rule)
        // the line from someone standing at the front panel passes through the panel; at the panel lift it clears it.
        ClimbPoint rung(float y) => new ClimbPoint(0f, y, CellarGeometry.PlannedLadderOrigin.Z +
            (LadderFootprint.Section.Back + LadderFootprint.Section.Front) * 0.5f);
        CellarPrism panel = CellarGeometry.Frame.Single(prism => prism.Name == "front_panel");
        float chestY = FloorFeet + Height * ClimbPolicy.ChestFraction;
        bool Blocked(float standZ, float sightY)
        {
            var chest = new ClimbPoint(0f, chestY, standZ);
            ClimbPoint to = rung(sightY);
            for (int step = 0; step <= 400; step++)
            {
                float t = step / 400f;
                var point = new ClimbPoint(0f, chest.Y + (to.Y - chest.Y) * t, chest.Z + (to.Z - chest.Z) * t);
                if (panel.ContainsStrictly(point, 0f)) { return true; }
            }

            return false;
        }

        float nearest = -CellarGeometry.HalfModule - Radius;
        Assert.True(Blocked(nearest, CellarGeometry.SnapPlaneY + 0.2f), "The floor's lip rule would aim through the panel.");
        foreach (float standZ in new[] { nearest, nearest - 0.2f, nearest - 0.5f })
        {
            Assert.False(Blocked(standZ, CellarLadder.OverPanelY), "Over the panel at " + standZ);
        }
    }

    // ---- The Unity adapters, by source ----

    [Fact]
    public void TheLeavesRuntimeIsTheCellarDoorsProductionCallerOfIsPassageAndIsLanding()
    {
        string leaves = Code(Source("Runtime/CellarLeaves.cs"));
        Assert.Single(Regex.Matches(leaves, @"HatchMotion\s*\.\s*IsPassage\s*\(\s*_progress\s*,\s*open\s*\)"));
        Assert.Single(Regex.Matches(leaves, @"HatchMotion\s*\.\s*IsLanding\s*\(\s*_progress\s*,\s*open\s*\)"));
        Assert.Matches(new Regex(@"internal\s+bool\s+Passage\s*=>\s*TryReadOpen\s*\(\s*out\s+bool\s+open\s*\)\s*&&\s*HatchMotion\s*\.\s*IsPassage"), leaves);
        Assert.Matches(new Regex(@"internal\s+bool\s+Landing\s*=>\s*TryReadOpen\s*\(\s*out\s+bool\s+open\s*\)\s*&&\s*HatchMotion\s*\.\s*IsLanding"), leaves);
        // Both read the native state through the one guarded helper, which refuses a cover that has not settled, has
        // stopped on a fault, lacks its parts or has no ZDO.
        string read = Between(leaves, @"private\s+bool\s+TryReadOpen\s*\(", @"return\s+true\s*;");
        Assert.Matches(new Regex(@"!\s*enabled\s*\|\|\s*!\s*_settled\s*\|\|\s*view\s*==\s*null\s*\|\|\s*!\s*view\s*\.\s*IsValid\s*\(\s*\)\s*\|\|\s*_left\s*==\s*null"), read);

        string shut = Between(leaves, @"internal\s+static\s+bool\s+RouteShut\s*\(", @"return\s+false\s*;");
        Assert.Matches(new Regex(@"bool\s+crosses\s*=\s*CellarLadder\s*\.\s*Crosses\s*\(\s*local\s*\.\s*x\s*,\s*local\s*\.\s*z\s*,\s*radius\s*,\s*low\s*-\s*origin\s*,\s*high\s*-\s*origin\s*\)\s*;"), shut);
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*HatchLadder\s*\.\s*AllowsRoute\s*\(\s*crosses\s*,\s*crosses\s*&&\s*cover\s*\.\s*Passage\s*\)\s*\)\s*\{\s*return\s+true\s*;"), shut);
        // A ghost or icon render (no ZDO) never shuts a route or lifts one.
        Assert.Equal(2, Regex.Matches(leaves, @"if\s*\(\s*cover\s*==\s*null\s*\|\|\s*cover\s*\.\s*_view\s*==\s*null\s*\|\|\s*!\s*cover\s*\.\s*_view\s*\.\s*IsValid\s*\(\s*\)\s*\)\s*\{\s*continue\s*;").Count);

        string land = Between(leaves, @"internal\s+static\s+bool\s+MayLandOn\s*\(", @"private\s+void\s+Stop\s*\(");
        Assert.Matches(new Regex(@"collider\s*\.\s*GetComponentInParent\s*<\s*CellarLeaves\s*>\s*\(\s*\)"), land);
        Assert.Matches(new Regex(@"if\s*\(\s*cover\s*==\s*null\s*\)\s*\{\s*return\s+true\s*;\s*\}"), land);
        Assert.Matches(new Regex(@"cover\s*\.\s*_leftPart\s*==\s*null\s*\|\|\s*cover\s*\.\s*_rightPart\s*==\s*null\s*\?\s*HatchPart\s*\.\s*Unknown"), land);
        Assert.Matches(new Regex(@"IsChildOf\s*\(\s*cover\s*\.\s*_leftPart\s*\)\s*\|\|\s*collider\s*\.\s*transform\s*\.\s*IsChildOf\s*\(\s*cover\s*\.\s*_rightPart\s*\)\s*\?\s*HatchPart\s*\.\s*Lid\s*:\s*HatchPart\s*\.\s*Frame"), land);
        Assert.Matches(new Regex(@"return\s+HatchLadder\s*\.\s*IsLandingSurface\s*\(\s*part\s*,\s*part\s*==\s*HatchPart\s*\.\s*Lid\s*&&\s*cover\s*\.\s*Landing\s*\)\s*;"), land);
        // The pivots are recorded before Awake can give up on a cover, so a cover missing other parts is still told apart.
        Assert.True(Index(leaves, @"_rightPart\s*=\s*rightHinge\s*;") < Index(leaves, @"enabled\s*=\s*false\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\b_leftPart\s*=(?!=)"));
        Assert.Single(Regex.Matches(leaves, @"\b_rightPart\s*=(?!=)"));

        string over = Between(leaves, @"internal\s+static\s+float\s+OverPanel\s*\(", @"return\s+best\s*;");
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*CellarLadder\s*\.\s*LeavesOverFrame\s*\(\s*from\s*\.\s*x\s*,\s*inside\s*\.\s*y\s*-\s*origin\s*,\s*from\s*\.\s*z\s*,\s*to\s*\.\s*x\s*,\s*outside\s*\.\s*y\s*-\s*origin\s*,\s*to\s*\.\s*z\s*\)\s*\)\s*\{\s*continue\s*;"), over);
        Assert.Matches(new Regex(@"best\s*=\s*Mathf\s*\.\s*Max\s*\(\s*best\s*,\s*origin\s*\+\s*CellarLadder\s*\.\s*OverPanelY\s*\)\s*;"), over);
        Assert.Matches(new Regex(@"float\s+best\s*=\s*float\s*\.\s*NegativeInfinity\s*;"), over);
    }

    [Fact]
    public void EveryWokenCoverIsRegisteredUntilItIsDestroyedAndAnyMeansSomeAre()
    {
        string leaves = Code(Source("Runtime/CellarLeaves.cs"));
        string awake = Between(leaves, @"private\s+void\s+Awake\s*\(\s*\)\s*\{", @"_view\s*=\s*GetComponent");
        Assert.Matches(new Regex(@"^\s*private\s+void\s+Awake\s*\(\s*\)\s*\{\s*Live\s*\.\s*Add\s*\(\s*this\s*\)\s*;\s*$"), awake);
        Assert.Single(Regex.Matches(leaves, @"\bLive\s*\.\s*Add\s*\("));
        Assert.Single(Regex.Matches(leaves, @"private\s+void\s+OnDestroy\s*\(\s*\)\s*=>\s*Live\s*\.\s*Remove\s*\(\s*this\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\bLive\s*\.\s*Remove\s*\("));
        Assert.Single(Regex.Matches(leaves, @"internal\s+static\s+bool\s+Any\s*=>\s*Live\s*\.\s*Count\s*>\s*0\s*;"));
        Assert.DoesNotMatch(new Regex(@"\bLive\s*\.\s*(Clear|Insert|RemoveAt|RemoveAll|AddRange)\s*\(|(?<!List\s*<\s*CellarLeaves\s*>\s*)\bLive\s*=(?!=)"), leaves);
        Assert.Matches(new Regex(@"private\s+static\s+readonly\s+List\s*<\s*CellarLeaves\s*>\s+Live\s*="), leaves);
        // Its own registry, never the trapdoor lid's.
        Assert.DoesNotMatch(new Regex(@"TrapdoorLid\s*\.\s*Live"), leaves);
    }

    [Fact]
    public void TheLadderQuestionsDoNotAllocate()
    {
        string leaves = Code(Source("Runtime/CellarLeaves.cs"));
        foreach (string member in new[] { "RouteShut", "OverPanel", "MayLandOn" })
        {
            Match header = Regex.Match(leaves, @"internal\s+static\s+\w+\s+" + member + @"\s*\([^)]*\)\s*\{");
            Assert.True(header.Success, member);
            string body = leaves.Substring(header.Index, BodyEnd(leaves, header) - header.Index);
            foreach (string allocating in new[] { @"\bnew\s+(?!Vector3\b|Quaternion\b)", @"\.\s*Select\s*\(", @"\.\s*Where\s*\(", @"\.\s*ToArray\s*\(", @"""", @"\bforeach\b", @"\bLog\b" })
            {
                Assert.DoesNotMatch(new Regex(allocating), body);
            }
        }
    }

    [Fact]
    public void TheLandingSearchRefusesANonLandingLeafBeforeItsSlopeTest()
    {
        string clearance = Code(Source("Runtime/LadderClearance.cs"));
        string surface = Between(clearance, @"private\s+bool\s+TrySurface\s*\(", @"return\s+available\s*;");
        int gate = Index(surface, @"if\s*\(\s*CellarLeaves\s*\.\s*Any\s*&&\s*Solid\s*\(\s*hit\s*\.\s*collider\s*\)\s*&&\s*!\s*CellarLeaves\s*\.\s*MayLandOn\s*\(\s*hit\s*\.\s*collider\s*\)\s*\)\s*\{\s*floor\s*=\s*default\s*;\s*return\s+false\s*;");
        int slope = Index(surface, @"hit\s*\.\s*normal\s*\.\s*y\s*<\s*0\.65f");
        Assert.True(gate < slope, "A leaf must be refused before the slope test can accept its tilted face or upright edge.");
        Assert.Single(Regex.Matches(clearance, @"CellarLeaves\s*\.\s*MayLandOn\s*\("));
    }

    [Fact]
    public void EveryExitRouteIsCheckedAgainstTheCellarDoorsBeforeItIsCommitted()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string plan = Between(runtime, @"private\s+bool\s+PlanExitTo\s*\(", @"private\s+bool\s+CrossesShutHatch\s*\(");
        int gate = Index(plan, @"if\s*\(\s*CellarLeaves\s*\.\s*Any\s*&&\s*CrossesShutCellar\s*\(\s*feet\s*,\s*path\s*\)\s*\)\s*\{\s*_cellarShut\s*=\s*true\s*;");
        int sweep = Index(plan, @"_clearance\s*\.\s*CanFollow\s*\(\s*feet\s*,\s*path\s*\)");
        int commit = Index(plan, @"_mode\s*=\s*Mode\s*\.\s*Exiting\s*;");
        Assert.True(gate < sweep && sweep < commit, "The cellar test must refuse a route before it is swept and committed.");

        string helper = Between(runtime, @"private\s+bool\s+CrossesShutCellar\s*\(", @"private\s+const\s+float\s+RouteTopMargin");
        Assert.Matches(new Regex(@"float\s+height\s*=\s*_clearance\s*\.\s*Height\s*\+\s*RouteTopMargin\s*;"), helper);
        Assert.Matches(new Regex(@"Vector3\s+from\s*=\s*start\s*;\s*foreach\s*\(\s*Vector3\s+to\s+in\s+path\s*\)"), helper);
        Assert.Matches(new Regex(@"HatchLadder\s*\.\s*LegSpan\s*\(\s*from\s*\.\s*y\s*,\s*to\s*\.\s*y\s*,\s*height\s*,\s*out\s+float\s+low\s*,\s*out\s+float\s+top\s*\)"), helper);
        Assert.Matches(new Regex(@"if\s*\(\s*CellarLeaves\s*\.\s*RouteShut\s*\(\s*from\s*,\s*radius\s*,\s*low\s*,\s*top\s*\)\s*\|\|\s*CellarLeaves\s*\.\s*RouteShut\s*\(\s*to\s*,\s*radius\s*,\s*low\s*,\s*top\s*\)\s*\)\s*\{\s*return\s+true\s*;\s*\}\s*from\s*=\s*to\s*;"), helper);
    }

    [Fact]
    public void AnExitOverTheFrameTriesOnlyLiftsThatClearThePanelAndOtherwiseExactlyTheStandardTwo()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string plan = Between(runtime, @"private\s+bool\s+PlanExitTo\s*\(", @"private\s+bool\s+CrossesShutHatch\s*\(");
        Assert.Matches(new Regex(@"float\s+overPanel\s*=\s*CellarLeaves\s*\.\s*Any\s*\?\s*CellarLeaves\s*\.\s*OverPanel\s*\(\s*feet\s*,\s*floor\s*\)\s*:\s*float\s*\.\s*NegativeInfinity\s*;"), plan);
        Assert.Matches(new Regex(@"float\s*\[\s*\]\s+lifts\s*=\s*Domain\s*\.\s*Doors\s*\.\s*CellarLadder\s*\.\s*TryPanelLift\s*\(\s*Mathf\s*\.\s*Max\s*\(\s*feet\s*\.\s*y\s*,\s*floor\s*\.\s*y\s*\)\s*,\s*overPanel\s*,\s*out\s+float\s+panel\s*\)\s*\?\s*Domain\s*\.\s*Doors\s*\.\s*CellarLadder\s*\.\s*Lifts\s*\(\s*panel\s*\)\s*:\s*new\s*\[\s*\]\s*\{\s*LadderRules\s*\.\s*RouteLift\s*,\s*LadderRules\s*\.\s*StepLift\s*\}\s*;"), plan);
        Assert.Matches(new Regex(@"foreach\s*\(\s*float\s+back\s+in\s+new\s*\[\s*\]\s*\{\s*0f\s*,\s*0\.25f\s*,\s*0\.5f\s*\}\s*\)\s*foreach\s*\(\s*float\s+lift\s+in\s+lifts\s*\)"), plan);
        Assert.Matches(new Regex(@"float\s+raised\s*=\s*Mathf\s*\.\s*Max\s*\(\s*feet\s*\.\s*y\s*,\s*floor\s*\.\s*y\s*\)\s*\+\s*lift\s*;"), plan);
        // Without a cellar door, overPanel is negative infinity, and that is no panel lift: the standard two stand.
        Assert.False(CellarLadder.TryPanelLift(0f, float.NegativeInfinity, out _));
    }

    [Fact]
    public void TheTopExitAndTheDescendingEntryNeedAnOpenPair()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string top = Between(runtime, @"private\s+bool\s+TryExitTop\s*\(", @"Vector3\s+inward\s*=\s*Toward\s*\(\s*\)\s*;");
        Assert.Matches(new Regex(@"if\s*\(\s*CellarLeaves\s*\.\s*Any\s*&&\s*_body\s*!=\s*null\s*&&\s*CellarLeaves\s*\.\s*RouteShut\s*\(\s*LanePoint\s*\(\s*_run\s*,\s*_laneDepth\s*,\s*_body\s*\.\s*position\s*\.\s*y\s*\)"), top);
        Assert.Matches(new Regex(@"_cellarShut\s*=\s*true\s*;[^}]*return\s+false\s*;"), top);
        Assert.Matches(new Regex(@"^\s*private\s+bool\s+TryExitTop\s*\(\s*\)\s*\{\s*_hatchShut\s*=\s*false\s*;\s*_cellarShut\s*=\s*false\s*;"), top);

        string mount = Between(runtime, @"private\s+bool\s+TryPlanMount\s*\(", @"private\s+static\s+bool\s+StandsOnLadder\s*\(");
        int gate = Index(mount, @"if\s*\(\s*topEntry\s*&&\s*CellarLeaves\s*\.\s*Any\s*&&\s*CellarLeaves\s*\.\s*RouteShut\s*\(\s*line\s*,");
        int refusal = Index(mount, @"first\s*=\s*ClimbRefusal\s*\.\s*CellarClosed\s*;\s*continue\s*;");
        int route = Index(mount, @"Vector3\s*\[\s*\]\s*\?\s*route\s*=\s*null\s*;");
        Assert.True(gate < refusal && refusal < route, "A descending entry is refused before any route is chosen.");
        Assert.True(Index(mount, @"first\s*=\s*ClimbRefusal\s*\.\s*HatchClosed\s*;") < gate, "After the trapdoor's own gate, which is unchanged.");
        Assert.Matches(new Regex(@"Notice\s*\(\s*CellarClosedNotice\s*\)"), runtime);
        Assert.Matches(new Regex(@"case\s+ClimbRefusal\s*\.\s*CellarClosed\s*:\s*Notice\s*\(\s*CellarClosedNotice\s*\)\s*;\s*break\s*;"), runtime);
        Assert.Matches(new Regex(@"private\s+const\s+string\s+CellarClosedNotice\s*=\s*""\$""\s*\+\s*Domain\s*\.\s*Doors\s*\.\s*CellarDoors\s*\.\s*CellarClosedToken\s*;"), runtime);
    }

    [Fact]
    public void ADescendingEntryOverTheFrameAimsAndLiftsOverThePanel()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string mount = Between(runtime, @"private\s+bool\s+TryPlanMount\s*\(", @"private\s+static\s+bool\s+StandsOnLadder\s*\(");
        Assert.Matches(new Regex(@"float\s+overPanel\s*=\s*topEntry\s*&&\s*CellarLeaves\s*\.\s*Any\s*\?\s*CellarLeaves\s*\.\s*OverPanel\s*\(\s*line\s*,\s*feet\s*\)\s*:\s*float\s*\.\s*NegativeInfinity\s*;"), mount);
        // The hands' aim: the lip rule, raised to the panel's height only over a cellar door's frame.
        Assert.Matches(new Regex(@"float\s+sightY\s*=\s*topEntry\s*\?\s*Mathf\s*\.\s*Max\s*\(\s*run\s*\.\s*Top\s*\.\s*Y\s*\+\s*TopEntrySightAbove\s*,\s*overPanel\s*\)\s*:"), mount);
        Assert.Matches(new Regex(@"float\s*\[\s*\]\s+lifts\s*=\s*Domain\s*\.\s*Doors\s*\.\s*CellarLadder\s*\.\s*TryPanelLift\s*\(\s*Mathf\s*\.\s*Max\s*\(\s*feet\s*\.\s*y\s*,\s*line\s*\.\s*y\s*\)\s*,\s*overPanel\s*,\s*out\s+float\s+panel\s*\)\s*\?\s*Domain\s*\.\s*Doors\s*\.\s*CellarLadder\s*\.\s*Lifts\s*\(\s*panel\s*\)\s*:\s*new\s*\[\s*\]\s*\{\s*LadderRules\s*\.\s*RouteLift\s*,\s*LadderRules\s*\.\s*StepLift\s*\}\s*;"), mount);
        Assert.Matches(new Regex(@"foreach\s*\(\s*float\s+lift\s+in\s+lifts\s*\)\s*\{\s*float\s+raised\s*=\s*Mathf\s*\.\s*Max\s*\(\s*feet\s*\.\s*y\s*,\s*line\s*\.\s*y\s*\)\s*\+\s*lift\s*;"), mount);
        // The aim is computed before the line-of-sight test that uses it.
        Assert.True(Index(mount, @"float\s+overPanel\s*=") < Index(mount, @"clearance\s*\.\s*HasLineOfSight\s*\(\s*chest\s*,\s*rung\s*\)"));
        // Mathf.Max with negative infinity is the first argument: without a cellar door the aim is the lip rule.
        Assert.Equal(0.2f, Math.Max(0.2f, float.NegativeInfinity));
    }

    [Fact]
    public void AClosedCellarDoorIsNamedWhereATrapdoorIs()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        Assert.Equal(2, Regex.Matches(runtime, @"Notice\s*\(\s*_hatchShut\s*\?\s*HatchClosedNotice\s*:\s*_cellarShut\s*\?\s*CellarClosedNotice\s*:\s*""\$cl_msg_nolanding""\s*\)").Count);
        Assert.Equal(2, Regex.Matches(runtime, @"Notice\s*\(\s*_hatchShut\s*\?\s*HatchClosedNotice\s*:\s*_cellarShut\s*\?\s*CellarClosedNotice\s*:\s*""\$cl_msg_notop""\s*\)").Count);
        Assert.Matches(new Regex(@"else\s+if\s*\(\s*velocity\s*>\s*0f\s*&&\s*_cellarShut\s*&&\s*body\s*\.\s*position\s*\.\s*y\s*>=\s*_run\s*\.\s*Top\s*\.\s*Y\s*-\s*TopBlockedBand\s*\)\s*\{\s*Notice\s*\(\s*CellarClosedNotice\s*\)\s*;\s*\}"), runtime);
        // Each attempt starts clear: a mount, a top exit and a dismount reset it with the trapdoor's flag.
        Assert.Equal(3, Regex.Matches(runtime, @"_hatchShut\s*=\s*false\s*;\s*_cellarShut\s*=\s*false\s*;").Count);
        Assert.Equal(Regex.Matches(runtime, @"_hatchShut\s*=\s*false\s*;").Count, Regex.Matches(runtime, @"_cellarShut\s*=\s*false\s*;").Count);
    }

    [Fact]
    public void TheSnapAdapterNeverTreatsACellarDoorAsAFloor()
    {
        string snap = Code(Source("Pieces/LadderSnapAlignment.cs"));
        string align = Between(snap, @"private\s+static\s+bool\s+Align\s*\(", @"Transform\s+t\s*=\s*aimed\s*\.\s*transform\s*;");
        Assert.Matches(new Regex(@"CellarLeaves\s*\?\s*cellar\s*=\s*CellarLeaves\s*\.\s*Any\s*\?\s*aimed\s*\.\s*GetComponent\s*<\s*CellarLeaves\s*>\s*\(\s*\)\s*:\s*null\s*;"), align);
        string block = Between(align, @"if\s*\(\s*cellar\s*!=\s*null\s*\)\s*\{", @"return\s+Place\s*\(");
        // Any mode but Auto and Top, and Auto without room below, is refused (the ghost is marked invalid).
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*CellarLadder\s*\.\s*UsesAnchor\s*\(\s*mode\s*\)\s*\|\|\s*\(\s*mode\s*!=\s*LadderSnapMode\s*\.\s*Top\s*&&\s*!\s*RoomBelowCellar\s*\(\s*c\s*\)\s*\)\s*\)\s*\{\s*_refused\s*=\s*player\s*;"), block);
        Assert.Matches(new Regex(@"LadderEdgePlacement\s+anchor\s*=\s*CellarLadder\s*\.\s*Anchor\s*;"), block);
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*LadderSnap\s*\.\s*WithinPlacementReach\s*\([^;]*anchor\s*\.\s*Joint[^;]*\)\s*\{\s*_refused\s*=\s*player\s*;"), block);
        Assert.Matches(new Regex(@"if\s*\(\s*cellarFacing\s*\.\s*sqrMagnitude\s*<\s*0\.0001f\s*\)\s*\{\s*_refused\s*=\s*player\s*;\s*return\s+false\s*;\s*\}"), block);
        // Every path out of the block either places the ghost in the anchor or refuses it; none falls through to the
        // floor-edge snap.
        Assert.Equal(3, Regex.Matches(block, @"_refused\s*=\s*player\s*;").Count);
        Assert.Equal(3, Regex.Matches(block, @"return\s+false\s*;").Count);
        Assert.Matches(new Regex(@"return\s+Place\s*\(\s*g\s*,\s*Quaternion\s*\.\s*LookRotation\s*\(\s*cellarFacing\s*\.\s*normalized\s*,\s*Vector3\s*\.\s*up\s*\)\s*,\s*c\s*\.\s*TransformPoint\s*\(\s*LadderSegment\s*\.\s*Vector\s*\(\s*anchor\s*\.\s*Origin\s*\)\s*\)\s*,\s*out\s+aligned\s*\)\s*;\s*\}"), align.Substring(Index(align, @"if\s*\(\s*cellar\s*!=\s*null\s*\)")));
        // After both ladder-on-ladder placements and the trapdoor's anchor, before the floor-edge snap.
        Assert.True(Index(snap, @"TrapdoorLid\s*\?\s*hatch\s*=") < Index(snap, @"CellarLeaves\s*\?\s*cellar\s*="));
        Assert.True(Index(snap, @"CellarLeaves\s*\?\s*cellar\s*=") < Index(snap, @"LadderEdgeSnap\s*\.\s*TryPlace\s*\(\s*request"));
        // The room test is the trapdoor's, under the cover's own probe.
        Assert.Matches(new Regex(@"private\s+static\s+bool\s+RoomBelowCellar\s*\(\s*Transform\s+cover\s*\)\s*=>\s*RoomBelow\s*\(\s*cover\s*,\s*CellarLadder\s*\.\s*RoomProbe\s*,\s*CellarLadder\s*\.\s*RoomProbeDepth\s*\)\s*;"), snap);
        Assert.Matches(new Regex(@"private\s+static\s+bool\s+RoomBelowHatch\s*\(\s*Transform\s+hatch\s*\)\s*=>\s*RoomBelow\s*\(\s*hatch\s*,\s*HatchLadder\s*\.\s*RoomProbe\s*,\s*HatchLadder\s*\.\s*RoomProbeDepth\s*\)\s*;"), snap);
        string room = Between(snap, @"private\s+static\s+bool\s+RoomBelow\s*\(\s*Transform", @"private\s+static\s+readonly\s+RaycastHit\s*\[\s*\]\s+Under");
        Assert.Matches(new Regex(@"if\s*\(\s*count\s*>=\s*Under\s*\.\s*Length\s*\)\s*\{\s*return\s+false\s*;\s*\}"), room);
        Assert.Matches(new Regex(@"if\s*\(\s*collider\s*!=\s*null\s*&&\s*!\s*collider\s*\.\s*transform\s*\.\s*IsChildOf\s*\(\s*piece\s*\)\s*\)\s*\{\s*return\s+false\s*;\s*\}"), room);
    }

    [Fact]
    public void WithNoCellarDoorTheLadderAdaptersAskNothingOfOne()
    {
        // Every place a ladder adapter consults a cellar door is in a statement whose condition first asks
        // CellarLeaves.Any (false all session with CellarDoors/Enabled off), or inside a helper only such a statement
        // calls.
        var consults = new Regex(@"CellarLeaves\s*\.\s*(RouteShut|MayLandOn|OverPanel)\s*\(|GetComponent\w*\s*<\s*CellarLeaves\s*>|CrossesShutCellar\s*\(");
        var helpers = new Regex(@"private\s+(static\s+)?bool\s+(CrossesShutCellar)\s*\([^)]*\)\s*(=>|\{)");
        foreach (string relative in new[] { "Runtime/LadderRuntime.cs", "Runtime/LadderClearance.cs", "Pieces/LadderSnapAlignment.cs" })
        {
            string text = Code(Source(relative));
            var excluded = new List<(int Start, int End)>();
            foreach (Match helper in helpers.Matches(text)) { excluded.Add((helper.Index, BodyEnd(text, helper))); }
            int found = 0;
            foreach (Match consult in consults.Matches(text))
            {
                if (excluded.Any(range => consult.Index >= range.Start && consult.Index < range.End)) { continue; }
                found++;
                int statement = text.LastIndexOfAny(new[] { ';', '{', '}' }, consult.Index) + 1;
                string guard = text.Substring(statement, consult.Index - statement);
                Assert.True(Regex.IsMatch(guard, @"CellarLeaves\s*\.\s*Any\s*(&&|\?)"), relative + ": an ungated cellar door consult: " + guard.Trim());
            }

            Assert.True(found > 0, relative + " must consult the cellar doors.");
        }

        // And the one shared input that is not a consult: a dismount's Use is held back once either door family exists.
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        Assert.Single(Regex.Matches(runtime, @"\bCellarLeaves\s*\.\s*Any\s*\)"));
    }

    // ---- helpers ----

    /// <summary>LadderRuntime.TryExitTop's straight-ahead candidates: radius + 0.45 m and radius + 0.85 m from the run's
    /// top, toward the floor (piece -z).</summary>
    private static readonly float[] TopExitDistances = { Radius + 0.45f, Radius + 0.85f };

    private static float TopExitZ(float distance) => CellarGeometry.PlannedLadderOrigin.Z - distance;

    /// <summary>|y| of the unit normal of the face through three of a leaf's corners at an angle.</summary>
    private static double FaceNormalY(CellarLeaf leaf, float angle, int a, int b, int c)
    {
        ClimbPoint p = CellarGeometry.LeafCorner(leaf, angle, a);
        ClimbPoint q = CellarGeometry.LeafCorner(leaf, angle, b);
        ClimbPoint r = CellarGeometry.LeafCorner(leaf, angle, c);
        double ux = q.X - p.X, uy = q.Y - p.Y, uz = q.Z - p.Z;
        double vx = r.X - p.X, vy = r.Y - p.Y, vz = r.Z - p.Z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        return Math.Abs(ny) / Math.Sqrt(nx * nx + ny * ny + nz * nz);
    }

    private static int BodyEnd(string text, Match header)
    {
        int after = header.Index + header.Length;
        if (header.Value.EndsWith("=>", StringComparison.Ordinal)) { return text.IndexOf(';', after) + 1; }
        int depth = 1;
        for (int index = after; index < text.Length; index++)
        {
            if (text[index] == '{') { depth++; }
            else if (text[index] == '}' && --depth == 0) { return index + 1; }
        }

        return text.Length;
    }

    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Between(string text, string startPattern, string endPattern)
    {
        Match start = Regex.Match(text, startPattern);
        Assert.True(start.Success, "Missing: " + startPattern);
        Match end = new Regex(endPattern).Match(text, start.Index + start.Length);
        Assert.True(end.Success, "Missing: " + endPattern);
        return text.Substring(start.Index, end.Index - start.Index);
    }

    private static int Index(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        Assert.True(match.Success, "Missing: " + pattern);
        return match.Index;
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(ProductRoot, relative));

    private static string ProductRoot => Path.Combine(RepoRoot, "src", "ConcernedLadders");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("ConcernedCatMods.sln not found above the test output.");
        }
    }
}
