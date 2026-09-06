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
