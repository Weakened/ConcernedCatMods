using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Railings;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Railings;

/// <summary>#556: railings in the ladders' three tiers at the game's three slopes. The angles are the game's stairs' own
/// (measured from their snap points), the railing's snaps land on the stair's side snaps, the handrail stays the same
/// height above the stair, and each tier is made of what the owner's references show: wood plain, copper twisted with a
/// knuckle, iron twisted with scrolls and finials. In game: OWNER GO-AROUND PENDING.</summary>
public sealed class RailingTests
{
    private const float Epsilon = 1e-4f;

    public static IEnumerable<object[]> Pieces() =>
        RailingFamilies.All.Select(piece => new object[] { (int)piece.Tier, (int)piece.Angle });

    [Theory]
    [InlineData((int)RailingAngle.Level, 0f, 0f)]
    [InlineData((int)RailingAngle.Stair, 1f, 26.565f)]
    [InlineData((int)RailingAngle.Steep, 2f, 45f)]
    public void TheSlopesAreTheGamesStairs(int angleValue, float rise, float degrees)
    {
        var angle = (RailingAngle)angleValue;
        Assert.Equal(rise, RailingGeometry.Rise(angle));
        Assert.Equal(degrees, RailingGeometry.Degrees(angle), 3);
        Assert.Equal(2f, RailingGeometry.Run);
    }

