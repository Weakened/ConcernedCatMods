using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Reporting;

/// <summary>Pure composer for the sanitized support report (privacy
/// audit, CC-098). The report is aggregate-only BY SIGNATURE: no world
/// UID, file path, or player-authored text is ever passed in — sidecar
/// files are reduced to row counts and sizes before composition, and the
/// caller keys sections by the fixed sidecar suffix, never by the
/// uid-bearing file name. As defense in depth, every emitted line
/// additionally passes through <see cref="CrashReportSanitizer"/>, so an
/// identifier smuggled into a future caller's config string is scrubbed
/// rather than shipped.</summary>
internal static class SupportReportComposer
{
    public const string Header =
        "# Concerned Cartographer support report " +
        "(sanitized: no positions, names, notes, world identifiers, or file paths)";

    public const string AbsentStatus = "absent";

    public static string UnreadableStatus(Exception exception)
    {
        return "unreadable: " + (exception is null ? "unknown" : exception.GetType().Name);
    }

    /// <summary>Reduces one present sidecar to size and row counts. The
    /// content lines never travel further than the codec parse — only
    /// counts leave this method. Sizes are reported in whole KB so a
    /// large sidecar can never form a scrub-shaped long digit run.</summary>
    public static string DescribeSidecar(string suffix, IReadOnlyList<string> contentLines, long sizeBytes)
    {
        string counts = suffix switch
        {
            ".roads.tsv" => Describe(RoadAtlasCodec.Parse(contentLines)),
            ".pins.tsv" => Describe(PinCodec.Parse(contentLines)),
            _ => Describe(RouteCodec.Parse(contentLines)),
        };

        long kilobytes = (sizeBytes + 1023) / 1024;
        return $"{kilobytes} KB, {counts}";
    }

    public static List<string> Compose(
        DateTime generatedUtc,
        string pluginVersion,
        string effectiveConfig,
        IReadOnlyList<(string Suffix, string Status)> sidecars,
        int backupCount)
    {
        var lines = new List<string>
        {
            Header,
            Scrub($"generated-utc: {generatedUtc:yyyy-MM-dd HH:mm:ss}Z"),
            Scrub($"plugin-version: {pluginVersion}"),
            Scrub($"config: {effectiveConfig}"),
        };

        foreach ((string suffix, string status) in sidecars)
        {
            lines.Add(Scrub($"{suffix}: {status}"));
        }

        lines.Add(Scrub($"backups: {backupCount}"));
        return lines;
    }

    private static string Describe(RoadAtlasCodec.ParseResult result)
    {
        int points = 0;
        foreach (RoadStroke stroke in result.Strokes)
        {
            points += stroke.Points.Count;
        }

        return $"{result.Strokes.Count} strokes, {points} points, {result.MalformedRows} malformed";
    }

    private static string Describe(PinCodec.ParseResult result)
    {
        return $"{result.Pins.Count} pins, {result.MalformedRows} malformed";
    }

    private static string Describe(RouteCodec.ParseResult result)
    {
        return $"{result.Routes.Count} routes, {result.MalformedRows} malformed";
    }

    private static string Scrub(string line)
    {
        return CrashReportSanitizer.Sanitize(line, CrashReportSanitizer.MaxMessageLength);
    }
}
