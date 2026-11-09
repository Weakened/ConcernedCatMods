using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 slice 4, the ladder integration. The rules are game-free (<see cref="HatchLadder"/>,
/// <see cref="ClimbControls.UseAsksDismount"/>, <see cref="ClimbControls.HoldsBackInteract"/>) and tested directly; the
/// Unity adapters that ask them (<c>LadderSnapAlignment</c>, <c>LadderRuntime</c>, <c>LadderClearance</c>,
/// <c>TrapdoorLid</c>) are pinned by source: each asks, in the place it must, and asks nothing while no trapdoor exists,
/// so with <c>Trapdoors/Enabled</c> off every ladder path is the one it was before.</summary>
public sealed class HatchLadderTests
{
    private const float Radius = 0.4f;
    private const float Height = 1.8f;
    private const float Margin = 0.05f;

    // ---- Anchor ----

    [Theory]
    [InlineData((int)LadderSnapMode.Auto, true)]
    [InlineData((int)LadderSnapMode.Top, true)]
    [InlineData((int)LadderSnapMode.Bottom, false)]
    [InlineData((int)LadderSnapMode.LeftSide, false)]
    [InlineData((int)LadderSnapMode.RightSide, false)]
    public void OnlyAutoAndTopHangALadderInTheHatch(int mode, bool anchors) =>
        Assert.Equal(anchors, HatchLadder.UsesAnchor((LadderSnapMode)mode));

    [Fact]
    public void TheAnchorHangsTheLadderFromTheFrontBandFacingTheHinge()
    {
        LadderEdgePlacement anchor = HatchLadder.Anchor;
        Assert.Equal(HatchGeometry.LadderOrigin.X, anchor.Origin.X);
        Assert.Equal(HatchGeometry.LadderOrigin.Y, anchor.Origin.Y);
        Assert.Equal(HatchGeometry.LadderOrigin.Z, anchor.Origin.Z);
        // Climbing side toward the hinge (+z), which is where the open lid stands; the ladder's top is the snap plane.
        Assert.Equal(0f, anchor.FacingX);
        Assert.Equal(1f, anchor.FacingZ);
        Assert.True(HatchGeometry.HingeZ > 0f);
        Assert.Equal(HatchGeometry.SnapPlaneY, anchor.Origin.Y + LadderEdgeSnap.SectionHeight, 5);
        Assert.True(anchor.OnFloor);
        Assert.Equal(0f, anchor.TurnDegrees);
        // The joint the reach is measured to is the middle of the front band's inner face, at the snap plane.
        Assert.Equal(new ClimbPoint(0f, HatchGeometry.SnapPlaneY, -HatchGeometry.HalfAperture), anchor.Joint);
        // The rails' back face rests the edge snap's own gap inside the frame, never in it.
        float back = anchor.Origin.Z + LadderFootprint.Section.Back;
        Assert.Equal(-HatchGeometry.HalfAperture + LadderEdgeSnap.Gap, back, 5);
        Assert.True(HatchGeometry.ClimberMargin(Radius) > 0f, "A climber in the anchored ladder's front lane must fit the opening.");
    }

    [Fact]
    public void TheAnchorIsWhereTheEdgeSnapWouldHangALadderFromTheOpeningsFrontEdge()
    {
        // A 1.6 m slab whose +z edge is the front band's inner face: the edge snap hangs a ladder below that edge, facing
        // out of it (+z), exactly where the anchor puts it. The two conventions cannot drift apart unnoticed.
        float face = -HatchGeometry.HalfAperture;
        var corners = new List<ClimbPoint>
        {
            new ClimbPoint(-0.8f, 0f, face - 1.6f), new ClimbPoint(0.8f, 0f, face - 1.6f),
            new ClimbPoint(-0.8f, 0f, face), new ClimbPoint(0.8f, 0f, face),
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
        LadderEdgePlacement anchor = HatchLadder.Anchor;
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
        ClimbPoint probe = HatchLadder.RoomProbe;
        Assert.True(probe.Y < HatchGeometry.FrameBottomY, "The room test must start below the hatch's own colliders.");
        Assert.True(probe.Z > HatchGeometry.LadderOrigin.Z + LadderFootprint.Section.Back);
        Assert.True(probe.Z < HatchGeometry.LadderOrigin.Z + LadderFootprint.Section.Front);
        Assert.True(probe.Z > -HatchGeometry.HalfAperture, "Inside the opening, not under the frame.");
        Assert.Equal(0f, probe.X);
        float reaches = probe.Y - HatchLadder.RoomProbeDepth;
        Assert.InRange(reaches, HatchGeometry.LadderOrigin.Y, HatchGeometry.LadderOrigin.Y + 0.25f);
    }

    // ---- Passage ----

    private static float FrontLane => HatchGeometry.ClimberCentreZ(LadderFace.Front, Radius);

    private static float RearLane => HatchGeometry.ClimberCentreZ(LadderFace.Rear, Radius);

    [Fact]
    public void ATopExitFromTheFrontLaneCrossesTheOpening()
    {
        // Feet a metre under the snap plane, rising onto a floor at the plane: the capsule passes through the hatch.
        Assert.True(HatchLadder.Crosses(0f, FrontLane, Radius, -1f, HatchGeometry.TopY + 0.3f + Height));
        // A descending entry from the floor above, down into the lane.
        Assert.True(HatchLadder.Crosses(0f, FrontLane, Radius, -1.3f, 0.1f + 0.3f + Height));
    }

    [Fact]
    public void TheRearLaneAndTheFloorAroundAreNotTheOpening()
    {
        Assert.False(HatchLadder.Crosses(0f, RearLane, Radius, -1f, 0.3f + Height));
        // Standing on the floor tile in front of the hatch, or beside it.
        Assert.False(HatchLadder.Crosses(0f, -1.5f, Radius, 0.1f, 0.1f + Height));
        Assert.False(HatchLadder.Crosses(1.5f, 0f, Radius, 0.1f, 0.1f + Height));
    }

    [Fact]
    public void ARouteWhollyAboveOrBelowTheHatchDoesNotCrossIt()
    {
        // Standing on a closed lid: the capsule starts above the frame's top.
        Assert.False(HatchLadder.Crosses(0f, 0f, Radius, HatchGeometry.TopY + 0.02f, HatchGeometry.TopY + 0.02f + Height));
        // The bottom step-off in a 2 m basement under the hatch, at the low route lift, stays under the frame: a closed lid
        // never refuses getting off at the foot of the ladder.
        float feet = HatchGeometry.LadderOrigin.Y;
        Assert.False(HatchLadder.Crosses(0f, FrontLane, Radius, feet, feet + LadderRules.RouteLift + Height + Margin));
    }

    [Fact]
    public void TheOpeningIsWidenedByTheRadiusOnEverySideAndAtTheCorners()
    {
        float edge = HatchGeometry.HalfAperture + Radius;
        Assert.True(HatchLadder.Crosses(edge - 0.01f, 0f, Radius, -1f, 1f));
        Assert.True(HatchLadder.Crosses(0f, -(edge - 0.01f), Radius, -1f, 1f));
        Assert.True(HatchLadder.Crosses(edge - 0.01f, edge - 0.01f, Radius, -1f, 1f));
        Assert.False(HatchLadder.Crosses(edge + 0.01f, 0f, Radius, -1f, 1f));
        Assert.False(HatchLadder.Crosses(0f, edge + 0.01f, Radius, -1f, 1f));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0.4f, -1f, 1f)]
    [InlineData(0f, float.PositiveInfinity, 0.4f, -1f, 1f)]
    [InlineData(5f, 5f, float.NaN, -1f, 1f)]
    [InlineData(5f, 5f, -0.1f, -1f, 1f)]
    [InlineData(5f, 5f, 0.4f, float.NaN, 1f)]
    [InlineData(5f, 5f, 0.4f, -1f, float.NegativeInfinity)]
    public void ARouteThatCannotBeMeasuredIsHeldToThePassageRule(float x, float z, float radius, float low, float high) =>
        Assert.True(HatchLadder.Crosses(x, z, radius, low, high));

    [Fact]
    public void TheHeightOrderDoesNotMatter() =>
        Assert.Equal(HatchLadder.Crosses(0f, FrontLane, Radius, -1f, 2f), HatchLadder.Crosses(0f, FrontLane, Radius, 2f, -1f));

    [Fact]
    public void ARouteThroughTheOpeningNeedsALidThatIsAPassage()
    {
        Assert.True(HatchLadder.AllowsRoute(false, false));
        Assert.True(HatchLadder.AllowsRoute(false, true));
        Assert.False(HatchLadder.AllowsRoute(true, false));
        Assert.True(HatchLadder.AllowsRoute(true, true));

        // Through the model: closed, opening, open, and open but asked to close.
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(0f, false)));
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(0.5f, true)));
        Assert.True(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(1f, true)));
        Assert.False(HatchLadder.AllowsRoute(true, HatchMotion.IsPassage(1f, false)));
    }

    // ---- Landing ----

    [Fact]
    public void TheFrameIsAlwaysAFloorAndTheLidOnlyWhenClosedAndStayingClosed()
    {
        Assert.True(HatchLadder.IsLandingSurface(HatchPart.None, false));
        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Frame, false));
        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Frame, true));
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Unknown, true));
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, false));
        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Lid, true));

        Assert.True(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0f, false)));
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0f, true)), "Asked to open: not a floor.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(0.3f, false)), "Closing: not a floor.");
        Assert.False(HatchLadder.IsLandingSurface(HatchPart.Lid, HatchMotion.IsLanding(1f, true)), "Open: not a floor.");
    }

    [Fact]
    public void ATiltedLidWouldPassTheLandingSearchsSlopeTestSoTheLidRuleIsWhatRefusesIt()
    {
        // The search accepts a surface whose normal has y >= 0.65. The lid's top face at 45 degrees has y = cos 45 = 0.71:
        // without the lid rule, a lid half way through its swing would be a "floor".
        double normalY = Math.Cos(45.0 * Math.PI / 180.0);
        Assert.True(normalY >= 0.65);
        Assert.False(HatchMotion.IsLanding(HatchMotion.ProgressForAngle(45f), false));
        Assert.False(HatchMotion.IsLanding(HatchMotion.ProgressForAngle(45f), true));
    }

    // ---- Controls ----

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void AHoveredDoorTakesUseAndTheLadderDoesNotAlsoDismount(bool mountFrame, bool hoversInteractable, bool asks) =>
        Assert.Equal(asks, ClimbControls.UseAsksDismount(mountFrame, hoversInteractable));

    [Fact]
    public void AUseThatAskedForADismountIsNotAlsoDeliveredToADoorOnceTrapdoorsExist()
    {
        Assert.True(ClimbControls.HoldsBackInteract(10, 10, -1, false), "A Jump or Drop frame holds back interaction, as before.");
        Assert.True(ClimbControls.HoldsBackInteract(10, 10, -1, true));
        Assert.True(ClimbControls.HoldsBackInteract(10, -1, 10, true));
        Assert.False(ClimbControls.HoldsBackInteract(10, -1, 10, false), "Without trapdoors the dismount frame changes nothing.");
        Assert.False(ClimbControls.HoldsBackInteract(11, 10, 10, true), "Only that frame.");
    }

    // ---- The Unity adapters, by source ----

    [Fact]
    public void TheLidRuntimeIsTheProductionCallerOfIsPassageAndIsLanding()
    {
        string lid = Code(Source("Runtime/TrapdoorLid.cs"));
        Assert.Single(Regex.Matches(lid, @"HatchMotion\s*\.\s*IsPassage\s*\(\s*_progress\s*,\s*open\s*\)"));
        Assert.Single(Regex.Matches(lid, @"HatchMotion\s*\.\s*IsLanding\s*\(\s*_progress\s*,\s*open\s*\)"));
        // Both read the native state through the one guarded helper, which also refuses a lid that has not settled, has
        // stopped on a fault or has no ZDO.
        string read = Between(lid, @"private\s+bool\s+TryReadOpen\s*\(", @"return\s+true\s*;");
        Assert.Matches(new Regex(@"!\s*enabled\s*\|\|\s*!\s*_settled\s*\|\|\s*view\s*==\s*null\s*\|\|\s*!\s*view\s*\.\s*IsValid\s*\(\s*\)\s*\|\|\s*_hinge\s*==\s*null"), read);
        Assert.Matches(new Regex(@"HatchLadder\s*\.\s*AllowsRoute\s*\("), lid);
        Assert.Matches(new Regex(@"HatchLadder\s*\.\s*Crosses\s*\("), lid);
        Assert.Matches(new Regex(@"HatchLadder\s*\.\s*IsLandingSurface\s*\("), lid);
        // A ghost or icon render (no ZDO) never shuts a route.
        Assert.Matches(new Regex(@"!\s*lid\s*\.\s*_view\s*\.\s*IsValid\s*\(\s*\)\s*\)\s*\{\s*continue\s*;"), lid);

        // Nothing else in the product decides passage or landing.
        foreach (string file in ProductFiles())
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            if (relative == "Runtime/TrapdoorLid.cs" || relative.StartsWith("Domain/Doors/", StringComparison.Ordinal)) { continue; }
            string text = Code(File.ReadAllText(file));
            Assert.DoesNotMatch(new Regex(@"\bIsPassage\s*\(|\bIsLanding\s*\("), text);
        }
    }

    [Fact]
    public void TheLandingSearchRefusesANonLandingLidBeforeItsSlopeTest()
    {
        string clearance = Code(Source("Runtime/LadderClearance.cs"));
        string surface = Between(clearance, @"private\s+bool\s+TrySurface\s*\(", @"return\s+available\s*;");
        int gate = Index(surface, @"if\s*\(\s*TrapdoorLid\s*\.\s*Any\s*&&\s*Solid\s*\(\s*hit\s*\.\s*collider\s*\)\s*&&\s*!\s*TrapdoorLid\s*\.\s*MayLandOn\s*\(\s*hit\s*\.\s*collider\s*\)\s*\)\s*\{\s*floor\s*=\s*default\s*;\s*return\s+false\s*;");
        int slope = Index(surface, @"hit\s*\.\s*normal\s*\.\s*y\s*<\s*0\.65f");
        Assert.True(gate < slope, "A lid must be refused before the slope test can accept its tilted face.");
        Assert.Single(Regex.Matches(clearance, @"MayLandOn\s*\("));
    }

    [Fact]
    public void EveryExitRouteIsCheckedAgainstTheTrapdoorsBeforeItIsCommitted()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string plan = Between(runtime, @"private\s+bool\s+PlanExitTo\s*\(", @"private\s+bool\s+CrossesShutHatch\s*\(");
        int gate = Index(plan, @"if\s*\(\s*TrapdoorLid\s*\.\s*Any\s*&&\s*CrossesShutHatch\s*\(\s*feet\s*,\s*path\s*,\s*raised\s*\)\s*\)");
        int sweep = Index(plan, @"_clearance\s*\.\s*CanFollow\s*\(\s*feet\s*,\s*path\s*\)");
        int commit = Index(plan, @"_mode\s*=\s*Mode\s*\.\s*Exiting\s*;");
        Assert.True(gate < sweep && sweep < commit, "The trapdoor test must refuse a route before it is swept and committed.");
        Assert.Single(Regex.Matches(runtime, @"_mode\s*=\s*Mode\s*\.\s*Exiting\s*;"));

        // The helper tests where the route starts and every point of it, over the capsule's whole height.
        string helper = Between(runtime, @"private\s+bool\s+CrossesShutHatch\s*\(", @"private\s+const\s+float\s+RouteTopMargin");
        Assert.Matches(new Regex(@"float\s+top\s*=\s*highest\s*\+\s*_clearance\s*\.\s*Height\s*\+\s*RouteTopMargin\s*;"), helper);
        Assert.Matches(new Regex(@"TrapdoorLid\s*\.\s*RouteShut\s*\(\s*start\s*,\s*radius\s*,\s*low\s*,\s*top\s*\)"), helper);
        Assert.Matches(new Regex(@"foreach\s*\(\s*Vector3\s+point\s+in\s+path\s*\)\s*\{\s*if\s*\(\s*TrapdoorLid\s*\.\s*RouteShut\s*\(\s*point\s*,\s*radius\s*,\s*low\s*,\s*top\s*\)\s*\)\s*\{\s*return\s+true\s*;"), helper);
    }

    [Fact]
    public void TheTopExitAndTheDescendingEntryNeedAnOpenLid()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string top = Between(runtime, @"private\s+bool\s+TryExitTop\s*\(", @"Vector3\s+inward\s*=\s*Toward\s*\(\s*\)\s*;");
        Assert.Matches(new Regex(@"if\s*\(\s*TrapdoorLid\s*\.\s*Any\s*&&\s*_body\s*!=\s*null\s*&&\s*TrapdoorLid\s*\.\s*RouteShut\s*\("), top);
        Assert.Matches(new Regex(@"_hatchShut\s*=\s*true\s*;[^}]*return\s+false\s*;"), top);

        string mount = Between(runtime, @"private\s+bool\s+TryPlanMount\s*\(", @"private\s+static\s+bool\s+StandsOnLadder\s*\(");
        int gate = Index(mount, @"if\s*\(\s*topEntry\s*&&\s*TrapdoorLid\s*\.\s*Any\s*&&\s*TrapdoorLid\s*\.\s*RouteShut\s*\(\s*line\s*,");
        int refusal = Index(mount, @"first\s*=\s*Keep\s*\(\s*first\s*,\s*ClimbRefusal\s*\.\s*HatchClosed\s*\)\s*;\s*continue\s*;");
        int route = Index(mount, @"Vector3\s*\[\s*\]\s*\?\s*route\s*=\s*null\s*;");
        Assert.True(gate < refusal && refusal < route, "A descending entry is refused before any route is chosen.");
    }

    [Fact]
    public void UseWhileAttachedGoesToTheHoveredDoorOnce()
    {
        string runtime = Code(Source("Runtime/LadderRuntime.cs"));
        string update = Between(runtime, @"internal\s+void\s+Update\s*\(\s*float\s+deltaTime\s*\)", @"private\s+static\s+bool\s+UsePressed\s*\(");
        Assert.Matches(new Regex(@"if\s*\(\s*_mode\s*==\s*Mode\s*\.\s*Climbing\s*&&\s*action\s*==\s*ClimbAction\s*\.\s*Use\s*&&\s*ClimbControls\s*\.\s*UseAsksDismount\s*\(\s*Time\s*\.\s*frameCount\s*==\s*_mountFrame\s*,\s*HoversInteractable\s*\(\s*_player\s*\)\s*\)\s*\)\s*\{\s*_dismountUseFrame\s*=\s*Time\s*\.\s*frameCount\s*;\s*if\s*\(\s*!\s*TrySafeDismount\s*\(\s*\)\s*\)"), update);
        // The only dismount-on-Use in Update is that one.
        Assert.Single(Regex.Matches(update, @"TrySafeDismount\s*\("));
        string suppress = Between(runtime, @"internal\s+bool\s+SuppressInteract\s*\(", @"private\s+static\s+bool\s+HoversInteractable\s*\(");
        Assert.Matches(new Regex(@"ClimbControls\s*\.\s*HoldsBackInteract\s*\(\s*Time\s*\.\s*frameCount\s*,\s*_departFrame\s*,\s*_dismountUseFrame\s*,\s*TrapdoorLid\s*\.\s*Any\s*\)"), suppress);
        // The hover test is the interactable one, so a trapdoor (a native Door) counts.
        Assert.Matches(new Regex(@"hover\s*\.\s*GetComponentInParent\s*<\s*Interactable\s*>\s*\(\s*\)\s*!=\s*null"), runtime);
        // The climber holds still on the rungs through the game's interact animation (a minor action) rather than letting go.
        Assert.Matches(new Regex(@"player\s*\.\s*InMinorAction\s*\(\s*\)\s*\|\|\s*BuildAction\s*\(\s*player\s*\)\s*\)\s*\{\s*Hold\s*\("), runtime);
    }

    [Fact]
    public void TheSnapAdapterHangsALadderInTheHatchOnlyWhileTrapdoorsExist()
    {
        string snap = Code(Source("Pieces/LadderSnapAlignment.cs"));
        string align = Between(snap, @"private\s+static\s+bool\s+Align\s*\(", @"//\s*3\.\s*Aimed at a floor or a wall\.|Transform\s+t\s*=\s*aimed\s*\.\s*transform\s*;");
        Assert.Matches(new Regex(@"TrapdoorLid\s*\?\s*hatch\s*=\s*TrapdoorLid\s*\.\s*Any\s*\?\s*aimed\s*\.\s*GetComponent\s*<\s*TrapdoorLid\s*>\s*\(\s*\)\s*:\s*null\s*;"), align);
        Assert.Matches(new Regex(@"if\s*\(\s*hatch\s*!=\s*null\s*&&\s*HatchLadder\s*\.\s*UsesAnchor\s*\(\s*mode\s*\)\s*&&\s*\(\s*mode\s*==\s*LadderSnapMode\s*\.\s*Top\s*\|\|\s*RoomBelowHatch\s*\(\s*aimed\s*\.\s*transform\s*\)\s*\)\s*\)"), align);
        Assert.Matches(new Regex(@"LadderEdgePlacement\s+anchor\s*=\s*HatchLadder\s*\.\s*Anchor\s*;"), align);
        // Out of reach is refused (the ghost is marked invalid), never moved.
        Assert.Matches(new Regex(@"if\s*\(\s*!\s*LadderSnap\s*\.\s*WithinPlacementReach\s*\([^;]*anchor\s*\.\s*Joint[^;]*\)\s*\{\s*_refused\s*=\s*player\s*;"), align);
        // The anchor comes after both ladder-on-ladder placements and before the floor-edge snap.
        Assert.True(Index(snap, @"LadderSegment\s*\?\s*column\s*=") < Index(snap, @"TrapdoorLid\s*\?\s*hatch\s*="));
        Assert.True(Index(snap, @"TrapdoorLid\s*\?\s*hatch\s*=") < Index(snap, @"LadderEdgeSnap\s*\.\s*TryPlace\s*\(\s*request"));
        // The lid's own kinematic body is aimable only while trapdoors exist.
        Assert.Matches(new Regex(@"private\s+static\s+bool\s+OnTrapdoorLid\s*\(\s*Collider\s+collider\s*\)\s*=>\s*TrapdoorLid\s*\.\s*Any\s*&&"), snap);
        Assert.Matches(new Regex(@"hit\s*\.\s*collider\s*\.\s*attachedRigidbody\s*!=\s*null\s*&&\s*!\s*OnTrapdoorLid\s*\(\s*hit\s*\.\s*collider\s*\)"), snap);
    }

    [Fact]
    public void WithNoTrapdoorTheLadderAdaptersAskNothingOfOne()
    {
        // Every place a ladder adapter consults a trapdoor is in a statement whose condition first asks TrapdoorLid.Any
        // (false all session with Trapdoors/Enabled off), or inside a helper that only such a statement calls (or that
        // asks it itself, pinned above).
        var consults = new Regex(@"TrapdoorLid\s*\.\s*(RouteShut|MayLandOn)\s*\(|GetComponent\w*\s*<\s*TrapdoorLid\s*>|CrossesShutHatch\s*\(");
        var helpers = new Regex(@"private\s+(static\s+)?bool\s+(CrossesShutHatch|OnTrapdoorLid)\s*\([^)]*\)\s*(=>|\{)");
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
                Assert.True(Regex.IsMatch(guard, @"TrapdoorLid\s*\.\s*Any\s*(&&|\?)"), relative + ": an ungated trapdoor consult: " + guard.Trim());
            }

            Assert.True(found > 0, relative + " must consult the trapdoors.");
        }
    }

    // ---- helpers ----

    /// <summary>Where the body of the member whose header <paramref name="header"/> matched ends: the statement after an
    /// arrow, or the brace that closes its block.</summary>
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

    private static IEnumerable<string> ProductFiles() =>
        Directory.EnumerateFiles(ProductRoot, "*.cs", SearchOption.AllDirectories).Where(file =>
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            return !relative.StartsWith("obj/", StringComparison.Ordinal) && !relative.StartsWith("bin/", StringComparison.Ordinal);
        });

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
