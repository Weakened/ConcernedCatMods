using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Families;

namespace TheConcernedCat.ConcernedLadders.Tests.Railings;

/// <summary>#558: ladders in the vanilla building families, behind their own off-by-default switch. Each is the game's own
/// pole of its material (recipe, station and health kept), samples its texture region, and is optional: one failing never
/// stops the three shipped ladders, and climbing depends on those three alone. A text audit of the adapter (the
/// registration needs the game); in game, OWNER GO-AROUND PENDING.</summary>
public sealed class LadderFamilyTests
{
    [Fact]
    public void FamilyLaddersExistOnlyBehindTheirOwnSwitch()
    {
        Assert.True(LadderFamilySettings.EnabledByDefault); // Owner 2026-11-12: finished and on by default for Concerned Builder 1.0.0.
        string plugin = Code("Plugin.cs");
        Assert.Matches(new Regex(@"bool\s+families\s*=\s*Config\.Bind\s*\(\s*LadderFamilySettings\.Section\s*,\s*LadderFamilySettings\.Key\s*,\s*LadderFamilySettings\.EnabledByDefault\s*,"), plugin);
        Assert.Matches(new Regex(@"new\s+LadderPieceRegistration\s*\([^;]*families\s*:\s*families\s*\)"), plugin);
        string registration = Code("Pieces/LadderPieceRegistration.cs");
        Assert.Matches(new Regex(@"if\s*\(\s*_families\s*&&\s*!_failed\s*\)"), registration);
        Assert.Single(Regex.Matches(registration, @"\bAddFamily\s*\(\s*family\s*\)\s*;"));
    }

    [Fact]
    public void ClimbingDependsOnTheShippedThreeAloneAndAFamilyFailureIsOnlySkipped()
    {
        string registration = Code("Pieces/LadderPieceRegistration.cs");
        Assert.Matches(new Regex(@"internal\s+bool\s+IsReady\s*=>\s*!_failed\s*&&\s*Array\.TrueForAll\s*\(\s*Shipped\s*,\s*_registered\.Contains\s*\)\s*;"), registration);
        Assert.Matches(new Regex(@"Shipped\s*=\s*\{\s*""tcc_ladder_wood""\s*,\s*""tcc_ladder_copper""\s*,\s*""tcc_ladder_iron""\s*\}"), registration);
        // The family path never sets the session's failure flag: its own catch logs and skips.
        Assert.Matches(new Regex(@"catch\s*\(\s*Exception\s+exception\s*\)\s*when\s*\(\s*!essential\s*\)\s*\{[^{}]*_log\s*\("), registration);
        string family = Body(registration, @"private\s+void\s+AddFamily\s*\(");
        Assert.DoesNotMatch(new Regex(@"_failed\s*="), family);
        Assert.Matches(new Regex(@"essential\s*:\s*false"), family);
        // The vanilla families keep the donor's health (null) and read the donor's recipe and station.
        Assert.Matches(new Regex(@"Add\s*\(\s*id\s*,\s*family\.Donor\s*,\s*family\.MaterialPrefab\s*,\s*family\.Material\s*,\s*null\s*,"), family);
        Assert.Matches(new Regex(@"donor\.m_resources"), family);
        Assert.Matches(new Regex(@"donor\.m_craftingStation"), family);
    }

    [Fact]
    public void EveryFamilyLadderHasANameAndSamplesItsRegion()
    {
        string localization = File.ReadAllText(Path.Combine(ProductRoot, "Pieces", "LadderLocalization.cs"));
        foreach (BuildFamilyInfo family in BuildFamilies.All.Where(info => info.FollowsDonor))
        {
            Assert.Contains("[\"cl_ladder_" + family.Word + "\"] = \"Vertical " + family.Name + " Ladder\"", localization, StringComparison.Ordinal);
            Assert.False(family.Region.Tiles, family.Name + " is an atlas");
        }

        string registration = Code("Pieces/LadderPieceRegistration.cs");
        // The visuals receive the region; boxes sample it unless the texture tiles (the shipped wood and copper, unchanged).
        Assert.Equal(3, Regex.Matches(registration, @"Visual\s*\(\s*prefab\.transform\s*,\s*""(new|worn|broken)""[^;]*,\s*region\s*,\s*layer\s*\)").Count);
        Assert.Matches(new Regex(@"if\s*\(\s*region\.Tiles\s*\)\s*\{\s*Bar\s*\("), registration);
        Assert.Matches(new Regex(@"DoorPieceKit\.VisualBox\s*\([^;]*region\s*,\s*layer\s*\)"), registration);
        // The iron ladder samples its pole's iron band, as the iron railing does, not the whole wood-and-iron sheet.
        Assert.Matches(new Regex(@"LadderStyle\.Iron\s*,\s*new\s+Color\s*\([^)]*\)\s*,\s*BuildFamilies\.Of\s*\(\s*BuildFamily\.Iron\s*\)\.Region\s*\)"), registration);
    }

    private static string Code(string relative) =>
        Regex.Replace(File.ReadAllText(Path.Combine(ProductRoot, relative)), @"/\*.*?\*/|//[^\n]*", " ", RegexOptions.Singleline);

    private static string Body(string text, string signature)
    {
        Match head = Regex.Match(text, signature);
        Assert.True(head.Success, "Signature not found: " + signature);
        int open = text.IndexOf('{', head.Index + head.Length);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("Unbalanced body after " + signature);
    }

    private static string ProductRoot
    {
        get
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln"))) { directory = directory.Parent; }
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src", "ConcernedLadders");
        }
    }
}
