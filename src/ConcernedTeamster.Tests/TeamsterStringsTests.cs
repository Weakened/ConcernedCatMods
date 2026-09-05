using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace ConcernedTeamster.Tests;

/// <summary>CT-032: the localization catalog must be complete and stable,
/// fall back to English (and warn once) on a missing key, validate
/// translation placeholders, and round-trip its template. These prove the
/// framework; the translator doc and progressive presenter migration ride on
/// top of it.</summary>
public class TeamsterStringsTests
{
    [Fact]
    public void Catalog_EveryKeyHasNonEmptyEnglish()
    {
        Assert.NotEmpty(TeamsterStrings.Defaults);
        Assert.All(TeamsterStrings.Defaults, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
        });
    }

    [Fact]
    public void Get_KnownKey_ReturnsEnglishAndIsKnown()
    {
        string value = TeamsterStrings.Get("routes.pick", out bool known);
        Assert.True(known);
        Assert.Equal("Pick a route to profile.", value);
    }

    [Fact]
    public void Get_UnknownKey_FallsBackToKeyTextAndReportsOnce()
    {
        string value = TeamsterStrings.Get("does.not.exist", out bool known);
        Assert.False(known);
        Assert.Equal("does.not.exist", value); // key text as last-resort fallback

        // Once-only warning semantics: first report true, then false.
        Assert.True(TeamsterStrings.ShouldReportMissing("ct032.test.missonce"));
        Assert.False(TeamsterStrings.ShouldReportMissing("ct032.test.missonce"));
    }

    [Fact]
    public void Format_AppliesPlaceholders_InvariantCulture()
    {
        Assert.Equal("Selected: Ore road", TeamsterStrings.Format("routes.selected", "Ore road"));
        Assert.Equal("Route report: Mine run", TeamsterStrings.Format("report.title", "Mine run"));
    }

    [Fact]
    public void Template_RoundTripsThroughParseOverrides()
    {
        // Every catalog line in the template parses back to a valid override
        // (identity translation), and none is skipped.
        List<string> template = TeamsterStrings.TranslatorTemplate().ToList();
        Assert.Equal(TeamsterStrings.TemplateHeader, template[0]);

        Dictionary<string, string> parsed = TeamsterStrings.ParseOverrides(template, out int skipped);
        Assert.Equal(0, skipped);
        Assert.Equal(TeamsterStrings.Defaults.Count, parsed.Count);
        foreach (KeyValuePair<string, string> entry in TeamsterStrings.Defaults)
        {
            Assert.Equal(entry.Value, parsed[entry.Key]);
        }
    }

    [Fact]
    public void ParseOverrides_SkipsMalformedUnknownAndPlaceholderMismatches()
    {
        var lines = new[]
        {
            "# comment",
            "",
            "no-tab-here",                              // malformed
            "unknown.key\tSomething",                  // unknown key
            "routes.selected\tChoisi : {1}",           // placeholder {1} != English {0}
            "routes.selected\tChoisi : {0}",           // valid — same placeholder set
            "routes.pick\tChoisissez un itineraire.",  // valid, no placeholders
        };

        Dictionary<string, string> overrides = TeamsterStrings.ParseOverrides(lines, out int skipped);

        Assert.Equal(3, skipped); // malformed + unknown + placeholder-mismatch
        Assert.Equal(2, overrides.Count);
        Assert.Equal("Choisi : {0}", overrides["routes.selected"]);
        Assert.Equal("Choisissez un itineraire.", overrides["routes.pick"]);
    }

    [Theory]
    [InlineData("C:\\temp\\notes")]      // backslash before 't'/'n' — the escape trap
    [InlineData("line\nbreak\tand tab")] // real control chars
    [InlineData("100% sure")]            // literal percent
    [InlineData("plain")]
    public void OverrideValues_RoundTripThroughTheTemplateEncoding(string value)
    {
        // A translated value with backslashes, control chars, or percents must
        // survive template write → parse unchanged (the encoding is invertible).
        var overrideLine = "routes.pick\t" + EscapeForTest(value);
        Dictionary<string, string> parsed = TeamsterStrings.ParseOverrides(
            new[] { overrideLine }, out int skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(value, parsed["routes.pick"]);
    }

    // Mirrors the catalog's private Escape so the test drives the real
    // Unescape path through ParseOverrides.
    private static string EscapeForTest(string value)
    {
        return value
            .Replace("%", "%25").Replace("\t", "%09").Replace("\n", "%0A").Replace("\r", "%0D");
    }

    [Fact]
    public void PlaceholderIndices_ExtractsDistinctSortedSlots()
    {
        Assert.Equal(new[] { 0, 1 }, TeamsterStrings.PlaceholderIndices("a {1} b {0} c {1}").ToArray());
        Assert.Empty(TeamsterStrings.PlaceholderIndices("no slots here"));
    }

    [Fact]
    public void LoadOverrides_TranslationWins_ThenFallsBackWhenCleared()
    {
        TeamsterStrings.LoadOverrides(new Dictionary<string, string> { ["routes.pick"] = "Choisissez." });
        Assert.Equal("Choisissez.", TeamsterStrings.Get("routes.pick"));

        // A partial catalog still falls back to English for untranslated keys.
        Assert.Equal("(unnamed route)", TeamsterStrings.Get("routes.unnamed"));

        // Clearing overrides restores English everywhere (isolation for other tests).
        TeamsterStrings.LoadOverrides(new Dictionary<string, string>());
        Assert.Equal("Pick a route to profile.", TeamsterStrings.Get("routes.pick"));
    }

    [Fact]
    public void Format_BrokenTranslation_FallsBackToEnglishFormat()
    {
        // An override that somehow slipped a bad placeholder past parsing
        // (defense in depth) must not crash Format.
        TeamsterStrings.LoadOverrides(new Dictionary<string, string> { ["routes.selected"] = "Bad {" });
        string result = TeamsterStrings.Format("routes.selected", "X");
        Assert.Equal("Selected: X", result); // English format used
        TeamsterStrings.LoadOverrides(new Dictionary<string, string>());
    }
}