    [Theory]
    [InlineData((int)RailingAngle.Stair)]
    [InlineData((int)RailingAngle.Steep)]
    public void TheSnapsAreSpacedAsTheStairsSideSnaps(int angleValue)
    {
        var angle = (RailingAngle)angleValue;
        (Point3 bottom, Point3 top) = RailingGeometry.StairSideSnaps(angle);
        Point3 railing = RailingGeometry.TopSnap(angle) - RailingGeometry.BottomSnap;
        // The stair rises toward -z; a railing turned half round to run up it covers the same rise and run.
        Assert.Equal(top.Y - bottom.Y, railing.Y, 4);
        Assert.Equal(System.Math.Abs(top.Z - bottom.Z), railing.Z, 4);
        Assert.Equal(0f, railing.X, 4);
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void TheHandrailStaysTheSameHeightAboveTheStairLine(int tierValue, int angleValue)
    {
        var tier = (RailingTier)tierValue;
        var angle = (RailingAngle)angleValue;
        RailingPart rail = Assert.Single(RailingDesign.Parts(tier, angle), part => part.Kind == RailingPartKind.Handrail);
        foreach (Point3 end in new[] { rail.From, rail.To })
            Assert.Equal(RailingGeometry.HandrailHeight, end.Y - RailingGeometry.BaseAt(angle, end.Z) + rail.Width * 0.5f, 4);
        Assert.Equal(RailingGeometry.Slope(angle), (rail.To.Y - rail.From.Y) / (rail.To.Z - rail.From.Z), 4);
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void BalustersAreVerticalEvenlySpacedAndBetweenTheRails(int tierValue, int angleValue)
    {
        var tier = (RailingTier)tierValue;
        var angle = (RailingAngle)angleValue;
        List<RailingPart> parts = RailingDesign.Parts(tier, angle).ToList();
        List<RailingPart> bars = parts.Where(part => part.Kind == RailingPartKind.Baluster).OrderBy(part => part.From.Z).ToList();
        Assert.True(bars.Count >= 8, "a 2 m railing has a row of balusters");
        float pitch = bars[1].From.Z - bars[0].From.Z;
        Assert.True(pitch <= RailingDesign.BalusterPitch + Epsilon);
        for (int i = 0; i < bars.Count; i++)
        {
            Assert.Equal(bars[i].From.Z, bars[i].To.Z, 5);
            Assert.True(bars[i].To.Y > bars[i].From.Y);
            if (i > 0) Assert.Equal(pitch, bars[i].From.Z - bars[i - 1].From.Z, 4);
            float above = bars[i].To.Y - RailingGeometry.BaseAt(angle, bars[i].To.Z);
            Assert.True(above <= RailingGeometry.HandrailHeight - RailingDesign.HandrailWidth(tier) + Epsilon);
        }

        // Within the posts.
        float inset = RailingDesign.PostWidth(tier) * 0.5f;
        Assert.True(bars[0].From.Z > inset && bars[bars.Count - 1].From.Z < RailingGeometry.Run - inset);
    }

    [Fact]
    public void EachTierIsMadeOfWhatTheReferencesShow()
    {
        foreach (RailingAngle angle in new[] { RailingAngle.Level, RailingAngle.Stair, RailingAngle.Steep })
        {
            IReadOnlyList<RailingPart> wood = RailingDesign.Parts(RailingTier.Wood, angle);
            IReadOnlyList<RailingPart> copper = RailingDesign.Parts(RailingTier.Copper, angle);
            IReadOnlyList<RailingPart> iron = RailingDesign.Parts(RailingTier.Iron, angle);

            // Wood: simple. Square posts with caps, plain balusters, nothing twisted or decorated.
            Assert.Equal(2, wood.Count(part => part.Kind == RailingPartKind.Post));
            Assert.Equal(2, wood.Count(part => part.Kind == RailingPartKind.PostCap));
            Assert.All(wood, part => Assert.Equal(0f, part.TwistDegrees));
            Assert.DoesNotContain(wood, part => part.Kind is RailingPartKind.Knuckle or RailingPartKind.Scroll or RailingPartKind.Finial);

            // Copper: slightly decorated. Every baluster twisted, a knuckle on each, capped posts, a bottom rail.
            List<RailingPart> copperBars = copper.Where(part => part.Kind == RailingPartKind.Baluster).ToList();
            Assert.All(copperBars, bar => Assert.Equal(RailingDesign.Twist(RailingTier.Copper), bar.TwistDegrees));
            Assert.Equal(copperBars.Count, copper.Count(part => part.Kind == RailingPartKind.Knuckle));
            Assert.Equal(2, copper.Count(part => part.Kind == RailingPartKind.PostCap));
            Assert.Single(copper, part => part.Kind == RailingPartKind.BottomRail);
            Assert.DoesNotContain(copper, part => part.Kind is RailingPartKind.Scroll or RailingPartKind.Finial);

            // Iron: the most detailed. Tighter twists, scroll pairs in place of every fourth bar, finials on the posts.
            List<RailingPart> ironBars = iron.Where(part => part.Kind == RailingPartKind.Baluster).ToList();
            int plain = ironBars.Count(bar => bar.TwistDegrees == 0f);
            Assert.True(plain >= 1, "the scroll pairs hang on straight bars");
            Assert.All(ironBars.Where(bar => bar.TwistDegrees != 0f), bar => Assert.Equal(RailingDesign.Twist(RailingTier.Iron), bar.TwistDegrees));
            Assert.Equal(plain * 4 * 10, iron.Count(part => part.Kind == RailingPartKind.Scroll));
            Assert.Equal(2, iron.Count(part => part.Kind == RailingPartKind.Finial));
            Assert.Single(iron, part => part.Kind == RailingPartKind.BottomRail);
            Assert.True(RailingDesign.Twist(RailingTier.Iron) > RailingDesign.Twist(RailingTier.Copper));
            Assert.True(iron.Count > copper.Count && copper.Count > wood.Count, "more detail, tier by tier");
        }
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void EveryPartStaysInsideTheRailingsSpace(int tierValue, int angleValue)
    {
        var tier = (RailingTier)tierValue;
        var angle = (RailingAngle)angleValue;
        float post = RailingDesign.PostWidth(tier);
        float reach = post * 0.75f;
        foreach (RailingPart part in RailingDesign.Parts(tier, angle))
        {
            foreach (Point3 p in new[] { part.From, part.To })
            {
                Assert.InRange(p.X, -reach, reach);
                Assert.InRange(p.Z, -reach, RailingGeometry.Run + reach);
                float above = p.Y - RailingGeometry.BaseAt(angle, Math.Clamp(p.Z, 0f, RailingGeometry.Run));
                Assert.InRange(above, -Epsilon, RailingGeometry.PostHeight + post * 1.8f);
            }
        }
    }

    [Fact]
    public void IronScrollsStayInTheGapBesideTheirBar()
    {
        IReadOnlyList<RailingPart> iron = RailingDesign.Parts(RailingTier.Iron, RailingAngle.Stair);
        List<RailingPart> bars = iron.Where(part => part.Kind == RailingPartKind.Baluster).OrderBy(part => part.From.Z).ToList();
        float pitch = bars[1].From.Z - bars[0].From.Z;
        foreach (RailingPart scroll in iron.Where(part => part.Kind == RailingPartKind.Scroll))
        {
            RailingPart host = bars.Where(bar => bar.TwistDegrees == 0f).OrderBy(bar => Math.Abs(bar.From.Z - scroll.From.Z)).First();
            foreach (Point3 p in new[] { scroll.From, scroll.To })
            {
                Assert.True(Math.Abs(p.Z - host.From.Z) < pitch - RailingDesign.BalusterWidth(RailingTier.Iron), "a scroll reaches its neighbour");
                Assert.InRange(p.Y, host.From.Y, host.To.Y);
            }
        }

        // A scroll starts on its bar.
        var centre = new Point3(0f, 1f, 0.1f);
        Point3 start = RailingDesign.Spiral(centre, 0.1f, 1, 1, 0f);
        Assert.Equal(0f, start.Z, 4);
    }

    [Theory]
    [MemberData(nameof(Pieces))]
    public void TheMeshIsFlatShadedFacesOutAndIsBounded(int tierValue, int angleValue)
    {
        var tier = (RailingTier)tierValue;
        var angle = (RailingAngle)angleValue;
        CellarMesh mesh = RailingMesh.Build("railing", RailingDesign.Parts(tier, angle));
        Assert.Equal(mesh.Vertices.Count, mesh.Normals.Count);
        Assert.Equal(mesh.Vertices.Count, mesh.Uvs.Count);
        Assert.Equal(0, mesh.Triangles.Count % 3);
        Assert.InRange(mesh.Vertices.Count, 100, 65000); // within a 16-bit index buffer
        for (int t = 0; t < mesh.Triangles.Count; t += 3)
        {
            ClimbPoint a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
            ClimbPoint n = CellarBlueprint.TriangleNormal(a, b, c);
            ClimbPoint stored = mesh.Normals[mesh.Triangles[t]];
            Assert.True(n.X * stored.X + n.Y * stored.Y + n.Z * stored.Z > 0f, "a triangle wound against its face's outward normal");
        }

        float post = RailingDesign.PostWidth(tier);
        Assert.All(mesh.Vertices, v =>
        {
            Assert.InRange(v.X, -post, post);
            Assert.InRange(v.Z, -post, RailingGeometry.Run + post);
            // The tallest thing is iron's finial: 0.9 of a post's width above the post, then 1.8 times its 0.7-post radius.
            Assert.InRange(v.Y, -0.05f, RailingGeometry.Rise(angle) + RailingGeometry.PostHeight + post * 2.2f);
        });
    }

    [Fact]
    public void ATwistedBarIsBuiltFromEnoughRingsToReadAsATwist()
    {
        Assert.Equal(1, RailingMesh.Segments(0f));
        Assert.Equal(12, RailingMesh.Segments(540f));
        Assert.Equal(24, RailingMesh.Segments(1080f));
        Assert.Equal(RailingMesh.MinimumTwistSegments, RailingMesh.Segments(10f));
        CellarMesh plain = RailingMesh.Build("bar", new[] { new RailingPart(RailingPartKind.Baluster, new Point3(0f, 0f, 0f), new Point3(0f, 1f, 0f), 0.03f) });
        CellarMesh twisted = RailingMesh.Build("bar", new[] { new RailingPart(RailingPartKind.Baluster, new Point3(0f, 0f, 0f), new Point3(0f, 1f, 0f), 0.03f, 540f) });
        Assert.Equal(6 * 4, plain.Vertices.Count);
        Assert.Equal((12 * 4 + 2) * 4, twisted.Vertices.Count);
    }

    [Theory]
    [InlineData((int)RailingAngle.Level)]
    [InlineData((int)RailingAngle.Stair)]
    [InlineData((int)RailingAngle.Steep)]
    public void TheColliderCoversTheRailingAlongItsWholeSlope(int angleValue)
    {
        var angle = (RailingAngle)angleValue;
        (Point3 centre, Point3 size) = RailingGeometry.Collider(angle);
        float cos = RailingGeometry.Run / RailingGeometry.Length(angle);
        Assert.Equal(RailingGeometry.Length(angle), size.Z, 4);
        Assert.Equal(RailingGeometry.Length(angle) * 0.5f, centre.Z, 4);
        // Square to the slope it reaches from just above the stair to the handrail's height.
        Assert.Equal(RailingGeometry.HandrailHeight * cos, centre.Y * 2f - 0.05f * cos, 3);
        Assert.Equal(RailingGeometry.ColliderThickness, size.X);
    }

    [Fact]
    public void TheNineRailingsHaveStableIdsNamesAndTheLaddersMaterials()
    {
        Assert.Equal(9, RailingFamilies.All.Count);
        Assert.Equal(9, RailingFamilies.All.Select(piece => piece.Prefab).Distinct().Count());
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_wood_level" && piece.English == "Wood Railing");
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_copper_stair" && piece.English == "Copper Stair Railing");
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_iron_steep" && piece.English == "Iron Steep Stair Railing");
        Dictionary<string, string> english = RailingFamilies.English();
        Assert.All(RailingFamilies.All, piece => Assert.Equal(piece.English, english[piece.Token]));
        Assert.True(english.ContainsKey(RailingFamilies.DescriptionToken));
        // The ladders' own donors and materials, read from their registration, so a railing reads as its tier's ladder.
        string ladders = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "LadderPieceRegistration.cs"));
        foreach (RailingPiece piece in RailingFamilies.All)
        {
            Assert.Matches(new Regex(@"Add\(""tcc_ladder_" + piece.Tier.ToString().ToLowerInvariant() + @""",\s*""" + piece.Donor + @""",\s*""" + piece.MaterialPrefab + @""",\s*""" + piece.Material + @""",\s*" + piece.Health.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + @"f"), ladders);
            Assert.NotEmpty(piece.Costs);
        }
    }

    [Fact]
    public void RailingsExistOnlyBehindTheirOwnSwitch()
    {
        string plugin = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Plugin.cs"));
        Assert.Matches(new Regex(@"Config\.Bind\s*\(\s*RailingSettings\.Section\s*,\s*RailingSettings\.Key\s*,\s*RailingSettings\.EnabledByDefault\s*,"), plugin);
        Assert.Single(Regex.Matches(plugin, @"new\s+RailingPieceRegistration\s*\("));
        Assert.Matches(new Regex(@"if\s*\(\s*_railingsEnabled\.Value\s*\)\s*\{\s*_railings\s*=\s*new\s+RailingPieceRegistration\s*\("), plugin);
        Assert.False(RailingSettings.EnabledByDefault);
        // Code only: the class's own documentation names what it does not do.
        string registration = Regex.Replace(File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "RailingPieceRegistration.cs")),
            @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);
        Assert.DoesNotMatch(new Regex(@"\bZDO\b|GetZDO|ZRoutedRpc|InvokeRPC|ClaimOwnership|SetOwner|\bInstantiate\s*\("), registration);
    }

    private static string RepoRoot
    {
        get
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln"))) { directory = directory.Parent; }
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
