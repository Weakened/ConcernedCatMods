using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-005 #486 slice 3, the leaf runtime. The pair's motion is the trapdoor's (<see cref="HatchMotion"/>) through
/// <see cref="CellarPairOccupancy{TLeaves}"/>; <see cref="CellarPairTests"/> drive it with scripts. Here it is driven by a
/// game-free model of the runtime's own query (each leaf's box at the asked angle, shrunk by the skin) against the
/// obstacles the design record names, and the Unity adapter (<c>Runtime/CellarLeaves.cs</c>) is pinned by source: it reads
/// the native door state, writes nothing to the network, ignores exactly the piece's own six colliders by identity, turns
/// only the two pivots from one progress, and does not allocate per frame.</summary>
public sealed class CellarLeavesTests
{
    private const string Runtime = "Runtime/CellarLeaves.cs";

    private const string Adapter = "Pieces/CellarDoorPieceRegistration.cs";

    private const float Frame = 1f / 60f;

    // ---- The query's exclusions ---------------------------------------------------------------------------------------

    [Fact]
    public void TheQueriesIgnoreExactlyThePiecesOwnSixColliders()
    {
        var expected = CellarBlueprint.FrameMeshes.Select(mesh => mesh.Name).ToList();
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            expected.Add(CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(leaf) + "/" + CellarBlueprint.LeafCollider(leaf).Name);
        }

