using System.Globalization;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace ConcernedCartographer.Tests;

/// <summary>CC-098 privacy-audit regression suite for the support
/// diagnostics. The support report must be provably free of world UIDs,
/// filesystem paths, machine usernames, coordinates, and pin/route
/// content: sidecar rows carrying exactly those values are fed through
/// the composer and the full report is checked pattern-by-pattern.
/// SafeLogText gets the same treatment for exception text bound for
/// LogOutput.log.</summary>
public class SupportReportPrivacyTests
{
    private const long WorldUid = 731284559L;

    private static readonly Regex LongDigitRun = new(@"\d{7,}", RegexOptions.Compiled);

    private static readonly Regex CoordinatePair = new(
        @"\(\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static List<string> PinSidecarContent()
    {
        var pin = new AtlasPin(new AtlasId(AtlasId.PinKind, Guid.NewGuid()))
        {
            Revision = 3,
            Name = "Erens Secret Base",
            Notes = "buried silver at the birch",
            Category = "hideouts",
            Position = new RoadPoint(1234.5f, 60f, -789.25f),
        };
        pin.Tags.Add("secret");

        var lines = new List<string> { PinCodec.Header, PinCodec.SerializeRow(pin) };
        return lines;
    }

    private static List<string> RoadSidecarContent()
    {
        var stroke = new RoadStroke(Guid.NewGuid(), RoadKind.Dirt, RoadObservationSource.Construction);
        stroke.Points.Add(new RoadPoint(1010.75f, 31f, -2087.5f));
        stroke.Points.Add(new RoadPoint(1013.25f, 31f, -2085.0f));
        return new List<string>(RoadAtlasCodec.Serialize(new[] { stroke }));
    }

    private static List<string> ComposeRealisticReport()
    {
        var sidecars = new List<(string Suffix, string Status)>
        {
            (".roads.tsv", SupportReportComposer.DescribeSidecar(".roads.tsv", RoadSidecarContent(), 327_840L)),
            (".pins.tsv", SupportReportComposer.DescribeSidecar(".pins.tsv", PinSidecarContent(), 2_048L)),
            (".routes-atlas.tsv", SupportReportComposer.AbsentStatus),
        };

        return SupportReportComposer.Compose(
            new DateTime(2026, 9, 3, 12, 34, 56, DateTimeKind.Utc),
            "0.9.0+e9615b00",
            "enabled=True, capture=True, reconcile=True, survey=True, cluster=True, contrast=False, uiScale=1",
            sidecars,
            backupCount: 3);
    }

    // ------------------------------------------------------------------
    // The support report: forbidden content
    // ------------------------------------------------------------------

    [Fact]
    public void Report_HasNoWorldUidFieldAndNoUidShapedValue()
    {
        string report = string.Join("\n", ComposeRealisticReport());

        Assert.DoesNotContain("world-uid", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(WorldUid.ToString(CultureInfo.InvariantCulture), report);
        Assert.DoesNotMatch(LongDigitRun, report);
    }

    [Fact]
    public void Report_HasNoPinOrRoadContent()
    {
        string report = string.Join("\n", ComposeRealisticReport());

        Assert.DoesNotContain("Secret", report);
        Assert.DoesNotContain("silver", report);
        Assert.DoesNotContain("hideouts", report);
        Assert.DoesNotContain("1234.5", report);
        Assert.DoesNotContain("-2087.5", report);
        Assert.DoesNotMatch(CoordinatePair, report);
    }

    [Fact]
    public void Report_HasNoFilesystemPaths()
    {
        string report = string.Join("\n", ComposeRealisticReport());

        Assert.DoesNotContain(":\\", report);
        Assert.DoesNotContain(":/", report);
        Assert.DoesNotContain("Users", report);
        Assert.DoesNotContain("BepInEx", report);
        Assert.DoesNotContain("profiles", report);
    }

    [Fact]
    public void Report_PlantedIdentifiersInCallerStrings_AreScrubbed()
    {
        // Defense in depth: even if a future caller leaks into the config
        // or version strings, the composed lines are scrubbed.
        var report = string.Join("\n", SupportReportComposer.Compose(
            new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
            "0.9.0+e9615b00 from C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager",
            $"uid={WorldUid}, home=(123.4, -567.8), world=MyWorld.db, dsn=https://key@sentry.example/1",
            new List<(string, string)> { (".pins.tsv", SupportReportComposer.AbsentStatus) },
            backupCount: 0));

        Assert.DoesNotContain("erenc", report);
        Assert.DoesNotContain("C:\\Users", report);
        Assert.DoesNotContain("AppData", report);
        Assert.DoesNotContain(WorldUid.ToString(CultureInfo.InvariantCulture), report);
        Assert.DoesNotContain("123.4", report);
        Assert.DoesNotContain("MyWorld", report);
        Assert.DoesNotContain("sentry.example", report);
    }

    // ------------------------------------------------------------------
    // The support report: preserved diagnostics
    // ------------------------------------------------------------------

    [Fact]
    public void Report_KeepsAggregateDiagnostics()
    {
        List<string> lines = ComposeRealisticReport();
        string report = string.Join("\n", lines);

        Assert.Equal(SupportReportComposer.Header, lines[0]);
        Assert.Contains("generated-utc: 2026-09-03 12:34:56Z", report);
        Assert.Contains("plugin-version: 0.9.0+e9615b00", report);
        Assert.Contains("enabled=True", report);
        Assert.Contains(".roads.tsv: 321 KB, 1 strokes, 2 points, 0 malformed", report);
        Assert.Contains(".pins.tsv: 2 KB, 1 pins, 0 malformed", report);
        Assert.Contains(".routes-atlas.tsv: absent", report);
        Assert.Contains("backups: 3", report);
        // The scrubber must not mangle any legitimate line.
        Assert.DoesNotContain("<n>", report);
        Assert.DoesNotContain("<path>", report);
        Assert.DoesNotContain("…[truncated]", report);
    }

    [Fact]
    public void Report_HeaderClaimsMatchTheAudit()
    {
        Assert.Contains("no positions, names, notes, world identifiers, or file paths", SupportReportComposer.Header);
    }

    [Fact]
    public void DescribeSidecar_CountsRoutesViaTheRouteCodec()
    {
        var content = new List<string> { RouteCodec.Header };
        string status = SupportReportComposer.DescribeSidecar(".routes-atlas.tsv", content, 100L);
        Assert.Equal("1 KB, 0 routes, 0 malformed", status);
    }

    [Fact]
    public void UnreadableStatus_NamesOnlyTheExceptionType()
    {
        string status = SupportReportComposer.UnreadableStatus(
            new IOException("Access to C:\\Users\\erenc\\secret.tsv denied"));
        Assert.Equal("unreadable: IOException", status);
    }

    // ------------------------------------------------------------------
    // Paths containing spaces (#388)
    //
    // The sanitizer's path patterns forbade whitespace inside a segment, so
    // the match stopped at the first space in a mod-manager path and
    // everything after it travelled verbatim. The user name sits before
    // that point and was already scrubbed; the profile name, the folder
    // layout and the player's own folder names were not. These are the
    // cases that fail against that pattern, followed by the prose the fix
    // must not swallow in exchange.
    // ------------------------------------------------------------------

    private static string Scrub(string text)
    {
        return CrashReportSanitizer.Sanitize(text, CrashReportSanitizer.MaxMessageLength);
    }

    [Fact]
    public void Sanitize_ThunderstoreProfilePath_IsReplacedPastTheFirstSpace()
    {
        string scrubbed = Scrub(
            "Could not find file 'C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager" +
            "\\DataFolder\\Valheim\\profiles\\tcc-cartographer-test\\BepInEx\\config" +
            "\\theconcernedcat.cartographer.cfg'.");

        Assert.DoesNotContain("erenc", scrubbed);
        Assert.DoesNotContain("Mod Manager", scrubbed);
        Assert.DoesNotContain("DataFolder", scrubbed);
        Assert.DoesNotContain("profiles", scrubbed);
        Assert.DoesNotContain("tcc-cartographer-test", scrubbed);
        Assert.DoesNotContain("BepInEx", scrubbed);
        Assert.DoesNotContain("\\", scrubbed);
        // The file name is still the diagnostic, and the sentence survives.
        Assert.Contains("<path>/theconcernedcat.cartographer.cfg", scrubbed);
        Assert.StartsWith("Could not find file '", scrubbed);
    }

    [Fact]
    public void Sanitize_MacOsApplicationSupportPath_IsReplacedPastTheFirstSpace()
    {
        string scrubbed = Scrub(
            "/Users/erenc/Library/Application Support/Steam/steamapps/common/Valheim/plugin.dll");

        Assert.DoesNotContain("erenc", scrubbed);
        Assert.DoesNotContain("Application Support", scrubbed);
        Assert.DoesNotContain("steamapps", scrubbed);
        Assert.Equal("<path>/plugin.dll", scrubbed);
    }

    [Fact]
    public void Sanitize_R2ModManProfileWithASpace_IsReplacedPastTheFirstSpace()
    {
        string scrubbed = Scrub(
            "/home/erenc/.config/r2modmanPlus-local/Valheim/profiles/My Test/BepInEx/config/a.cfg");

        Assert.DoesNotContain("erenc", scrubbed);
        Assert.DoesNotContain("My Test", scrubbed);
        Assert.DoesNotContain("r2modmanPlus-local", scrubbed);
        Assert.Equal("<path>/a.cfg", scrubbed);
    }

    [Fact]
    public void Sanitize_PathEndingInAFolderWithSpaces_KeepsNoneOfTheFolderName()
    {
        // No file name to keep: the path ends at the profile root, which is
        // where the pattern used to hand back "Mod Manager" on its own.
        string scrubbed = Scrub(
            "0.9.0+e9615b00 from C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager");

        Assert.DoesNotContain("Mod Manager", scrubbed);
        Assert.DoesNotContain("AppData", scrubbed);
        Assert.Equal("0.9.0+e9615b00 from <path>/Thunderstore", scrubbed);
    }

    [Fact]
    public void Sanitize_ProgramFilesPath_IsReplacedWhole()
    {
        string scrubbed = Scrub(
            "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Valheim\\valheim_Data\\Managed\\a.dll" +
            " could not be loaded");

        Assert.DoesNotContain("Program Files", scrubbed);
        Assert.DoesNotContain("steamapps", scrubbed);
        Assert.Equal("<path>/a.dll could not be loaded", scrubbed);
    }

    [Theory]
    // The space run is admitted only where a path can actually continue, so
    // the sentence after an unquoted path is left alone even when a slash
    // appears later in it.
    [InlineData(
        "Could not open C:\\a\\b.txt was not found, see the log/file for details.",
        "Could not open <path>/b.txt was not found, see the log/file for details.")]
    [InlineData("C:\\a\\b.cfg Cannot be read.", "<path>/b.cfg Cannot be read.")]
    [InlineData(
        "Access to the path 'C:\\Users\\erenc\\AppData\\Roaming\\TMM\\p\\x.cfg' is denied.",
        "Access to the path '<path>/x.cfg' is denied.")]
    [InlineData(
        "   at Atlas.Save() in C:\\src\\repo\\AtlasStore.cs:line 42",
        "   at Atlas.Save() in <path>/AtlasStore.cs:line 42")]
    public void Sanitize_KeepsTheSentenceAroundAPath(string message, string expected)
    {
        Assert.Equal(expected, Scrub(message));
    }

    // ------------------------------------------------------------------
    // SafeLogText: exception text bound for LogOutput.log
    // ------------------------------------------------------------------

    [Fact]
    public void SafeLogText_Describe_ScrubsPathsUsernamesAndWorldUids()
    {
        var exception = new InvalidOperationException(
            "Could not read C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager\\DataFolder" +
            $"\\profiles\\CC\\BepInEx\\config\\ConcernedCatMods\\ConcernedCartographer\\{WorldUid}.pins.tsv");

        string described = SafeLogText.Describe(exception);

        Assert.Contains("InvalidOperationException", described);
        Assert.DoesNotContain("erenc", described);
        Assert.DoesNotContain("Users\\", described);
        Assert.DoesNotContain(WorldUid.ToString(CultureInfo.InvariantCulture), described);
        // #388: this assertion set used to stop at the user name, so it
        // passed while everything from the space in "Thunderstore Mod
        // Manager" onwards went to the log verbatim.
        Assert.DoesNotContain("Mod Manager", described);
        Assert.DoesNotContain("DataFolder", described);
        Assert.DoesNotContain("BepInEx", described);
        // The sidecar kind survives for diagnostics.
        Assert.Contains(".pins.tsv", described);
    }

    [Fact]
    public void SafeLogText_Brief_KeepsTheTypeAndScrubsTheMessage()
    {
        var exception = new IOException(
            "Sharing violation on MyWorld.db at (123.4, -567.8) reported by 192.168.1.20");

        string brief = SafeLogText.Brief(exception);

        Assert.StartsWith("IOException: ", brief);
        Assert.DoesNotContain("MyWorld", brief);
        Assert.DoesNotContain("123.4", brief);
        Assert.DoesNotContain("192.168.1.20", brief);
    }

    [Fact]
    public void SafeLogText_ToleratesNullExceptions()
    {
        Assert.Equal("", SafeLogText.Describe(null!));
        Assert.Equal("", SafeLogText.Brief(null!));
    }
}
