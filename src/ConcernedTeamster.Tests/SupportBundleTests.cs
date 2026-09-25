using System.Globalization;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedTeamster.Domain.Support;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;

namespace ConcernedTeamster.Tests;

/// <summary>CT-039 privacy-audit regression suite for the support bundle.
/// The bundle must be provably free of world UIDs, filesystem paths, and
/// machine usernames even when a realistic mix of inputs (including this
/// mod's own actual log-warning shape, which embeds a full path) is fed
/// through the composer — planted, not assumed.</summary>
public class SupportBundleTests
{
    private const long WorldUid = 731284559L;

    private static readonly Regex LongDigitRun = new(@"\d{7,}", RegexOptions.Compiled);

    private static List<SupportBundleComposer.SidecarSummary> RealisticSidecars()
    {
        return new List<SupportBundleComposer.SidecarSummary>
        {
            new("teamster_trips_" + WorldUid + ".txt", 42L, 12, 0),
            new("teamster_trips_991234567.txt", 3L, 1, 2),
        };
    }

    private static List<RecoveryEvent> RealisticRecoveryEvents()
    {
        return new List<RecoveryEvent>
        {
            new(
                "refused",
                "teamster_trips_" + WorldUid + ".txt",
                "Trip sidecar was refused (world-uid " + WorldUid + " does not match this world); " +
                "backing it up and starting fresh."),
        };
    }

    private static List<string> RealisticLogLines()
    {
        return new List<string>
        {
            // This mod's OWN actual warning shape (TripRecordingService.Persist)
            // embeds a full path — exactly the riskiest realistic input.
            "[Warning] Trip sidecar at C:\\Users\\erenc\\AppData\\Roaming\\r2modmanPlus-local\\" +
                "Valheim\\profiles\\TCT-Dev\\BepInEx\\config\\ConcernedCatMods\\ConcernedTeamster\\" +
                "teamster_trips_" + WorldUid + ".txt: was refused; backing it up and starting fresh.",
            "[Info] Cart telemetry sampler armed: interval 0.5 s, radius 25 m.",
        };
    }

    private static List<string> ComposeRealisticBundle()
    {
        return SupportBundleComposer.Compose(
            new DateTime(2026, 9, 6, 12, 34, 56, DateTimeKind.Utc),
            "0.8.0+e9615b00",
            "Enabled=True, DebugLogging=False, Brake.Enabled=True, Trips.Enabled=True, UiScale=1, " +
                "Profile=Standard, ConfigSchemaVersion=1",
            new[] { "BetterCarts v1.0.6 (adapted): reduces cart mass by default." },
            RealisticSidecars(),
            backupFileCount: 2,
            RealisticRecoveryEvents(),
            RealisticLogLines());
    }

    // ------------------------------------------------------------------
    // Forbidden content
    // ------------------------------------------------------------------

    [Fact]
    public void Bundle_HasNoUidShapedValueAnywhere()
    {
        string bundle = string.Join("\n", ComposeRealisticBundle());

        Assert.DoesNotContain(WorldUid.ToString(CultureInfo.InvariantCulture), bundle);
        Assert.DoesNotContain("991234567", bundle);
        Assert.DoesNotMatch(LongDigitRun, bundle);
    }

    [Fact]
    public void Bundle_HasNoFilesystemPathsOrUsernames()
    {
        string bundle = string.Join("\n", ComposeRealisticBundle());

        Assert.DoesNotContain(":\\", bundle);
        Assert.DoesNotContain(":/", bundle);
        Assert.DoesNotContain("Users", bundle);
        Assert.DoesNotContain("erenc", bundle);
        Assert.DoesNotContain("r2modmanPlus", bundle);
        Assert.DoesNotContain("profiles", bundle);
        Assert.DoesNotContain("TCT-Dev", bundle);
    }

