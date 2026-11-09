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
    [InlineData(0.275f)]
    [InlineData(0.4f)]
    public void AWallReachingIntoEveryPoseOnTheRearEdgeHoldsTheCoverShut(float reach)
    {
        // A stave or stake wall centred on the rear grid edge (section 2a's table): it reaches past every pose of the
        // leaves' rear end, so no pose is clear. The cover settles at its stored closed end and never opens.
        Obstacle wall = RearWall(reach);
        Assert.Equal(0f, HatchMotion.Settle(false, Pair(wall)));
        Assert.Equal(0f, Run(wall, open: true, from: 0f));
        Assert.DoesNotContain(wall.Asked, pose => !pose.Occupied);
    }

    [Theory]
    [InlineData(0.235f)]
    [InlineData(0.24f)]
    public void AWallReachingIntoTheClosedLeavesButNotTheOpenOnesLoadsAClosedCoverOpen(float reach)
    {
        // The roof-top 67 and ashwood cross walls (0.235-0.238 m): the closed leaves' rear edge reaches z 0.79, the open
        // leaves' only z 0.763, so the wall is in the closed pose but not the open one. HatchMotion's first-pose rule,
        // reused unchanged, then takes the clear open end (as it would for someone standing in the entrance), and the
        // leaves close only until they meet the wall: part open on this client while the native state says closed
        // (CELLAR_DOORS.md section 8, item 15, an owner question).
        Obstacle wall = RearWall(reach);
        float settled = HatchMotion.Settle(false, Pair(wall));
        Assert.Equal(1f, settled);
        float closing = Run(wall, open: false, from: settled);
        Assert.InRange(HatchMotion.Angle(closing), 30f, CellarGeometry.OpenAngleDegrees);
        Assert.False(HatchMotion.IsLanding(closing, false));
        Assert.All(wall.Asked.Where(pose => pose.Occupied), pose => Assert.True(pose.Angle < HatchMotion.Angle(closing)));
        // Asked to open from closed in session, it never moves.
        Assert.Equal(0f, Run(RearWall(reach), open: true, from: 0f));
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.2f)]
    public void AWallWithinTheHeaderOnTheRearEdgeLeavesTheCoverFreeToOpenAndClose(float reach)
    {
        Obstacle wall = RearWall(reach);
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
        var feet = new Obstacle(new ClimbPoint(0.25f, bottom, centre.Z - 0.15f), new ClimbPoint(0.55f, bottom + 1.8f, centre.Z + 0.15f));
        Assert.Equal(0f, HatchMotion.Settle(false, Pair(feet)));
        float held = Run(feet, open: true, from: 0f);
        Assert.InRange(HatchMotion.Angle(held), 0f, HatchMotion.MaximumAngleStepDegrees);
        Assert.False(HatchMotion.IsPassage(held, true));
        Assert.Contains(feet.Asked, pose => pose.Occupied);
    }

    // ---- The Unity adapter, by source ----------------------------------------------------------------------------------

    [Fact]
    public void TheLeavesReadTheNativeDoorStateAndWriteNothingToTheNetwork()
    {
        string leaves = Code(Source(Runtime));
        // Two reads of the one native value: the first pose (TrySettle) and each frame (Update), each after IsValid().
        Assert.Equal(2, Regex.Matches(leaves, @"\.\s*GetZDO\s*\(\s*\)\s*\.\s*GetInt\s*\(\s*ZDOVars\s*\.\s*s_state\s*\)").Count);
        Assert.Equal(2, Regex.Matches(leaves, @"view\s*==\s*null\s*\|\|\s*!\s*view\s*\.\s*IsValid\s*\(\s*\)").Count);
        Assert.Equal(2, Regex.Matches(leaves, @"\bGetZDO\b").Count);
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
        // Not a trapdoor: nothing joins the lid's registry, so the ladder code is unchanged by it.
        Assert.DoesNotMatch(new Regex(@"\bLive\b|\bTrapdoorLid\s*\.\s*(Any|RouteShut|MayLandOn)\b"), leaves);
    }

    [Fact]
    public void ThePairIsSteppedAndSettledThroughTheTestedModelWithOneSkinnedOverlapPerLeaf()
    {
        string leaves = Code(Source(Runtime));
        Assert.Single(Regex.Matches(leaves, @"_pair\s*=\s*new\s+CellarPairOccupancy\s*<\s*CellarLeaves\s*>\s*\(\s*this\s*\)\s*;"));
        Assert.Single(Regex.Matches(leaves, @"\b_pair\s*=(?!=)"));
        Assert.Single(Regex.Matches(leaves, @"HatchMotion\s*\.\s*Step\s*\(\s*_progress\s*,\s*open\s*,\s*Time\s*\.\s*deltaTime\s*,\s*SwingSeconds\s*,\s*_pair\s*\)"));
        Assert.Single(Regex.Matches(leaves, @"HatchMotion\s*\.\s*Settle\s*\(\s*open\s*,\s*_pair\s*\)"));
        Assert.Equal(2, Regex.Matches(leaves, @"\b_progress\s*=(?!=)").Count);
        Assert.DoesNotMatch(new Regex(@"HatchMotion\s*\.\s*(Step|Settle)\s*\([^;]*\bthis\s*\)"), leaves);

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
        // Compared by identity, before the blocking rule.
        Assert.Matches(new Regex(@"if\s*\(\s*collider\s*==\s*null\s*\|\|\s*collider\s*\.\s*isTrigger\s*\|\|\s*IsOwn\s*\(\s*collider\s*\)\s*\)\s*\{\s*return\s+false\s*;\s*\}"), leaves);
        Assert.Matches(new Regex(@"if\s*\(\s*own\s*\[\s*index\s*\]\s*==\s*collider\s*\)\s*\{\s*return\s+true\s*;\s*\}"), leaves);
        // Never by hierarchy, name, tag or layer: the lid's IsChildOf test would hide anything parented under the piece.
        Assert.DoesNotMatch(new Regex(@"\bIsChildOf\b|GetComponentInParent|\.\s*name\b|\.\s*tag\b|CompareTag|\.\s*layer\b|\broot\b"), Between(leaves, @"public\s+bool\s+Occupied\s*\(", null));
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

        // Nothing else in the product adds the leaves or names them in code: the ladder code does not know them yet.
        foreach (string file in ProductFiles())
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            string text = Code(File.ReadAllText(file));
            if (relative != Adapter) { Assert.DoesNotMatch(new Regex(@"AddComponent\s*<\s*CellarLeaves\s*>"), text); }
            if (relative != Adapter && relative != Runtime) { Assert.DoesNotMatch(new Regex(@"\bCellarLeaves\b"), text); }
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

    /// <summary>A wall standing on the rear grid edge, reaching <paramref name="reach"/> toward the door, full height.</summary>
    private static Obstacle RearWall(float reach) =>
        new Obstacle(new ClimbPoint(-1.2f, 0f, CellarGeometry.HalfModule - reach), new ClimbPoint(1.2f, 3f, 1.5f));

    /// <summary>An axis-aligned box in piece space, asked about the runtime's query box: the leaf's box at the angle,
    /// shrunk by <see cref="HatchMotion.QuerySkinMetres"/> on every side, sampled through its volume.</summary>
    private sealed class Obstacle : ICellarLeafOccupancy
    {
        private readonly ClimbPoint _min;
        private readonly ClimbPoint _max;

        internal Obstacle(ClimbPoint min, ClimbPoint max)
        {
            _min = min;
            _max = max;
        }

        internal List<(CellarLeaf Leaf, float Angle, bool Occupied)> Asked { get; } = new List<(CellarLeaf, float, bool)>();

        public bool Occupied(CellarLeaf leaf, float angleDegrees)
        {
            bool occupied = QuerySamples(leaf, angleDegrees).Any(p =>
                p.X > _min.X && p.X < _max.X && p.Y > _min.Y && p.Y < _max.Y && p.Z > _min.Z && p.Z < _max.Z);
            Asked.Add((leaf, angleDegrees, occupied));
            return occupied;
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