        Assert.Equal(expected, CellarBlueprint.QueryIgnores);
        Assert.Equal(6, CellarBlueprint.QueryIgnores.Count);
        Assert.Equal(CellarBlueprint.QueryIgnores.Count, CellarBlueprint.QueryIgnores.Distinct(StringComparer.Ordinal).Count());
        // Nothing that is not a collider the adapter builds: no presentation root, visual, hinge mark or donor leaf.
        var notColliders = new List<string> { TrapdoorBlueprint.NewRoot, TrapdoorBlueprint.WornRoot, TrapdoorBlueprint.LeafChild, CellarBlueprint.SlopeRoot };
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            notColliders.Add(CellarBlueprint.Hinge(leaf));
            notColliders.Add(CellarBlueprint.LeafVisual(leaf).Name);
            notColliders.AddRange(CellarBlueprint.HingeMarks(leaf).Select(mark => mark.Name));
        }

        foreach (string path in CellarBlueprint.QueryIgnores)
        {
            Assert.DoesNotContain(path.Split('/').Last(), notColliders);
        }
    }

    // ---- The pair against the design record's obstacles, through the model of the runtime's query -----------------------

    [Fact]
    public void ARoofLowerThanTheOpenLeavesHoldsBothShortOfOpenAndAHigherOneDoesNot()
    {
        // A roof beam over the right half only, just under the open leaves' top (2.03 m): the right leaf meets it and
        // the left leaf, which never would, is held at the same angle.
        var low = new Obstacle(new ClimbPoint(0.05f, 1.9f, -1.3f), new ClimbPoint(1.2f, 2.2f, 1.2f));
        float progress = Run(low, open: true, from: 0f);
        float held = HatchMotion.Angle(progress);
        Assert.InRange(held, 30f, CellarGeometry.OpenAngleDegrees - 1f);
        Assert.False(HatchMotion.IsPassage(progress, true));
        Assert.False(low.Asked.Any(pose => pose.Leaf == CellarLeaf.Left && pose.Occupied), "The left leaf cannot reach the beam.");
        Assert.All(low.Asked.Where(pose => pose.Occupied), pose => Assert.True(pose.Angle > held));

        var high = new Obstacle(new ClimbPoint(0.05f, 2.1f, -1.3f), new ClimbPoint(1.2f, 2.4f, 1.2f));
        Assert.True(HatchMotion.IsPassage(Run(high, open: true, from: 0f), true));
    }

    [Theory]
    [InlineData(0.235f, false)]
    [InlineData(0.235f, true)]
    [InlineData(0.24f, false)]
    [InlineData(0.24f, true)]
    [InlineData(0.25f, false)]
    [InlineData(0.25f, true)]
    [InlineData(0.275f, false)]
    [InlineData(0.275f, true)]
    [InlineData(0.4f, false)]
    [InlineData(0.4f, true)]
    public void AWallReachingIntoTheClosedLeavesOnTheRearEdgeHoldsTheCoverShutWhateverItsStoredState(float reach, bool storedOpen)
    {
        // Section 2a's walls that reach past the closed leaves' rear edge (z 0.79): the roof-top 67 and ashwood cross
        // walls, the log walls, and the stave, stake and dvergr walls. Some are in the closed pose only (the open leaves
        // reach back only to z 0.763), some in every pose. Either way the first pose is closed (lead default 15), even
        // after a Use stored the door open, and no later frame opens it: fail-closed, never part open while the wall
        // stands. HatchMotion.Settle alone would load the shallower walls open, which is what CellarSettle exists for.
        // (Stored open means a Use asked for open while the wall held the cover; the world was then saved.)
        Obstacle wall = RearWall(reach);
        Assert.Equal(0f, Settle(wall, storedOpen));
        Assert.Equal(0f, Run(wall, open: true, from: 0f));
        Assert.Contains(wall.Asked, pose => pose.Angle == 0f && pose.Occupied);
    }

    [Fact]
    public void WithoutTheFixedObstructionRuleTheShallowerWallsWouldLoadACoverOpen()
    {
        // Why CellarSettle is needed: the trapdoor's first-pose rule takes the clear open end for these walls.
        Assert.Equal(1f, HatchMotion.Settle(false, Pair(RearWall(0.235f))));
        Assert.Equal(1f, HatchMotion.Settle(true, Pair(RearWall(0.24f))));
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.2f)]
    [InlineData(0.209f)]
    public void AWallWithinTheHeaderOnTheRearEdgeLeavesTheCoverFreeToOpenAndClose(float reach)
    {
        // Up to the roof-top and beam walls (0.205-0.209 m): never in a pose.
        Obstacle wall = RearWall(reach);
        Assert.Equal(0f, Settle(wall, false));
        Assert.Equal(1f, Settle(wall, true));
        float opened = Run(wall, open: true, from: 0f);
        Assert.True(HatchMotion.IsPassage(opened, true));
        Assert.True(HatchMotion.IsLanding(Run(wall, open: false, from: opened), false));
        Assert.DoesNotContain(wall.Asked, pose => pose.Occupied);
    }

    [Fact]
    public void SomeoneStandingOnTheClosedLeavesIsContactNotOccupancyButHoldsThemShut()
    {
        // Feet resting on the right leaf's top face, the box's bottom at the slope's height under its rear edge: the
        // skin makes the closed pose clear, so the cover settles closed under them; opening lifts the leaf into them
        // within the skin, so it holds a few degrees open at most, never a passage.
        float v = CellarGeometry.LeafStartV + CellarGeometry.LeafLength / 2f;
        ClimbPoint centre = CellarGeometry.ToPiece(0.4f, 0f, v);
        float bottom = CellarGeometry.TopAt(centre.Z + 0.15f);
        var feet = new Obstacle(new ClimbPoint(0.25f, bottom, centre.Z - 0.15f), new ClimbPoint(0.55f, bottom + 1.8f, centre.Z + 0.15f), mover: true);
        Assert.Equal(0f, Settle(feet, false));
        float held = Run(feet, open: true, from: 0f);
        Assert.InRange(HatchMotion.Angle(held), 0f, HatchMotion.MaximumAngleStepDegrees);
        Assert.False(HatchMotion.IsPassage(held, true));
        Assert.Contains(feet.Asked, pose => pose.Occupied);
    }

    [Fact]
    public void SomeoneInTheEntranceUnderAClosedCoverAtLoadIsNeverPutUnderTheClosedLeaves()
    {
        // A climber's head and shoulders in the opening, up into where the closed right leaf lies: the closed pose is
        // occupied by a mover, so the first pose is the trapdoor's (the clear open end), and the cover shuts once they
        // have gone.
        ClimbPoint under = CellarGeometry.ToPiece(0.4f, -0.03f, CellarGeometry.LeafStartV + CellarGeometry.LeafLength / 2f);
        var head = new Obstacle(new ClimbPoint(0.2f, under.Y - 1.6f, under.Z - 0.2f), new ClimbPoint(0.6f, under.Y + 0.02f, under.Z + 0.2f), mover: true);
        float settled = Settle(head, false);
        Assert.Equal(1f, settled);
        Assert.Equal(HatchMotion.Settle(false, Pair(head)), settled);
        var gone = new Obstacle(new ClimbPoint(5f, 5f, 5f), new ClimbPoint(6f, 6f, 6f), mover: true);
        Assert.True(HatchMotion.IsLanding(Run(gone, open: false, from: settled), false));
    }

    // ---- The first-pose rule, by script ------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClearCoverTakesItsStoredEndAndAsksOnlyAboutTheClosedPoseAndThatEnd(bool open)
    {
        var all = new Scripted(_ => false);
        var movers = new Scripted(_ => throw new InvalidOperationException("Not asked when the closed pose is clear."));
        Assert.Equal(open ? 1f : 0f, CellarSettle.First(open, all, movers));
        // The closed pose, then the trapdoor's own first question, the stored end.
        Assert.Equal(new[] { 0f, open ? HatchMotion.Angle(1f) : 0f }, all.Asked);
    }

    [Fact]
    public void AStoredOpenCoverWithAFixedThingOnlyInTheClosedPoseLoadsShut()
    {
        // A Use asked it to open while a wall held it shut, and the world was saved: the open pose is clear, but the
        // cover loads shut, because it could never close again past the wall.
        var all = new Scripted(angle => angle < 30f);
        var movers = new Scripted(_ => false);
        Assert.Equal(0f, CellarSettle.First(true, all, movers));
        Assert.Equal(1f, HatchMotion.Settle(true, new Scripted(angle => angle < 30f)));
    }

    [Fact]
    public void AStoredOpenCoverWhoseOpenPoseIsOccupiedFallsBackToTheTrapdoorsRule()
    {
        // The closed pose clear, something where the open leaves stand: the trapdoor's other-end rule, closed.
        Func<float, bool> rule = angle => angle > 60f;
        Assert.Equal(HatchMotion.Settle(true, new Scripted(rule)), CellarSettle.First(true, new Scripted(rule), new Scripted(rule)));
        Assert.Equal(0f, CellarSettle.First(true, new Scripted(rule), new Scripted(rule)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoredEndHeldOnlyByFixedThingsSettlesClosed(bool open)
    {
        // Everything is occupied for the full probe (a wall in every pose), nothing for the movers' probe.
        var all = new Scripted(_ => true);
        var movers = new Scripted(_ => false);
        Assert.Equal(0f, CellarSettle.First(open, all, movers));
        Assert.Equal(new[] { 0f }, all.Asked);
        Assert.Equal(new[] { 0f }, movers.Asked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SomeoneInTheClosedPoseFallsBackToTheTrapdoorsRuleUnchanged(bool open)
    {
        // A mover in the closed pose only: exactly HatchMotion.Settle's answer, the open end.
        Func<float, bool> rule = angle => angle < 30f;
        float expected = HatchMotion.Settle(open, new Scripted(rule));
        Assert.Equal(1f, expected);
        Assert.Equal(expected, CellarSettle.First(open, new Scripted(rule), new Scripted(rule)));
        // Nothing clear anywhere and a mover in the closed pose: the stored end, as the trapdoor's.
        Assert.Equal(open ? 1f : 0f, CellarSettle.First(open, new Scripted(_ => true), new Scripted(_ => true)));
    }

    // ---- The Unity adapter, by source ----------------------------------------------------------------------------------

    [Fact]
    public void TheLeavesReadTheNativeDoorStateAndWriteNothingToTheNetwork()
    {
        string leaves = Code(Source(Runtime));
        // Three reads of the one native value: the first pose (TrySettle), each frame (Update), and the ladder's passage
        // and landing questions (TryReadOpen, slice 4), each after IsValid().
        Assert.Equal(3, Regex.Matches(leaves, @"\.\s*GetZDO\s*\(\s*\)\s*\.\s*GetInt\s*\(\s*ZDOVars\s*\.\s*s_state\s*\)").Count);
        Assert.Equal(3, Regex.Matches(leaves, @"view\s*==\s*null\s*\|\|\s*!\s*view\s*\.\s*IsValid\s*\(\s*\)").Count);
        Assert.Equal(3, Regex.Matches(leaves, @"\bGetZDO\b").Count);
        foreach (string forbidden in new[]
                 {
                     @"\bInvokeRPC\b", @"\bRoutedRPC\b", @"\.\s*Register\s*<", @"\.\s*Register\s*\(", @"\.\s*Set\s*\(",
                     @"\bClaimOwnership\b", @"\bSetOwner\b", @"\bDestroy\w*\s*\(", @"\bInteract\s*\(", @"\bUseDoor\b",
                     @"\bMoveRotation\b", @"\bMovePosition\b", @"\bAddForce\w*\b", @"\bvelocity\b", @"\bangularVelocity\b",
                     @"\bSetInteger\b", @"\bisKinematic\s*=", @"\bZNetScene\b", @"\bm_nview\b",
                 })
        {
            Assert.DoesNotMatch(new Regex(forbidden), leaves);
        }

        // The only transforms it turns are the two pivots: closed once in Awake, then both from the one angle, each by
        // its own leaf's sign, about local z.
        Assert.Single(Regex.Matches(leaves, @"_left\s*!\s*\.\s*Hinge\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*Euler\s*\(\s*0f\s*,\s*0f\s*,\s*_left\s*\.\s*Sign\s*\*\s*angle\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"_right\s*!\s*\.\s*Hinge\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*Euler\s*\(\s*0f\s*,\s*0f\s*,\s*_right\s*\.\s*Sign\s*\*\s*angle\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\bleftHinge\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*identity\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\brightHinge\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*identity\s*;"));
        Assert.Equal(4, Regex.Matches(leaves, @"\.\s*(localRotation|rotation|localPosition|position|localScale|eulerAngles|localEulerAngles)\s*=(?!=)").Count);
        Assert.Matches(new Regex(@"new\s+Pivot\s*\(\s*leftHinge\s*,\s*leftBox\s*,\s*CellarGeometry\s*\.\s*HingeRotationSign\s*\(\s*CellarLeaf\s*\.\s*Left\s*\)\s*\)"), leaves);
        Assert.Matches(new Regex(@"new\s+Pivot\s*\(\s*rightHinge\s*,\s*rightBox\s*,\s*CellarGeometry\s*\.\s*HingeRotationSign\s*\(\s*CellarLeaf\s*\.\s*Right\s*\)\s*\)"), leaves);
        Assert.Matches(new Regex(@"Quaternion\s+turn\s*=\s*Quaternion\s*\.\s*Euler\s*\(\s*0f\s*,\s*0f\s*,\s*part\s*\.\s*Sign\s*\*\s*angleDegrees\s*\)\s*;"), leaves);
        // Not a trapdoor: nothing joins the lid's registry. Since slice 4 the covers have their own (CellarLadderTests).
        Assert.DoesNotMatch(new Regex(@"\bTrapdoorLid\s*\.\s*(Any|RouteShut|MayLandOn|Live)\b"), leaves);
    }

    [Fact]
    public void ThePairIsSteppedAndSettledThroughTheTestedModelWithOneSkinnedOverlapPerLeaf()
    {
        string leaves = Code(Source(Runtime));
        Assert.Single(Regex.Matches(leaves, @"_pair\s*=\s*new\s+CellarPairOccupancy\s*<\s*CellarLeaves\s*>\s*\(\s*this\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\b_pair\s*=(?!=)"));
        Assert.Single(Regex.Matches(leaves, @"HatchMotion\s*\.\s*Step\s*\(\s*_progress\s*,\s*open\s*,\s*Time\s*\.\s*deltaTime\s*,\s*SwingSeconds\s*,\s*_pair\s*\)"));
        Assert.Single(Regex.Matches(leaves, @"_progress\s*=\s*CellarSettle\s*\.\s*First\s*\(\s*open\s*,\s*_pair\s*,\s*_movers\s*\)\s*;"));
        Assert.DoesNotMatch(new Regex(@"HatchMotion\s*\.\s*Settle\s*\("), leaves);
        Assert.Single(Regex.Matches(leaves, @"_movers\s*=\s*new\s+CellarPairOccupancy\s*<\s*Movers\s*>\s*\(\s*new\s+Movers\s*\(\s*this\s*\)\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\b_movers\s*=(?!=)"));
        // The movers' probe is the same query counting only characters and dynamic bodies; the full one is the lid's rule.
        Assert.Matches(new Regex(@"public\s+bool\s+Occupied\s*\(\s*CellarLeaf\s+leaf\s*,\s*float\s+angleDegrees\s*\)\s*=>\s*_leaves\s*\.\s*Query\s*\(\s*leaf\s*,\s*angleDegrees\s*,\s*true\s*\)\s*;"), leaves);
        Assert.Matches(new Regex(@"public\s+bool\s+Occupied\s*\(\s*CellarLeaf\s+leaf\s*,\s*float\s+angleDegrees\s*\)\s*\{\s*return\s+Query\s*\(\s*leaf\s*,\s*angleDegrees\s*,\s*false\s*\)\s*;\s*\}"), leaves);
        Assert.Equal(2, Regex.Matches(leaves, @"\bQuery\s*\(\s*leaf\s*,").Count);
        Assert.Matches(new Regex(@"occupied\s*=\s*moversOnly\s*\?\s*Moves\s*\(\s*Overlaps\s*\[\s*index\s*\]\s*\)\s*:\s*Blocks\s*\(\s*Overlaps\s*\[\s*index\s*\]\s*\)\s*;"), leaves);
        Assert.Matches(new Regex(@"private\s+bool\s+Moves\s*\(\s*Collider\s+collider\s*\)\s*\{\s*if\s*\(\s*collider\s*==\s*null\s*\|\|\s*collider\s*\.\s*isTrigger\s*\|\|\s*IsOwn\s*\(\s*collider\s*\)\s*\)\s*\{\s*return\s+false\s*;\s*\}\s*if\s*\(\s*\(\s*_moverMask\s*&\s*\(\s*1\s*<<\s*collider\s*\.\s*gameObject\s*\.\s*layer\s*\)\s*\)\s*!=\s*0\s*\)\s*\{\s*return\s+true\s*;\s*\}\s*Rigidbody\s+body\s*=\s*collider\s*\.\s*attachedRigidbody\s*;\s*return\s+body\s*!=\s*null\s*&&\s*!\s*body\s*\.\s*isKinematic\s*;\s*\}"), leaves);
        Assert.Matches(new Regex(@"MoverLayers\s*=\s*\{\s*""character""\s*,\s*""character_net""\s*,\s*""character_ghost""\s*,\s*""character_noenv""\s*,\s*""hitbox""\s*,\s*""item""\s*,\s*""vehicle""\s*\}"), leaves);
        Assert.Matches(new Regex(@"!\s*ResolveMoverLayers\s*\(\s*\)"), leaves);
        // The pose composition: the pivot's own position in the slope transform, turned about its local z, and the
        // box measured in the pivot's space at the closed pose.
        Assert.Matches(new Regex(@"Vector3\s+centre\s*=\s*part\s*\.\s*Space\s*\.\s*TransformPoint\s*\(\s*part\s*\.\s*Local\s*\+\s*turn\s*\*\s*part\s*\.\s*BoxCentre\s*\)\s*;"), leaves);
        Assert.Matches(new Regex(@"Quaternion\s+rotation\s*=\s*part\s*\.\s*Space\s*\.\s*rotation\s*\*\s*turn\s*\*\s*part\s*\.\s*BoxRotation\s*;"), leaves);
        Assert.Matches(new Regex(@"Space\s*=\s*hinge\s*\.\s*parent\s*;\s*Local\s*=\s*hinge\s*\.\s*localPosition\s*;"), leaves);
        Assert.Matches(new Regex(@"BoxCentre\s*=\s*hinge\s*\.\s*InverseTransformPoint\s*\(\s*box\s*\.\s*transform\s*\.\s*TransformPoint\s*\(\s*box\s*\.\s*center\s*\)\s*\)\s*;"), leaves);
        Assert.Matches(new Regex(@"BoxRotation\s*=\s*Quaternion\s*\.\s*Inverse\s*\(\s*hinge\s*\.\s*rotation\s*\)\s*\*\s*box\s*\.\s*transform\s*\.\s*rotation\s*;"), leaves);
        // Measured with the pivots closed: the identity rotations come before the pivots are measured.
        Assert.True(Index(leaves, @"rightHinge\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*identity") < Index(leaves, @"_left\s*=\s*new\s+Pivot\s*\("));
        Assert.Equal(2, Regex.Matches(leaves, @"\b_progress\s*=(?!=)").Count);
        Assert.DoesNotMatch(new Regex(@"HatchMotion\s*\.\s*Step\s*\([^;]*\bthis\s*\)"), leaves);

        // One bounded, non-allocating box overlap per asked leaf, triggers ignored, a full buffer occupied, with the
        // lid's layers and blocking rule.
        Assert.Single(Regex.Matches(leaves, @"Physics\s*\.\s*OverlapBoxNonAlloc\s*\(\s*centre\s*,\s*part\s*\.\s*HalfExtents\s*,\s*Overlaps\s*,\s*rotation\s*,\s*TrapdoorLid\s*\.\s*QueryMask\s*,\s*QueryTriggerInteraction\s*\.\s*Ignore\s*\)"));
        Assert.DoesNotMatch(new Regex(@"Physics\s*\.\s*(OverlapBox|OverlapSphere|OverlapCapsule|BoxCastAll|RaycastAll|CheckBox)\s*\("), leaves);
        Assert.Matches(new Regex(@"bool\s+occupied\s*=\s*found\s*>=\s*Overlaps\s*\.\s*Length\s*;"), leaves);
        Assert.Matches(new Regex(@"static\s+readonly\s+Collider\s*\[\s*\]\s+Overlaps\s*=\s*new\s+Collider\s*\[\s*32\s*\]\s*;"), leaves);
        Assert.Matches(new Regex(@"return\s+TrapdoorLid\s*\.\s*BlocksByKind\s*\(\s*collider\s*\)\s*;"), leaves);
        Assert.Matches(new Regex(@"!\s*TrapdoorLid\s*\.\s*ResolveLayers\s*\(\s*\)"), leaves);
        // The skin, on every side, exactly as the lid's.
        Assert.Matches(new Regex(@"float\s+skin\s*=\s*HatchMotion\s*\.\s*QuerySkinMetres\s*;"), leaves);
        foreach (string axis in new[] { "x", "y", "z" })
        {
            Assert.Matches(new Regex(@"Mathf\s*\.\s*Max\s*\(\s*0\.005f\s*,\s*half\s*\.\s*" + axis + @"\s*-\s*skin\s*\)"), leaves);
        }

        Assert.Single(Regex.Matches(leaves, @"HalfExtents\s*=(?!=)"));
        // The pair asks the left leaf, then the right only if the left is clear (CellarPairOccupancy), so at most two
        // overlaps a checked pose.
        Assert.Matches(new Regex(@"Pivot\s+part\s*=\s*leaf\s*==\s*CellarLeaf\s*\.\s*Left\s*\?\s*_left\s*!\s*:\s*_right\s*!\s*;"), leaves);
    }

    [Fact]
    public void EachLeafIgnoresThePiecesOwnCollidersByIdentityAndNothingElse()
    {
        string leaves = Code(Source(Runtime));
        // Resolved once, from the blueprint's list, all or nothing.
        Assert.Matches(new Regex(@"var\s+own\s*=\s*new\s+Collider\s*\[\s*CellarBlueprint\s*\.\s*QueryIgnores\s*\.\s*Count\s*\]\s*;"), leaves);
        Assert.Matches(new Regex(@"transform\s*\.\s*Find\s*\(\s*CellarBlueprint\s*\.\s*QueryIgnores\s*\[\s*index\s*\]\s*\)"), leaves);
        Assert.Matches(new Regex(@"if\s*\(\s*collider\s*==\s*null\s*\)\s*\{\s*return\s+null\s*;\s*\}"), leaves);
        Assert.Matches(new Regex(@"\|\|\s*own\s*==\s*null\s*\|\|"), leaves);
        // Compared by identity, before the blocking rule, in both the full and the movers' query.
        Assert.Equal(2, Regex.Matches(leaves, @"if\s*\(\s*collider\s*==\s*null\s*\|\|\s*collider\s*\.\s*isTrigger\s*\|\|\s*IsOwn\s*\(\s*collider\s*\)\s*\)\s*\{\s*return\s+false\s*;\s*\}").Count);
        Assert.Equal(2, Regex.Matches(leaves, @"\bIsOwn\s*\(\s*collider\s*\)").Count);
        Assert.Matches(new Regex(@"if\s*\(\s*own\s*\[\s*index\s*\]\s*==\s*collider\s*\)\s*\{\s*return\s+true\s*;\s*\}"), leaves);
        // Never by hierarchy, name, tag or layer: the lid's IsChildOf test would hide anything parented under the piece.
        Assert.DoesNotMatch(new Regex(@"\bIsChildOf\b|GetComponentInParent|\.\s*name\b|\.\s*tag\b|CompareTag|\.\s*layer\b|\broot\b"), Between(leaves, @"private\s+bool\s+IsOwn\s*\(", null));
        // Slice 4 adds one hierarchy lookup, the ladder's landing question (MayLandOn), which tells a leaf from the frame
        // for the landing search and is never part of an occupancy query. Everywhere else in the type, none.
        // Exactly the method's own body is excluded: anything added after it is still under the ban.
        Match header = Regex.Match(leaves, @"internal\s+static\s+bool\s+MayLandOn\s*\(\s*Collider\s+collider\s*\)\s*\{");
        Assert.True(header.Success, "MayLandOn");
        int depth = 1, end = header.Index + header.Length;
        while (end < leaves.Length && depth > 0) { depth += leaves[end] == '{' ? 1 : leaves[end] == '}' ? -1 : 0; end++; }
        Assert.Equal(0, depth);
        string landing = leaves.Substring(header.Index, end - header.Index);
        Assert.DoesNotMatch(new Regex(@"\bIsChildOf\b|GetComponentInParent|CompareTag"), leaves.Remove(header.Index, end - header.Index));
        Assert.Matches(new Regex(@"GetComponentInParent\s*<\s*CellarLeaves\s*>"), landing);
        Assert.Single(Regex.Matches(leaves, @"\bMayLandOn\s*\("));
        foreach (string query in new[] { @"public\s+bool\s+Occupied\s*\(", @"private\s+bool\s+Query\s*\(", @"private\s+bool\s+Blocks\s*\(", @"private\s+bool\s+Moves\s*\(" })
        {
            Assert.DoesNotMatch(new Regex(@"\bMayLandOn\b|\bIsChildOf\b|GetComponentInParent"), Between(leaves, query, @"\n\s*\}\s*\n"));
        }
        Assert.Single(Regex.Matches(leaves, @"\b_own\s*=(?!=)"));
    }

    [Fact]
    public void TheLeavesPerFramePathDoesNotAllocate()
    {
        string leaves = Code(Source(Runtime));
        string update = Between(leaves, @"private\s+void\s+Update\s*\(\s*\)", @"\bcatch\s*\(");
        string rest = Between(leaves, @"private\s+void\s+Apply\s*\(\s*\)", null);
        foreach (string body in new[] { update, rest })
        {
            foreach (string allocating in new[]
                     {
                         @"\bnew\s+(?!Vector3\b|Quaternion\b)", @"\bGetComponent\w*\s*[<(]", @"\bFind\s*\(", @"\.\s*Select\s*\(", @"\.\s*Where\s*\(",
                         @"\.\s*ToArray\s*\(", @"\.\s*ToList\s*\(", @"""", @"\bstring\s*\.", @"=>\s*\w+\s*\(", @"\bLog\b", @"\bforeach\b",
                     })
            {
                Assert.DoesNotMatch(new Regex(allocating), body);
            }
        }
    }

    [Fact]
    public void EveryCellarCloneCarriesTheLeavesWithItsDonorsSwingBeforeItIsVerified()
    {
        string adapter = Code(Source(Adapter));
        Assert.Single(Regex.Matches(adapter, @"AddComponent\s*<\s*CellarLeaves\s*>"));
        int add = Index(adapter, @"prefab\s*\.\s*AddComponent\s*<\s*CellarLeaves\s*>\s*\(\s*\)\s*\.\s*SwingSeconds\s*=\s*family\s*\.\s*SwingSeconds\s*;");
        int verify = Index(adapter, @"\bVerify\s*\(\s*prefab\s*,\s*wear\s*\)\s*;");
        int register = Index(adapter, @"PieceManager\s*\.\s*Instance\s*\.\s*AddPiece\s*\(");
        Assert.True(add < verify && verify < register, "The leaves must be on the clone before it is verified and registered.");
        // Verify refuses a clone without exactly one runtime on its root, with an accepted swing, or without every
        // collider the queries ignore, or with any other collider.
        Assert.Matches(new Regex(@"CellarLeaves\s*\[\s*\]\s+runtimes\s*=\s*prefab\s*\.\s*GetComponentsInChildren\s*<\s*CellarLeaves\s*>\s*\(\s*true\s*\)\s*;"), adapter);
        Assert.Matches(new Regex(@"runtimes\s*\.\s*Length\s*!=\s*1\s*\|\|\s*runtimes\s*\[\s*0\s*\]\s*\.\s*gameObject\s*!=\s*prefab"), adapter);
        Assert.Matches(new Regex(@"prefab\s*\.\s*GetComponentsInChildren\s*<\s*Collider\s*>\s*\(\s*true\s*\)\s*\.\s*Length\s*!=\s*CellarBlueprint\s*\.\s*QueryIgnores\s*\.\s*Count"), adapter);
        Assert.Matches(new Regex(@"foreach\s*\(\s*string\s+path\s+in\s+CellarBlueprint\s*\.\s*QueryIgnores\s*\)"), adapter);
        Assert.Matches(new Regex(@"CellarLeaves\s*\.\s*Log\s*=\s*_log\s*;"), adapter);

        // Nothing else in the product adds the leaves. Since slice 4 the ladder adapters name them too, each consult
        // gated by CellarLeaves.Any (CellarLadderTests); no other file does.
        var ladders = new[] { "Runtime/LadderRuntime.cs", "Runtime/LadderClearance.cs", "Pieces/LadderSnapAlignment.cs" };
        foreach (string file in ProductFiles())
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            string text = Code(File.ReadAllText(file));
            if (relative != Adapter) { Assert.DoesNotMatch(new Regex(@"AddComponent\s*<\s*CellarLeaves\s*>"), text); }
            if (relative != Adapter && relative != Runtime && Array.IndexOf(ladders, relative) < 0)
            {
                Assert.DoesNotMatch(new Regex(@"\bCellarLeaves\b"), text);
            }
        }
    }

    // ---- The model of the runtime's query -----------------------------------------------------------------------------

    private static float Run(Obstacle obstacle, bool open, float from)
    {
        float progress = from;
        var pair = Pair(obstacle);
        for (int frame = 0; frame < 600; ++frame) { progress = HatchMotion.Step(progress, open, Frame, DoorFamilies.LightSwing, pair); }
        return progress;
    }

    private static CellarPairOccupancy<Obstacle> Pair(Obstacle obstacle) => new CellarPairOccupancy<Obstacle>(obstacle);

    /// <summary>The runtime's first pose for this obstacle: the full probe, and the movers' probe, which sees the
    /// obstacle only if it is a mover (a character, item, cart, ship or dynamic body), as the runtime's <c>Moves</c> does.</summary>
    private static float Settle(Obstacle obstacle, bool open) =>
        CellarSettle.First(open, Pair(obstacle), new CellarPairOccupancy<MoverView>(new MoverView(obstacle)));

    /// <summary>A wall standing on the rear grid edge, reaching <paramref name="reach"/> toward the door, full height.</summary>
    private static Obstacle RearWall(float reach) =>
        new Obstacle(new ClimbPoint(-1.2f, 0f, CellarGeometry.HalfModule - reach), new ClimbPoint(1.2f, 3f, 1.5f));

    /// <summary>An axis-aligned box in piece space, asked about the runtime's query box: the leaf's box at the angle,
    /// shrunk by <see cref="HatchMotion.QuerySkinMetres"/> on every side, sampled through its volume.</summary>
    private sealed class Obstacle : ICellarLeafOccupancy
    {
        private readonly ClimbPoint _min;
        private readonly ClimbPoint _max;

        internal Obstacle(ClimbPoint min, ClimbPoint max, bool mover = false)
        {
            _min = min;
            _max = max;
            Mover = mover;
        }

        /// <summary>A character or a dynamic body, rather than a fixed piece.</summary>
        internal bool Mover { get; }

        internal List<(CellarLeaf Leaf, float Angle, bool Occupied)> Asked { get; } = new List<(CellarLeaf, float, bool)>();

        public bool Occupied(CellarLeaf leaf, float angleDegrees)
        {
            bool occupied = Hits(leaf, angleDegrees);
            Asked.Add((leaf, angleDegrees, occupied));
            return occupied;
        }

        internal bool Hits(CellarLeaf leaf, float angleDegrees) => QuerySamples(leaf, angleDegrees).Any(p =>
            p.X > _min.X && p.X < _max.X && p.Y > _min.Y && p.Y < _max.Y && p.Z > _min.Z && p.Z < _max.Z);
    }

    private sealed class MoverView : ICellarLeafOccupancy
    {
        private readonly Obstacle _obstacle;

        internal MoverView(Obstacle obstacle) => _obstacle = obstacle;

        public bool Occupied(CellarLeaf leaf, float angleDegrees) => _obstacle.Mover && _obstacle.Hits(leaf, angleDegrees);
    }

    private sealed class Scripted : IHatchOccupancy
    {
        private readonly Func<float, bool> _rule;

        internal Scripted(Func<float, bool> rule) => _rule = rule;

        internal List<float> Asked { get; } = new List<float>();

        public bool Occupied(float angleDegrees)
        {
            Asked.Add(angleDegrees);
            return _rule(angleDegrees);
        }
    }

    /// <summary>Points through the skinned query box of a leaf at an angle, in piece space, by the slope model's own
    /// rotation (<see cref="CellarGeometry.SlopeCorner"/>'s, with the box shrunk by the skin).</summary>
    private static IEnumerable<ClimbPoint> QuerySamples(CellarLeaf leaf, float angleDegrees)
    {
        float skin = HatchMotion.QuerySkinMetres;
        double radians = angleDegrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        const int across = 9, through = 3, along = 17;
        for (int i = 0; i < across; ++i)
        {
            float du = -skin - (CellarGeometry.LeafWidth - 2f * skin) * i / (across - 1);
            for (int j = 0; j < through; ++j)
            {
                float dw = -skin - (CellarGeometry.LeafThickness - 2f * skin) * j / (through - 1);
                for (int k = 0; k < along; ++k)
                {
                    float v = CellarGeometry.LeafStartV + skin + (CellarGeometry.LeafLength - 2f * skin) * k / (along - 1);
                    float right = CellarGeometry.HingeU + du * cos + dw * sin;
                    float w = -du * sin + dw * cos;
                    yield return CellarGeometry.ToPiece(leaf == CellarLeaf.Right ? right : -right, w, v);
                }
            }
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------------

    /// <summary>The source without comments, so a pin is never satisfied or tripped by prose.</summary>
    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Between(string text, string startPattern, string? endPattern)
    {
        Match start = Regex.Match(text, startPattern);
        Assert.True(start.Success, "Missing: " + startPattern);
        if (endPattern == null) { return text.Substring(start.Index); }
        Match end = new Regex(endPattern).Match(text, start.Index);
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