    [Fact]
    public void Bundle_PlantedIdentifiersInCallerStrings_AreScrubbed()
    {
        // Defense in depth: even if a future caller leaks an identifier
        // into the config summary or compatibility lines, the composed
        // bundle is still scrubbed.
        string bundle = string.Join("\n", SupportBundleComposer.Compose(
            new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc),
            "0.8.0",
            $"home=(123.4, -567.8), uid={WorldUid}",
            new[] { $"leaked https://example.com/{WorldUid} in a compat line" },
            new List<SupportBundleComposer.SidecarSummary>(),
            backupFileCount: 0,
            new List<RecoveryEvent>(),
            new List<string> { "C:\\Users\\someone\\secret.txt mentioned in a log line" }));

        Assert.DoesNotContain("123.4", bundle);
        Assert.DoesNotContain(WorldUid.ToString(CultureInfo.InvariantCulture), bundle);
        Assert.DoesNotContain("example.com", bundle);
        Assert.DoesNotContain("someone", bundle);
        Assert.DoesNotContain("C:\\Users", bundle);
        // The path's own terminal segment must not survive either — a
        // review finding: the first cut of this sanitizer kept it,
        // relying on today's one real caller never putting anything
        // sensitive in a filename, which this "defense in depth" test is
        // specifically meant not to assume.
        Assert.DoesNotContain("secret", bundle);
    }

    // ------------------------------------------------------------------
    // Preserved diagnostics
    // ------------------------------------------------------------------

    [Fact]
    public void Bundle_KeepsAggregateDiagnostics()
    {
        List<string> lines = ComposeRealisticBundle();
        string bundle = string.Join("\n", lines);

        Assert.Equal(SupportBundleComposer.Header, lines[0]);
        Assert.Contains("generated-utc: 2026-09-06 12:34:56Z", bundle);
        Assert.Contains("plugin-version: 0.8.0+e9615b00", bundle);
        Assert.Contains("Enabled=True", bundle);
        Assert.Contains("BetterCarts v1.0.6 (adapted)", bundle);
        Assert.Contains("12 trip(s), 0 malformed row(s)", bundle);
        Assert.Contains("1 trip(s), 2 malformed row(s)", bundle);
        Assert.Contains("2 backup file(s)", bundle);
        Assert.Contains("[refused]:", bundle);
        Assert.Contains("Cart telemetry sampler armed", bundle);
        Assert.DoesNotContain("…[truncated]", bundle);
    }

    [Fact]
    public void Bundle_HeaderClaimsMatchTheAudit()
    {
        Assert.Contains(
            "no world identifiers beyond a masked number, no player names, no full paths",
            SupportBundleComposer.Header);
    }

    [Fact]
    public void Bundle_NoCompatibilityOrSidecarData_SaysSoPlainly()
    {
        string bundle = string.Join("\n", SupportBundleComposer.Compose(
            new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc),
            "0.8.0",
            "Enabled=True",
            Array.Empty<string>(),
            new List<SupportBundleComposer.SidecarSummary>(),
            backupFileCount: 0,
            new List<RecoveryEvent>(),
            Array.Empty<string>()));

        Assert.Contains("(not yet probed)", bundle);
        Assert.Contains("(none found)", bundle);
        Assert.Contains("(none)", bundle);
        Assert.Contains("(none captured)", bundle);
    }

    // ------------------------------------------------------------------
    // Sanitizer, standalone
    // ------------------------------------------------------------------

    [Fact]
    public void Sanitizer_MasksWindowsPathsCoordinatesAndLongDigitRuns()
    {
        string sanitized = SupportBundleSanitizer.Sanitize(
            "C:\\Users\\erenc\\file.txt at (12.5, -30.0) for world 123456789 via https://x.example/y");

        Assert.DoesNotContain("erenc", sanitized);
        Assert.DoesNotContain("12.5", sanitized);
        Assert.DoesNotContain("123456789", sanitized);
        Assert.DoesNotContain("x.example", sanitized);
    }

    [Fact]
    public void Sanitizer_PathsTerminalSegment_DoesNotSurviveEvenWhenItIsTheSensitivePart()
    {
        // CT-039 review finding: the first cut of this sanitizer kept a
        // path's final segment unmasked ("<path>/$1"), mirroring the
        // sibling product's sanitizer. That is safe for Cartographer only
        // because its composer never passes it truly arbitrary text; this
        // sanitizer's whole purpose is scrubbing arbitrary free-text log
        // lines, so a filename that is itself the identifying content
        // (not just a generic name like "file.txt") must not survive.
        string sanitized = SupportBundleSanitizer.Sanitize(
            "C:\\Users\\someone\\ErensSecretWorldBackup.txt mentioned in a log line");

        Assert.DoesNotContain("ErensSecretWorldBackup", sanitized);
        Assert.DoesNotContain("someone", sanitized);
    }

    // ------------------------------------------------------------------
    // Paths containing spaces (#410)
    //
    // Both path patterns forbade whitespace inside a segment, so the match
    // stopped at the first space in a mod-manager path and everything after
    // it travelled verbatim. The user name is before that point and always
    // went, which is why this was invisible to the two tests above.
    //
    // It matters more here than in the sibling product for a reason the
    // class comment already records: LogTailRecorder hands this sanitizer
    // raw BepInEx log lines, and this mod's own warning lines embed full
    // paths. A support bundle is then a file whose purpose is to be handed
    // to somebody else.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(
        "Could not open C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager\\DataFolder" +
        "\\Valheim\\profiles\\tcc-dev\\BepInEx\\config\\teamster.cfg",
        new[] { "erenc", "Mod Manager", "DataFolder", "profiles", "tcc-dev", "BepInEx", "\\" })]
    [InlineData(
        "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Valheim\\valheim_Data\\Managed\\a.dll",
        new[] { "Program Files", "steamapps", "valheim_Data" })]
    // The path ends at a folder whose name has spaces: nothing of it survives,
    // because this sanitizer keeps no terminal segment at all.
    [InlineData(
        "profile root C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager",
        new[] { "erenc", "Mod Manager", "AppData" })]
    // The run-start signal is "not a lower-case letter", not "ASCII upper
    // case": an ASCII-only rule silently excludes every non-Latin folder name,
    // which is the non-English-locale player it is meant to protect.
    [InlineData(
        "C:\\Users\\erenc\\Mein \u00c4rger\\Valheim\\profiles\\geheim\\x.cfg",
        new[] { "erenc", "Arger", "geheim", "profiles" })]
    [InlineData(
        "C:\\Users\\erenc\\\u041c\u043e\u0438 \u041c\u043e\u0434\u044b\\Valheim\\profiles\\secret\\x.cfg",
        new[] { "erenc", "secret", "profiles" })]
    [InlineData(
        "C:\\Games\\Valheim !Mods\\profiles\\erens-run\\x.cfg",
        new[] { "!Mods", "erens-run", "profiles" })]
    public void Sanitizer_PathWithSpaces_IsScrubbedPastTheFirstSpace(string line, string[] forbidden)
    {
        string sanitized = SupportBundleSanitizer.Sanitize(line);

        foreach (string fragment in forbidden)
        {
            Assert.DoesNotContain(fragment, sanitized);
        }

        Assert.Contains("<path>", sanitized);
    }

    [Theory]
    // A line that is nothing but a path becomes the marker and nothing else.
    // These two are asserted as exact output rather than as absent fragments
    // on purpose: against the old pattern their leftover tail happened to be
    // 40-odd characters of `[A-Za-z0-9+/=_-]`, so `TokenBlob` masked it and an
    // absence assertion would have passed for a reason that has nothing to do
    // with path scrubbing. A dot in the wrong place, and it would not have.
    [InlineData("/Users/erenc/Library/Application Support/Steam/steamapps/common/Valheim/plugin.dll")]
    [InlineData("/home/erenc/.config/r2modmanPlus-local/Valheim/profiles/My Test/BepInEx/config/a.cfg")]
    [InlineData("C:\\Users\\erenc\\AppData\\Roaming\\Thunderstore Mod Manager\\DataFolder\\Valheim" +
                "\\profiles\\My Secret Base\\BepInEx\\config\\teamster.cfg")]
    public void Sanitizer_APathOnItsOwn_BecomesTheMarkerAlone(string line)
    {
        Assert.Equal("<path>", SupportBundleSanitizer.Sanitize(line));
    }

    [Theory]
    // The other half: a sanitizer that admits spaces too freely deletes the
    // sentence around an unquoted path, which would buy privacy with the
    // diagnostic the bundle exists for. Each row is a shape the sibling
    // product's review demonstrated being swallowed.
    [InlineData("wrote C:\\a\\b.cfg OK", "wrote <path> OK")]
    [InlineData("plugin loaded from C:\\a\\plugin.dll 0.9.0", "plugin loaded from <path> 0.9.0")]
    [InlineData("C:\\a\\b.cfg Cannot Be Read", "<path> Cannot Be Read")]
    [InlineData("C:\\a\\b.cfg Zugriffsverweigerung", "<path> Zugriffsverweigerung")]
    [InlineData("C:\\a\\b.cfg Reason: disk full", "<path> Reason: disk full")]
    [InlineData(
        "Could not open C:\\a\\b.txt was not found, see the log/file for details.",
        "Could not open <path> was not found, see the log/file for details.")]
    // Two paths in one line: a run must not consume the second one's drive
    // letter and leave that whole path behind.
    [InlineData(
        "copy C:\\a\\b.cfg to D:\\home\\erenc\\valheim-mods\\x.cfg",
        "copy <path> to <path>")]
    public void Sanitizer_KeepsTheSentenceAroundAPath(string line, string expected)
    {
        Assert.Equal(expected, SupportBundleSanitizer.Sanitize(line));
    }

    [Fact]
    public void Sanitizer_AWorldNameWithSpaces_StillReachesTheSaveFileRule()
    {
        // SaveFileNames runs after the path pass, so a run that ate the
        // extension would take the `.db` marker with it and the world name
        // would stop being recognised as one.
        string sanitized = SupportBundleSanitizer.Sanitize(
            "failed to load C:\\Users\\erenc\\AppData\\LocalLow\\IronGate\\Valheim\\worlds_local" +
            "\\Erens New World.db");

        Assert.DoesNotContain("erenc", sanitized);
        Assert.DoesNotContain("Erens", sanitized);
        Assert.Contains("<save>.db", sanitized);
    }

    [Fact]
    public void Sanitizer_NullOrEmpty_ReturnsEmptyString()
    {
        Assert.Equal("", SupportBundleSanitizer.Sanitize(null));
        Assert.Equal("", SupportBundleSanitizer.Sanitize(""));
    }

    [Fact]
    public void Sanitizer_LongLine_IsTruncated()
    {
        // Short, space-separated words so no scrub pattern (hex/token
        // blob, path, digit run) fires and shrinks the string first —
        // this test isolates truncation, not scrubbing.
        string longLine = string.Concat(System.Linq.Enumerable.Repeat("word ", 500));
        string sanitized = SupportBundleSanitizer.Sanitize(longLine);

        Assert.True(sanitized.Length <= SupportBundleSanitizer.MaxLineLength + "…[truncated]".Length);
        Assert.EndsWith("…[truncated]", sanitized);
    }

    [Fact]
    public void Sanitizer_OrdinaryLine_IsUnchanged()
    {
        string ordinary = "Cart telemetry sampler armed: interval 0.5 s, radius 25 m.";
        Assert.Equal(ordinary, SupportBundleSanitizer.Sanitize(ordinary));
    }
}
