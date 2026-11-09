using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 slice 2, the piece adapter's plan: the trapdoors are off by default, the boxes the adapter
/// builds are exactly the hatch model's, the lid turned on its hinge is the model's lid at every angle, and a donor
/// or a clone that differs from the pinned matrix is refused instead of guessed at.</summary>
public sealed class TrapdoorBlueprintTests
{
    [Fact]
    public void TrapdoorsAreOffByDefault()
    {
        Assert.False(TrapdoorSettings.EnabledByDefault);
        Assert.Equal("Trapdoors", TrapdoorSettings.Section);
        Assert.Equal("Enabled", TrapdoorSettings.Key);
        Assert.Contains("delete", TrapdoorSettings.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginRegistersTrapdoorsOnlyWhenTheSettingIsOn()
    {
        string plugin = Source("Plugin.cs");
        Assert.Matches(new Regex(@"Config\s*\.\s*Bind\s*\(\s*TrapdoorSettings\s*\.\s*Section\s*,\s*TrapdoorSettings\s*\.\s*Key\s*,\s*TrapdoorSettings\s*\.\s*EnabledByDefault\s*,"), plugin);
        // The one construction of the registration sits inside the setting's own branch.
        Assert.Single(Regex.Matches(plugin, @"new\s+TrapdoorPieceRegistration\s*\("));
        Assert.Matches(new Regex(@"if\s*\(\s*_trapdoorsEnabled\s*\.\s*Value\s*\)\s*\{\s*_trapdoors\s*=\s*new\s+TrapdoorPieceRegistration\s*\([^;]*\)\s*;\s*try\s*\{\s*_trapdoors\s*\.\s*Install\s*\(\s*\)\s*;\s*\}\s*catch"), plugin);
        Assert.Single(Regex.Matches(plugin, @"_trapdoors\s*\.\s*Install\s*\("));
        Assert.Matches(new Regex(@"_trapdoors\s*\?\s*\.\s*Dispose\s*\(\s*\)\s*;"), plugin);

        // Nothing else in the product registers a trapdoor or its localization.
        foreach (string file in Directory.EnumerateFiles(ProductRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) { continue; }
            string text = File.ReadAllText(file);
            if (relative != "Plugin.cs") { Assert.DoesNotMatch(new Regex(@"new\s+TrapdoorPieceRegistration\s*\("), text); }
            if (relative != "Pieces/TrapdoorPieceRegistration.cs") { Assert.DoesNotMatch(new Regex(@"TrapdoorLocalization\s*\.\s*Register\s*\("), text); }
        }
    }

    [Fact]
    public void TheAdapterChecksTheDonorAndReadsTheCloneBackBeforeAddingThePiece()
    {
        // Source order in Add(): the donor check and its refusal come before the clone is made; the clone's
        // requirements are read back and refused on a difference; the hierarchy is verified; only then is it added.
        string adapter = Source(Path.Combine("Pieces", "TrapdoorPieceRegistration.cs"));
        int donorCheck = Index(adapter, @"TrapdoorBlueprint\s*\.\s*DonorMismatches\s*\(\s*family\s*,\s*donor\s*==\s*null\s*\?\s*null\s*:\s*Read\s*\(\s*donor\s*\)\s*\)");
        int donorRefuse = Index(adapter, @"if\s*\(\s*problems\s*\.\s*Count\s*>\s*0\s*\)\s*\{\s*Refuse\s*\(");
        int clone = Index(adapter, @"new\s+CustomPiece\s*\(");
        int readBack = Index(adapter, @"TrapdoorBlueprint\s*\.\s*RequirementMismatches\s*\(\s*family\s*,\s*Requirements\s*\(\s*piece\s*\)\s*\)");
        int readBackRefuse = Index(adapter, @"if\s*\(\s*recipe\s*\.\s*Count\s*>\s*0\s*\)\s*throw");
        int verify = Index(adapter, @"\bVerify\s*\(\s*prefab\s*,\s*wear\s*\)\s*;");
        int add = Index(adapter, @"PieceManager\s*\.\s*Instance\s*\.\s*AddPiece\s*\(");
        Assert.True(donorCheck < donorRefuse && donorRefuse < clone && clone < readBack && readBack < readBackRefuse &&
                    readBackRefuse < verify && verify < add, "Add() lost its check order.");
        Assert.Single(Regex.Matches(adapter, @"new\s+CustomPiece\s*\("));
        Assert.Single(Regex.Matches(adapter, @"AddPiece\s*\("));
        Assert.Single(Regex.Matches(adapter, @"new\s+RequirementConfig\s*\(\s*family\s*\.\s*Costs\s*\[\s*index\s*\]\s*\.\s*Item\s*,\s*family\s*\.\s*Costs\s*\[\s*index\s*\]\s*\.\s*Amount\s*,\s*0\s*,\s*true\s*\)"));
    }

    [Fact]
    public void TheFrameBoxesAreTheModelsFrameAsVisualsAndAsColliders()
    {
        Assert.Equal(HatchGeometry.Frame.Count, TrapdoorBlueprint.FrameVisuals.Count);
        Assert.Equal(HatchGeometry.Frame.Count, TrapdoorBlueprint.FrameColliders.Count);
        for (int index = 0; index < HatchGeometry.Frame.Count; ++index)
        {
            HatchBox band = HatchGeometry.Frame[index];
            foreach (TrapdoorPart part in new[] { TrapdoorBlueprint.FrameVisuals[index], TrapdoorBlueprint.FrameColliders[index] })
            {
                AssertPoint(band.Centre, part.Centre);
                AssertPoint(band.Size, part.Size);
            }

            Assert.Equal(TrapdoorPartRole.FrameVisual, TrapdoorBlueprint.FrameVisuals[index].Role);
            Assert.Equal(TrapdoorPartRole.FrameCollider, TrapdoorBlueprint.FrameColliders[index].Role);
        }

        Assert.Equal(4, TrapdoorBlueprint.FrameColliders.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    [InlineData(22.5f)]
    [InlineData(45f)]
    [InlineData(67.5f)]
    [InlineData(90f)]
    public void TheLidTurnedOnItsHingeIsTheModelsLid(float angle)
    {
        // The runtime turns the hinge pivot by Quaternion.Euler(angle, 0, 0): y' = y cos - z sin, z' = y sin + z cos.
        LidPose model = HatchGeometry.Lid(angle);
        foreach (TrapdoorPart part in new[] { TrapdoorBlueprint.LidCollider, TrapdoorBlueprint.LidVisual })
        {
            ClimbPoint centre = Turn(part.Centre, angle);
            AssertPoint(model.Centre, centre);
            AssertPoint(model.Size, part.Size);
        }
    }

    [Fact]
    public void TheClosedLidColliderIsFlushAndInsideTheAperture()
    {
        TrapdoorPart lid = TrapdoorBlueprint.LidCollider;
        ClimbPoint hinge = TrapdoorBlueprint.HingePosition;
        float top = hinge.Y + lid.Centre.Y + lid.Size.Y / 2f;
        Assert.Equal(HatchGeometry.TopY, top, 5);
        float front = hinge.Z + lid.Centre.Z - lid.Size.Z / 2f;
        float rear = hinge.Z + lid.Centre.Z + lid.Size.Z / 2f;
        Assert.True(front > -HatchGeometry.HalfAperture && rear < HatchGeometry.HalfAperture);
        Assert.True(lid.Size.X / 2f < HatchGeometry.HalfAperture);
        // The lid's top rear edge is the hinge axis.
        Assert.Equal(hinge.Z, rear, 5);
        Assert.Equal(hinge.Y, top, 5);
    }

    [Fact]
    public void TheHingeStrapsMarkTheRearEdgeOnTheLidsTop()
    {
        Assert.Equal(2, TrapdoorBlueprint.HingeStraps.Count);
        Assert.Contains(TrapdoorBlueprint.HingeStraps, s => s.Centre.X < 0f);
        Assert.Contains(TrapdoorBlueprint.HingeStraps, s => s.Centre.X > 0f);
        foreach (TrapdoorPart strap in TrapdoorBlueprint.HingeStraps)
        {
            Assert.Equal(TrapdoorPartRole.HingeStrap, strap.Role);
            // On the lid's top face (hinge-space y 0 up to its thickness), from the hinge edge forward, inside the lid.
            Assert.Equal(0f, strap.Centre.Y - strap.Size.Y / 2f, 5);
            Assert.Equal(0f, strap.Centre.Z + strap.Size.Z / 2f, 5);
            Assert.True(strap.Size.Z < HatchGeometry.LidLength / 2f);
            Assert.True(Math.Abs(strap.Centre.X) + strap.Size.X / 2f < HatchGeometry.HalfLid);
        }
    }

    [Fact]
    public void EveryFamilyBorrowsNamedDonorMaterialsAndKeepsTheDonorsWearRootCount()
    {
        // Donors whose m_new, m_worn and m_broken are one object (census.json): iron_grate (New), hexagonal and
        // flametal (door). Every other donor has a separate worn root with its own material.
        var single = new HashSet<string>(StringComparer.Ordinal) { "iron_grate", "piece_hexagonal_door", "flametal_gate" };
        foreach (DoorFamily family in DoorFamilies.All)
        {
            TrapdoorMaterials materials = TrapdoorBlueprint.MaterialsFor(family);
            Assert.False(string.IsNullOrWhiteSpace(materials.Lid));
            Assert.False(string.IsNullOrWhiteSpace(materials.Frame));
            Assert.Equal(single.Contains(family.Donor), materials.FrameWorn == null);
        }
    }

    [Fact]
    public void TheLocalizationNamesEveryFamilyOnceInTitleCase()
    {
        Dictionary<string, string> english = TrapdoorBlueprint.English();
        Assert.Equal(DoorFamilies.All.Count + 2, english.Count);
        Assert.True(english.ContainsKey(TrapdoorBlueprint.DescriptionToken));
        // Slice 4: the climber's notice is registered with the trapdoors, so it exists exactly when they do.
        Assert.False(string.IsNullOrWhiteSpace(english[TrapdoorBlueprint.HatchClosedToken]));
        Assert.StartsWith("cl_msg_", TrapdoorBlueprint.HatchClosedToken, StringComparison.Ordinal);
        foreach (DoorFamily family in DoorFamilies.All)
        {
            string name = english[family.TrapdoorToken];
            Assert.EndsWith(" Trapdoor", name, StringComparison.Ordinal);
            Assert.All(name.Split(' '), word => Assert.True(char.IsUpper(word[0]), name));
            Assert.DoesNotContain("$", family.TrapdoorToken, StringComparison.Ordinal);
            Assert.StartsWith(DoorFamilies.TrapdoorPrefix, family.TrapdoorPrefab, StringComparison.Ordinal);
        }

        Assert.Equal(english.Count, english.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { "Building", "Doors" }, TrapdoorBlueprint.Usage);
    }

    [Fact]
    public void ThePinnedDonorPassesForEveryFamily()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            Assert.Empty(TrapdoorBlueprint.DonorMismatches(family, Pinned(family)));
        }
    }

    [Fact]
    public void AMissingDonorIsRefused() =>
        Assert.NotEmpty(TrapdoorBlueprint.DonorMismatches(DoorFamilies.All[0], null));

    private static readonly (string Expected, Action<DonorFacts> Change)[] DonorChangeTable =
    {
        ("no Piece", f => f.HasPiece = false),
        ("no WearNTear", f => f.HasWearNTear = false),
        ("no Door", f => f.HasDoor = false),
        ("no ZNetView", f => f.HasNetView = false),
        ("no Animator", f => f.HasRootAnimator = false),
        ("child", f => f.HasLeaf = false),
        ("station", f => f.Station = "forge_changed"),
        ("station", f => f.Station = null),
        ("health", f => f.Health += 50f),
        ("health", f => f.Health = float.NaN),
        ("material", f => f.Material += 1),
        ("burnable", f => f.Burnable = !f.Burnable),
        ("door name", f => f.DoorName = "$piece_other"),
        ("key", f => f.HasKeyItem = true),
        ("cannot be closed", f => f.CanNotBeClosed = true),
        ("guard stones", f => f.ChecksGuardStone = false),
        ("enables an object", f => f.HasOpenEnable = true),
        ("open effect", f => f.OpenEffect += ",sfx_extra"),
        ("close effect", f => f.CloseEffect = null),
        ("requirement count", f => f.Requirements = Array.Empty<ObservedRequirement>()),
    };

    public static IEnumerable<object[]> DonorChanges() =>
        Enumerable.Range(0, DonorChangeTable.Length).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(DonorChanges))]
    public void AnyDonorChangeRefusesEveryFamily(int change)
    {
        (string expected, Action<DonorFacts> apply) = DonorChangeTable[change];
        foreach (DoorFamily family in DoorFamilies.All)
        {
            DonorFacts facts = Pinned(family);
            apply(facts);
            IReadOnlyList<string> problems = TrapdoorBlueprint.DonorMismatches(family, facts);
            Assert.Contains(problems, p => p.Contains(expected, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AClonesMockedRequirementsMatchThePin()
    {
        foreach (DoorFamily family in DoorFamilies.All)
        {
            var mocked = family.Costs.Select(c => new ObservedRequirement(TrapdoorBlueprint.MockPrefix + c.Item, c.Amount, true)).ToArray();
            Assert.Empty(TrapdoorBlueprint.RequirementMismatches(family, mocked));
        }
    }

    [Fact]
    public void AnEmptyRequirementListNeverMatches()
    {
        // Jötunn keeps the donor's requirements when PieceConfig.Requirements is empty: emptiness is not a cost.
        foreach (DoorFamily family in DoorFamilies.All)
        {
            Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(family, Array.Empty<ObservedRequirement>()));
            Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(family, null));
        }
    }

    [Fact]
    public void EveryRequirementDifferenceIsNamed()
    {
        DoorFamily darkwood = DoorFamilies.ByDonor("darkwood_gate")!;
        ObservedRequirement[] Exact() => darkwood.Costs.Select(c => new ObservedRequirement(c.Item, c.Amount, true)).ToArray();

        ObservedRequirement[] amount = Exact();
        amount[1] = new ObservedRequirement("Iron", 5, true);
        Assert.Contains(TrapdoorBlueprint.RequirementMismatches(darkwood, amount), p => p.Contains("amount 5", StringComparison.Ordinal));

        ObservedRequirement[] item = Exact();
        item[2] = new ObservedRequirement("Resin", 2, true);
        Assert.Contains(TrapdoorBlueprint.RequirementMismatches(darkwood, item), p => p.Contains("item Resin", StringComparison.Ordinal));

        ObservedRequirement[] kept = Exact();
        kept[0] = new ObservedRequirement("Wood", 16, false);
        Assert.Contains(TrapdoorBlueprint.RequirementMismatches(darkwood, kept), p => p.Contains("not recovered", StringComparison.Ordinal));

        ObservedRequirement[] reordered = Exact().Reverse().ToArray();
        Assert.NotEmpty(TrapdoorBlueprint.RequirementMismatches(darkwood, reordered));

        ObservedRequirement[] extra = Exact().Concat(new[] { new ObservedRequirement("Wood", 1, true) }).ToArray();
        Assert.Contains(TrapdoorBlueprint.RequirementMismatches(darkwood, extra), p => p.Contains("count 4", StringComparison.Ordinal));

        ObservedRequirement[] missing = Exact();
        missing[0] = new ObservedRequirement(null, 16, true);
        Assert.Contains(TrapdoorBlueprint.RequirementMismatches(darkwood, missing), p => p.Contains("<none>", StringComparison.Ordinal));
    }

    [Fact]
    public void OnlyJotunnsMockPrefixIsRemovedFromAnItemName()
    {
        Assert.Equal("Wood", TrapdoorBlueprint.PrefabName("JVLmock_Wood"));
        Assert.Equal("Wood", TrapdoorBlueprint.PrefabName("Wood"));
        Assert.Equal("xJVLmock_Wood", TrapdoorBlueprint.PrefabName("xJVLmock_Wood"));
        Assert.Null(TrapdoorBlueprint.PrefabName(null));
    }

    /// <summary>The donor exactly as DOOR_DONOR_MATRIX.md pins it, as the adapter would read it from the game.</summary>
    private static DonorFacts Pinned(DoorFamily family) => new DonorFacts
    {
        HasPiece = true,
        HasWearNTear = true,
        HasDoor = true,
        HasNetView = true,
        HasRootAnimator = true,
        HasLeaf = true,
        Station = family.Station,
        Requirements = family.Costs.Select(c => new ObservedRequirement(c.Item, c.Amount, true)).ToArray(),
        Health = family.Health,
        Material = (int)family.Material,
        Burnable = family.Burnable,
        DoorName = family.DonorToken,
        HasKeyItem = false,
        CanNotBeClosed = false,
        ChecksGuardStone = true,
        HasOpenEnable = false,
        OpenEffect = family.OpenEffect,
        CloseEffect = family.CloseEffect,
    };

    private static ClimbPoint Turn(ClimbPoint local, float angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(radians), sin = (float)Math.Sin(radians);
        ClimbPoint hinge = TrapdoorBlueprint.HingePosition;
        return new ClimbPoint(hinge.X + local.X, hinge.Y + local.Y * cos - local.Z * sin, hinge.Z + local.Y * sin + local.Z * cos);
    }

    private static void AssertPoint(ClimbPoint expected, ClimbPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
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
