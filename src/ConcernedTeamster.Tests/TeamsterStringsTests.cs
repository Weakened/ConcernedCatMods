using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace ConcernedTeamster.Tests;

/// <summary>CT-032: the localization catalog must be complete and stable,
/// fall back to English (and warn once) on a missing key, validate
/// translation placeholders, and round-trip its template. These prove the
/// framework; the translator doc and progressive presenter migration ride on
/// top of it.
///
/// Isolation rule: TeamsterStrings is process-global static state and xUnit
/// runs test classes in parallel, so tests here may only override
/// `routes.*` keys (no other suite asserts those exact outputs) and every
/// mutation restores the default in a finally/cleanup line. Overriding a
/// `status.*`/`manifest.*` key would race the exact-output presenter
/// suites.</summary>
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
    public void Get_UnknownKey_InvokesTheWiredReporterExactlyOnce()
    {
        var reported = new List<string>();
        TeamsterStrings.MissingKeyReporter = reported.Add;
        try
        {
            TeamsterStrings.Get("ct032.reporter.once");
            TeamsterStrings.Get("ct032.reporter.once");
            Assert.Equal(new[] { "ct032.reporter.once" }, reported);
        }
        finally
        {
            TeamsterStrings.MissingKeyReporter = null;
        }
    }

    [Fact]
    public void Get_ThrowingReporter_NeverBreaksResolution()
    {
        TeamsterStrings.MissingKeyReporter = _ => throw new System.InvalidOperationException("boom");
        try
        {
            Assert.Equal("ct032.reporter.throws", TeamsterStrings.Get("ct032.reporter.throws"));
        }
        finally
        {
            TeamsterStrings.MissingKeyReporter = null;
        }
    }

    [Fact]
    public void Get_BeforeReporterIsWired_StillReportsOnceAfterWiring()
    {
        var reported = new List<string>();
        try
        {
            // Resolved with no sink: must not consume the once-only slot.
            TeamsterStrings.Get("ct032.reporter.prewire");
            TeamsterStrings.MissingKeyReporter = reported.Add;
            TeamsterStrings.Get("ct032.reporter.prewire");
            TeamsterStrings.Get("ct032.reporter.prewire");
            Assert.Equal(new[] { "ct032.reporter.prewire" }, reported);
        }
        finally
        {
            TeamsterStrings.MissingKeyReporter = null;
        }
    }

    /// <summary>The manifest row and overflow lines are composed in the
    /// game-linked panel (not compiled into this test project), so these two
    /// catalog values are pinned here byte-exactly — they are the panel's
    /// entire rendering contract.</summary>
    [Fact]
    public void ManifestPanelCompositionKeys_ArePinnedByteExact()
    {
        Assert.Equal("{0}   ×{1}   unit {2}   line {3}", TeamsterStrings.Get("manifest.row"));
        Assert.Equal("… {0} more — sort or filter to narrow", TeamsterStrings.Get("manifest.overflow"));
        Assert.Equal(
            "Iron ore   ×12   unit 12.0   line 144.0",
            TeamsterStrings.Format("manifest.row", "Iron ore", "12", "12.0", "144.0"));
        Assert.Equal(
            "… 3 more — sort or filter to narrow",
            TeamsterStrings.Format("manifest.overflow", "3"));
    }

    /// <summary>The warn/diag/coop suites assert with Contains/StartsWith,
    /// so the composed-line separators live only in these values — pinned
    /// byte-exact here (same rationale as the manifest composition keys).</summary>
    [Fact]
    public void ComposedLineSeparatorKeys_ArePinnedByteExact()
    {
        Assert.Equal("{0} — {1} {2}", TeamsterStrings.Get("warn.line"));
        Assert.Equal("[?] STUCK — {0}: {1} {2}", TeamsterStrings.Get("diag.line"));
        Assert.Equal("({0})", TeamsterStrings.Get("coop.nameList"));
        Assert.Equal("{0}.", TeamsterStrings.Get("diag.verdictEvidence"));
        Assert.Equal(
            "[!] CAUTION — Steep climb ahead (19%) with cart mass 220. Check your load.",
            TeamsterStrings.Format(
                "warn.line", "[!] CAUTION",
                "Steep climb ahead (19%) with cart mass 220.", "Check your load."));
    }

    [Fact]
    public void Get_KnownKey_NeverInvokesTheReporter()
    {
        var reported = new List<string>();
        TeamsterStrings.MissingKeyReporter = reported.Add;
        try
        {
            TeamsterStrings.Get("status.noCart");
            TeamsterStrings.Format("manifest.noMatch", "iron");
            Assert.Empty(reported);
        }
        finally
        {
            TeamsterStrings.MissingKeyReporter = null;
        }
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
