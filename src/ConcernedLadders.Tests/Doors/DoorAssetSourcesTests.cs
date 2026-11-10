using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-004 #485 and CL-005 #486, the editable-source record: the door pieces are built from code, so
/// <c>docs/mods/concerned-ladders/DOOR_ASSET_SOURCES.md</c> is the place a person reads object names, pivots, rest
/// transforms, collider shapes and the motion curve. These tests hold that record to the code: a renamed part, a moved
/// pivot, a changed curve, swing time or material makes them fail until the record says the same.</summary>
public sealed class DoorAssetSourcesTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void EveryObjectTheAdaptersBuildIsNamedInTheRecord()
    {
        string doc = Doc();
        var names = new List<string>
        {
            TrapdoorBlueprint.LeafChild,
            TrapdoorBlueprint.NewRoot,
            TrapdoorBlueprint.WornRoot,
            TrapdoorBlueprint.Hinge,
            HatchGeometry.CornerSnapToken,
            CellarBlueprint.SlopeRoot,
        };
        names.AddRange(TrapdoorBlueprint.FrameVisuals.Select(part => TrapdoorBlueprint.NewRoot + "/" + part.Name));
        names.AddRange(TrapdoorBlueprint.FrameColliders.Select(part => part.Name));
        names.AddRange(TrapdoorBlueprint.HingeStraps.Select(part => TrapdoorBlueprint.Hinge + "/" + part.Name));
        names.Add(TrapdoorBlueprint.Hinge + "/" + TrapdoorBlueprint.LidCollider.Name);
        names.Add(TrapdoorBlueprint.Hinge + "/" + TrapdoorBlueprint.LidVisual.Name);
        names.AddRange(CellarBlueprint.FrameMeshes.Select(mesh => mesh.Name));
        names.AddRange(CellarBlueprint.QueryIgnores);
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            string hinge = CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(leaf);
            names.Add(hinge);
            names.Add(hinge + "/" + CellarBlueprint.LeafVisual(leaf).Name);
            names.AddRange(CellarBlueprint.HingeMarks(leaf).Select(mark => hinge + "/" + mark.Name));
        }

        foreach (string name in names.Distinct())
        {
            Assert.True(doc.Contains("`" + name + "`", StringComparison.Ordinal), "DOOR_ASSET_SOURCES.md does not name `" + name + "`");
        }
    }

    [Fact]
    public void TheTrapdoorTransformsInTheRecordAreTheBlueprints()
    {
        string doc = Doc();
        Row(doc, TrapdoorBlueprint.Hinge, Point(TrapdoorBlueprint.HingePosition), "`Quaternion.Euler(angle, 0, 0)`", "0° closed to " + Number(HatchGeometry.OpenAngleDegrees) + "° open");
        TrapdoorPart lid = TrapdoorBlueprint.LidCollider;
        Assert.Equal(lid.Centre, TrapdoorBlueprint.LidVisual.Centre);
        Row(doc, TrapdoorBlueprint.Hinge + "/" + lid.Name, Point(lid.Centre), Size(lid.Size));
        Row(doc, TrapdoorBlueprint.Hinge + "/" + TrapdoorBlueprint.LidVisual.Name, Point(lid.Centre), Size(lid.Size));
        TrapdoorPart left = TrapdoorBlueprint.HingeStraps[0];
        TrapdoorPart right = TrapdoorBlueprint.HingeStraps[1];
        Row(doc, TrapdoorBlueprint.Hinge + "/" + left.Name, "left " + Point(left.Centre), "right " + Point(right.Centre), Size(left.Size));
        foreach (ClimbPoint corner in HatchGeometry.CornerSnaps)
        {
            Assert.Equal(HatchGeometry.HalfModule, Math.Abs(corner.X));
            Assert.Equal(HatchGeometry.HalfModule, Math.Abs(corner.Z));
            Assert.Equal(0f, corner.Y);
        }

        for (int index = 0; index < HatchGeometry.Frame.Count; ++index)
        {
            HatchBox band = HatchGeometry.Frame[index];
            Row(doc, TrapdoorBlueprint.FrameColliders[index].Name,
                "| " + Range(band.Min.X, band.Max.X) + " | " + Range(band.Min.Y, band.Max.Y) + " | " + Range(band.Min.Z, band.Max.Z) + " |");
        }
    }

    [Fact]
    public void TheCellarTransformsInTheRecordAreTheBlueprints()
    {
        string doc = Doc();
        Row(doc, CellarBlueprint.SlopeRoot, Point(CellarBlueprint.SlopePosition),
            "`Quaternion.Euler(" + Number(CellarBlueprint.SlopeEulerX, "0.000") + ", 0, 0)`");
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            string hinge = CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(leaf);
            string sign = CellarGeometry.HingeRotationSign(leaf) < 0f ? "-" : string.Empty;
            Row(doc, hinge, Point(CellarBlueprint.HingePosition(leaf)), "`Quaternion.Euler(0, 0, " + sign + "angle)`");
        }

        CellarPart leftLeaf = CellarBlueprint.LeafCollider(CellarLeaf.Left);
        CellarPart rightLeaf = CellarBlueprint.LeafCollider(CellarLeaf.Right);
        Assert.Equal(leftLeaf.Centre, CellarBlueprint.LeafVisual(CellarLeaf.Left).Centre);
        Assert.Equal(rightLeaf.Centre, CellarBlueprint.LeafVisual(CellarLeaf.Right).Centre);
        string collider = CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(CellarLeaf.Left) + "/" + leftLeaf.Name;
        Row(doc, collider, "left " + Point(leftLeaf.Centre), "right " + Point(rightLeaf.Centre), Size(leftLeaf.Size));
        string visual = CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(CellarLeaf.Left) + "/" + CellarBlueprint.LeafVisual(CellarLeaf.Left).Name;
        Row(doc, visual, "left " + Point(leftLeaf.Centre), "right " + Point(rightLeaf.Centre), Size(leftLeaf.Size));

        CellarPart leftMark = CellarBlueprint.HingeMarks(CellarLeaf.Left)[0];
        CellarPart leftRear = CellarBlueprint.HingeMarks(CellarLeaf.Left)[1];
        CellarPart rightMark = CellarBlueprint.HingeMarks(CellarLeaf.Right)[0];
        Row(doc, CellarBlueprint.SlopeRoot + "/" + CellarBlueprint.Hinge(CellarLeaf.Left) + "/" + leftMark.Name,
            "x " + Number(leftMark.Centre.X) + " on the left leaf, " + Number(rightMark.Centre.X) + " on the right",
            "y " + Number(leftMark.Centre.Y),
            "z " + Number(leftMark.Centre.Z) + " front, " + Number(leftRear.Centre.Z) + " rear",
            Size(leftMark.Size));

        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            string line = Line(doc, prism.Name);
            Assert.Contains("| " + Range(prism.MinX, prism.MaxX) + " |", line, StringComparison.Ordinal);
            if (prism.Name == "cheek_right")
            {
                Assert.Equal(Profile(CellarGeometry.Frame[0]), Profile(prism));
                continue;
            }

            Assert.Contains("| " + Profile(prism) + " |", line, StringComparison.Ordinal);
        }

        Assert.Contains("one UV unit is " + Number(CellarBlueprint.UvMetres) + " m", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCurveSamplesInTheRecordAreTheRuntimesCurve()
    {
        string doc = Doc();
        Assert.Equal(90f, HatchGeometry.OpenAngleDegrees);
        Assert.Equal(HatchGeometry.OpenAngleDegrees, CellarGeometry.OpenAngleDegrees);
        Assert.Contains("p * p * (3 - 2 * p)", doc, StringComparison.Ordinal);
        Assert.Contains("capped at " + Number(HatchMotion.MaximumStepSeconds) + " s", doc, StringComparison.Ordinal);
        Assert.Contains("more than " + Number(HatchMotion.MaximumAngleStepDegrees) + "°", doc, StringComparison.Ordinal);
        Assert.Contains("shrunk by " + Number(HatchMotion.QuerySkinMetres) + " m", doc, StringComparison.Ordinal);
        for (int tenth = 0; tenth <= 10; ++tenth)
        {
            float progress = tenth / 10f;
            Match row = Regex.Match(doc, @"^\| " + Regex.Escape(Number(progress, "0.0")) + @" \| ([0-9]+\.[0-9]{2}) \|\s*$", RegexOptions.Multiline);
            Assert.True(row.Success, "No curve sample for p = " + Number(progress, "0.0"));
            float recorded = float.Parse(row.Groups[1].Value, Invariant);
            Assert.True(Math.Abs(recorded - HatchMotion.Angle(progress)) < 0.006f,
                "p = " + progress + ": recorded " + recorded + ", runtime " + HatchMotion.Angle(progress));
        }
    }

    [Fact]
    public void TheFamilyTableIsEveryFamilysSwingPrefabsAndMaterials()
    {
        string doc = Doc();
        Assert.Equal(DoorFamilies.All.Count, CellarDoors.All.Count);
        // Doc to code as well: the table has exactly one row per family, so a stale extra row fails too.
        int rows = doc.Split('\n').Count(line => line.StartsWith("| `", StringComparison.Ordinal) && line.Contains(" s | `" + DoorFamilies.TrapdoorPrefix, StringComparison.Ordinal));
        Assert.Equal(DoorFamilies.All.Count, rows);
        foreach (DoorFamily family in DoorFamilies.All)
        {
            CellarDoorFamily cellar = CellarDoors.All.Single(candidate => candidate.Door.Donor == family.Donor);
            TrapdoorMaterials materials = TrapdoorBlueprint.MaterialsFor(family);
            string row = "| `" + family.Donor + "` | " + Number(family.SwingSeconds, "0.0") + " s | `" + family.TrapdoorPrefab + "` | `" +
                cellar.Prefab + "` | `" + materials.Lid + "` | `" + materials.Frame + "` | " +
                (materials.FrameWorn == null ? "none (one root)" : "`" + materials.FrameWorn + "`") + " |";
            Assert.True(doc.Contains(row, StringComparison.Ordinal), "DOOR_ASSET_SOURCES.md lacks the row " + row);
        }
    }

    /// <summary>Asserts that the table row whose first cell names <paramref name="name"/> contains every fragment.</summary>
    private static void Row(string doc, string name, params string[] fragments)
    {
        string line = Line(doc, name);
        foreach (string fragment in fragments)
        {
            Assert.True(line.Contains(fragment, StringComparison.Ordinal), "The row for `" + name + "` lacks " + fragment + ": " + line);
        }
    }

    /// <summary>The table row whose first cell is exactly <paramref name="name"/>, or failing that the first row whose
    /// first cell starts with it (a cell that lists several names, the left one first).</summary>
    private static string Line(string doc, string name)
    {
        string[] lines = doc.Split('\n');
        string? line = lines.FirstOrDefault(candidate => candidate.StartsWith("| `" + name + "` |", StringComparison.Ordinal))
            ?? lines.FirstOrDefault(candidate => candidate.StartsWith("| `" + name + "`", StringComparison.Ordinal));
        Assert.True(line != null, "No table row starts with `" + name + "`");
        return line!;
    }

    private static string Profile(CellarPrism prism) =>
        string.Join(", ", prism.Profile.Select(point => "(" + Exact(point.Z) + ", " + Exact(point.Y) + ")"));

    private static string Range(float min, float max) => Exact(min) + " to " + Exact(max);

    private static string Point(ClimbPoint point) => "(" + Number(point.X) + ", " + Number(point.Y) + ", " + Number(point.Z) + ")";

    private static string Size(ClimbPoint size) => Number(size.X) + " x " + Number(size.Y) + " x " + Number(size.Z);

    /// <summary>Transforms and sizes are recorded to 4 decimals (0.1 mm).</summary>
    private static string Number(float value, string format = "0.####") => value.ToString(format, Invariant);

    /// <summary>Extents and profiles are recorded to 6 decimals, as the floor measurements are.</summary>
    private static string Exact(float value) => value.ToString("0.######", Invariant);

    /// <summary>The record, with the typographic minus read as a hyphen so numbers compare as the code prints them.</summary>
    private static string Doc() =>
        File.ReadAllText(Path.Combine(RepoRoot, "docs", "mods", "concerned-ladders", "DOOR_ASSET_SOURCES.md"))
            .Replace('−', '-')
            .Replace("\r\n", "\n");

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
