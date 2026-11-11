using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.ConcernedLadders.Domain.Families;
using TheConcernedCat.ConcernedLadders.Domain.Railings;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Railings;

/// <summary>#556, #565: railings in eight material families at the game's stair shapes. The slopes are the game's stairs'
/// own (measured from their snap points), the railing's snaps land on the stair's, the handrail stays the
/// same height above the stair, and each family's style is what the owner's references show: plain wood, copper twisted
/// with a knuckle, iron (and flametal) twisted with scrolls and finials. In game: OWNER GO-AROUND PENDING.</summary>
public sealed class RailingTests
{
    private const float Epsilon = 1e-4f;

    private static readonly RailingAngle[] Railed =
        { RailingAngle.Level, RailingAngle.Short, RailingAngle.Stair, RailingAngle.Steep };

    public static IEnumerable<object[]> StylesAndShapes()
    {
        foreach (RailingStyle style in Enum.GetValues(typeof(RailingStyle)))
            foreach (RailingAngle angle in Railed) yield return new object[] { (int)style, (int)angle };
    }

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
        Assert.Equal(1f, RailingGeometry.RunOf(RailingAngle.Short));
        Assert.Equal(0f, RailingGeometry.Rise(RailingAngle.Short));
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
        Assert.Equal(Math.Abs(top.Z - bottom.Z), railing.Z, 4);
        Assert.Equal(0f, railing.X, 4);
    }

    [Theory]
    [MemberData(nameof(StylesAndShapes))]
    public void TheHandrailStaysTheSameHeightAboveTheStairLine(int styleValue, int angleValue)
    {
        var style = (RailingStyle)styleValue;
        var angle = (RailingAngle)angleValue;
        List<RailingPart> rails = RailingDesign.Parts(style, angle).Where(part => part.Kind == RailingPartKind.Handrail).ToList();
        Assert.Single(rails);
        foreach (RailingPart rail in rails)
            foreach (Point3 end in new[] { rail.From, rail.To })
                Assert.Equal(RailingGeometry.HandrailHeight, end.Y - BaseHeight(angle, end) + rail.Width * 0.5f, 4);
    }

    [Theory]
    [MemberData(nameof(StylesAndShapes))]
    public void BalustersAreVerticalEvenlySpacedAndUnderTheHandrail(int styleValue, int angleValue)
    {
        var style = (RailingStyle)styleValue;
        var angle = (RailingAngle)angleValue;
        List<RailingPart> bars = RailingDesign.Parts(style, angle).Where(part => part.Kind == RailingPartKind.Baluster).OrderBy(part => Along(angle, part.From)).ToList();
        Assert.True(bars.Count >= (angle == RailingAngle.Short ? 4 : 8), "a railing has a row of balusters");
        float pitch = Along(angle, bars[1].From) - Along(angle, bars[0].From);
        Assert.True(pitch <= RailingDesign.BalusterPitch + Epsilon);
        for (int i = 0; i < bars.Count; i++)
        {
            Assert.Equal(bars[i].From.X, bars[i].To.X, 5);
            Assert.Equal(bars[i].From.Z, bars[i].To.Z, 5);
            Assert.True(bars[i].To.Y > bars[i].From.Y);
            if (i > 0) Assert.True(Math.Abs(pitch - (Along(angle, bars[i].From) - Along(angle, bars[i - 1].From))) < 1e-3f, "uneven spacing");
            Assert.True(bars[i].To.Y - BaseHeight(angle, bars[i].To) <= RailingGeometry.HandrailHeight - RailingDesign.HandrailWidth(style) + Epsilon);
        }
    }

    [Fact]
    public void EachStyleIsMadeOfWhatTheReferencesShow()
    {
        foreach (RailingAngle angle in Railed)
        {
            IReadOnlyList<RailingPart> plain = RailingDesign.Parts(RailingStyle.Plain, angle);
            IReadOnlyList<RailingPart> decorated = RailingDesign.Parts(RailingStyle.Decorated, angle);
            IReadOnlyList<RailingPart> detailed = RailingDesign.Parts(RailingStyle.Detailed, angle);

            Assert.Equal(2, plain.Count(part => part.Kind == RailingPartKind.Post));
            Assert.Equal(2, plain.Count(part => part.Kind == RailingPartKind.PostCap));
            Assert.All(plain, part => Assert.Equal(0f, part.TwistDegrees));
            Assert.DoesNotContain(plain, part => part.Kind is RailingPartKind.Knuckle or RailingPartKind.Scroll or RailingPartKind.Finial);

            List<RailingPart> decoratedBars = decorated.Where(part => part.Kind == RailingPartKind.Baluster).ToList();
            Assert.All(decoratedBars, bar => Assert.Equal(RailingDesign.Twist(RailingStyle.Decorated), bar.TwistDegrees));
            Assert.Equal(decoratedBars.Count, decorated.Count(part => part.Kind == RailingPartKind.Knuckle));
            Assert.Equal(2, decorated.Count(part => part.Kind == RailingPartKind.PostCap));
            Assert.DoesNotContain(decorated, part => part.Kind is RailingPartKind.Scroll or RailingPartKind.Finial);

            Assert.Equal(2, detailed.Count(part => part.Kind == RailingPartKind.Finial));
            List<RailingPart> detailedBars = detailed.Where(part => part.Kind == RailingPartKind.Baluster).ToList();
            int straightBars = detailedBars.Count(bar => bar.TwistDegrees == 0f);
            Assert.True(straightBars >= 1, "the scroll pairs hang on straight bars");
            Assert.Equal(straightBars * 4 * 10, detailed.Count(part => part.Kind == RailingPartKind.Scroll));

            Assert.True(RailingDesign.Twist(RailingStyle.Detailed) > RailingDesign.Twist(RailingStyle.Decorated));
            Assert.True(detailed.Count > plain.Count && decorated.Count > plain.Count, "more detail than the plain style");
        }
    }

    [Theory]
    [MemberData(nameof(StylesAndShapes))]
    public void EveryPartStaysOnTheRailingsLine(int styleValue, int angleValue)
    {
        var style = (RailingStyle)styleValue;
        var angle = (RailingAngle)angleValue;
        float post = RailingDesign.PostWidth(style);
        foreach (RailingPart part in RailingDesign.Parts(style, angle))
        {
            foreach (Point3 p in new[] { part.From, part.To })
            {
                Assert.True(OffLine(angle, p) <= post * 0.75f + Epsilon, part.Kind + " strays off the railing's line");
                float above = p.Y - BaseHeight(angle, p);
                Assert.InRange(above, -Epsilon, RailingGeometry.PostHeight + post * 1.8f);
            }
        }
    }

    [Fact]
    public void ANewelPostIsOneCappedOrFinialledPost()
    {
        foreach (RailingStyle style in Enum.GetValues(typeof(RailingStyle)))
        {
            IReadOnlyList<RailingPart> parts = RailingDesign.Parts(style, RailingAngle.Newel);
            RailingPart post = Assert.Single(parts, part => part.Kind == RailingPartKind.Post);
            Assert.Equal(RailingGeometry.NewelHeight, post.To.Y - post.From.Y, 4);
            Assert.Equal(2, parts.Count);
            Assert.Contains(parts, part => part.Kind == (style == RailingStyle.Detailed ? RailingPartKind.Finial : RailingPartKind.PostCap));
        }
    }

    [Fact]
    public void IronScrollsStayInTheGapBesideTheirBar()
    {
        IReadOnlyList<RailingPart> iron = RailingDesign.Parts(RailingStyle.Detailed, RailingAngle.Stair);
        List<RailingPart> bars = iron.Where(part => part.Kind == RailingPartKind.Baluster).OrderBy(part => part.From.Z).ToList();
        float pitch = bars[1].From.Z - bars[0].From.Z;
        foreach (RailingPart scroll in iron.Where(part => part.Kind == RailingPartKind.Scroll))
        {
            RailingPart host = bars.Where(bar => bar.TwistDegrees == 0f).OrderBy(bar => Math.Abs(bar.From.Z - scroll.From.Z)).First();
            foreach (Point3 p in new[] { scroll.From, scroll.To })
            {
                Assert.True(Math.Abs(p.Z - host.From.Z) < pitch - RailingDesign.BalusterWidth(RailingStyle.Detailed), "a scroll reaches its neighbour");
                Assert.InRange(p.Y, host.From.Y, host.To.Y);
            }
        }

        Assert.Equal(0f, RailingDesign.Spiral(new Point3(0f, 1f, 0.1f), 0.1f, 1, 1, 0f).Z, 4);
    }

    public static IEnumerable<object[]> AllPieces() => RailingFamilies.All.Select(piece => new object[] { piece.Prefab });

    [Theory]
    [MemberData(nameof(AllPieces))]
    public void EveryPiecesMeshIsFlatShadedFacesOutStaysInItsRegionAndFitsOneBuffer(string prefab)
    {
        RailingPiece piece = RailingFamilies.All.Single(candidate => candidate.Prefab == prefab);
        CellarMesh mesh = RailingMesh.Build(prefab, RailingDesign.Parts(piece.Style, piece.Angle), piece.Family.Region);
        Assert.Equal(mesh.Vertices.Count, mesh.Normals.Count);
        Assert.Equal(mesh.Vertices.Count, mesh.Uvs.Count);
        Assert.InRange(mesh.Vertices.Count, 24, 65000); // something, within a 16-bit index buffer
        for (int t = 0; t < mesh.Triangles.Count; t += 3)
        {
            ClimbPoint a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
            ClimbPoint n = CellarBlueprint.TriangleNormal(a, b, c);
            ClimbPoint stored = mesh.Normals[mesh.Triangles[t]];
            Assert.True(n.X * stored.X + n.Y * stored.Y + n.Z * stored.Z > 0f, "a triangle wound against its face's outward normal");
        }

        UvRegion region = piece.Family.Region;
        if (!region.Tiles)
        {
            Assert.All(mesh.Uvs, uv =>
            {
                Assert.InRange(uv.U, region.U0 - 1e-4f, region.U1 + 1e-4f);
                Assert.InRange(uv.V, region.V0 - 1e-4f, region.V1 + 1e-4f);
            });
        }
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
    [InlineData((int)RailingAngle.Short)]
    [InlineData((int)RailingAngle.Stair)]
    [InlineData((int)RailingAngle.Steep)]
    public void AStraightRailingsColliderCoversItsWholeSlope(int angleValue)
    {
        var angle = (RailingAngle)angleValue;
        RailingCollider collider = Assert.Single(RailingGeometry.Colliders(angle));
        float cos = RailingGeometry.RunOf(angle) / RailingGeometry.Length(angle);
        Assert.Equal(RailingGeometry.Length(angle), collider.Size.Z, 4);
        Assert.Equal(RailingGeometry.Length(angle) * 0.5f, collider.Centre.Z, 4);
        Assert.Equal(RailingGeometry.HandrailHeight * cos, collider.Centre.Y * 2f - 0.05f * cos, 3);
        Assert.Equal(-RailingGeometry.Degrees(angle), collider.Pitch, 3);
        Assert.Equal(0f, collider.Yaw, 3);
        Assert.Equal(RailingGeometry.ColliderThickness, collider.Size.X);
    }

    [Fact]
    public void EveryFamilyHasEveryShapeWithStableIdsAndTheFirstNineKeepTheirs()
    {
        Assert.Equal(BuildFamilies.All.Count * RailingFamilies.Shapes.Length, RailingFamilies.All.Count);
        Assert.Equal(40, RailingFamilies.All.Count);
        // #565: the spiral railings are gone; the short railing joins.
        Assert.DoesNotContain(RailingFamilies.All, piece => piece.Prefab.Contains("spiral", StringComparison.Ordinal));
        Assert.Equal(RailingFamilies.All.Count, RailingFamilies.All.Select(piece => piece.Prefab).Distinct().Count());
        // #561's nine, unchanged.
        foreach (string family in new[] { "wood", "copper", "iron" })
            foreach (string shape in new[] { "level", "stair", "steep" })
                Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_" + family + "_" + shape);
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_wood_level" && piece.English == "Wood Railing");
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_darkwood_short" && piece.English == "Darkwood Short Railing");
        Assert.Contains(RailingFamilies.All, piece => piece.Prefab == "tcc_railing_flametal_newel" && piece.English == "Flametal Newel Post");
        Dictionary<string, string> english = RailingFamilies.English();
        Assert.All(RailingFamilies.All, piece => Assert.Equal(piece.English, english[piece.Token]));
        // The vanilla families cost what their donor pole costs; the ladders' three keep a fixed recipe.
        Assert.All(RailingFamilies.All, piece => Assert.Equal(piece.Family.FollowsDonor, piece.Costs == null));
    }

    [Fact]
    public void TheFamiliesAreTheLaddersThreeAndTheVanillaPolesWithTheirOwnRegions()
    {
        string ladders = File.ReadAllText(Path.Combine(RepoRoot, "src", "ConcernedLadders", "Pieces", "LadderPieceRegistration.cs"));
        foreach (BuildFamilyInfo family in BuildFamilies.All.Where(info => !info.FollowsDonor))
        {
            Assert.Matches(new Regex(@"Add\(""tcc_ladder_" + family.Word + @""",\s*""" + family.Donor + @""",\s*""" + family.MaterialPrefab + @""",\s*""" + family.Material + @""",\s*" +
                family.Health!.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + @"f"), ladders);
        }

        Assert.Equal(new[] { "darkwood_pole", "ashwood_pole_2m", "Piece_flametal_beam", "piece_dvergr_pole", "wood_pole_log" },
            BuildFamilies.All.Where(info => info.FollowsDonor).Select(info => info.Donor));
        // Every texture but the plank sheet and copper's plain colour is an atlas: those families sample a region.
        Assert.All(BuildFamilies.All, info => Assert.Equal(info.Family is BuildFamily.Wood or BuildFamily.Copper, info.Region.Tiles));
        Assert.Equal(RailingStyle.Detailed, BuildFamilies.Of(BuildFamily.Flametal).Style);
        Assert.Equal(RailingStyle.Plain, BuildFamilies.Of(BuildFamily.Log).Style);
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

    /// <summary>The base line's height under a point of a railing.</summary>
    private static float BaseHeight(RailingAngle angle, Point3 p) =>
        RailingGeometry.BaseAt(angle, Math.Clamp(p.Z, 0f, RailingGeometry.RunOf(angle)));

    /// <summary>How far along the ground a point is.</summary>
    private static float Along(RailingAngle angle, Point3 p) => p.Z;

    /// <summary>How far a point is, in plan, from the railing's line.</summary>
    private static float OffLine(RailingAngle angle, Point3 p)
    {
        float run = RailingGeometry.RunOf(angle);
        float beyond = p.Z < 0f ? -p.Z : p.Z > run ? p.Z - run : 0f;
        return Math.Max(Math.Abs(p.X), beyond);
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
