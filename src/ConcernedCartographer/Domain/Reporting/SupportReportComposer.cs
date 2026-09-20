using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>The console reply after a report has been written.
    ///
    /// <b>The reply used to contradict itself (#367).</b> It read "Sanitized
    /// support report (no positions/names/notes/world ids/paths) written to
    /// C:\…\support-report.log" — a claim that there are no paths, immediately
    /// followed by a path. The claim was never wrong, it was unattributed: the
    /// sanitization is a property of the file's CONTENTS, and the path is on the
    /// player's own screen so they can find the file we are asking them to send.
    /// Saying which is which costs a sentence, and the moment it is read is the
    /// moment somebody is already confused enough to be asking for help.
    ///
    /// The path stays. Removing it would be the wrong repair: this is the one
    /// file the troubleshooting docs ask a player to go and find.</summary>
    public static string DescribeWrittenReport(string path)
    {
        return "Support report written to " + path +
            ". The report's contents are sanitized — no positions, names, notes, world identifiers " +
            "or file paths — so it is safe to attach to a bug report. The location above is on your " +
            "own machine and is not inside the file.";
    }

    /// <summary>Reduces one present sidecar to size and row counts. The
    /// content lines never travel further than the codec parse — only
    /// counts leave this method. Sizes are reported in whole KB so a
    /// large sidecar can never form a scrub-shaped long digit run.
    ///
    /// <b>Every sidecar kind is named (#367).</b> The fallback used to parse
    /// anything unrecognised with the route codec, which was harmless only for
    /// as long as routes were the sole suffix reaching it. When the backup
    /// family grew from three of five sidecars to all five, that fallback would
    /// have described the rejected-survey memory and the terrain-intent mask as
    /// "0 routes, N malformed" — a confident, wrong answer in the one document a
    /// player sends us when something is already wrong. A suffix this method has
    /// never heard of is now described as rows, which is true of any text
    /// sidecar and claims nothing further.</summary>
    public static string DescribeSidecar(string suffix, IReadOnlyList<string> contentLines, long sizeBytes)
    {
        string counts = suffix switch
        {
            ".roads.tsv" => Describe(RoadAtlasCodec.Parse(contentLines)),
            ".pins.tsv" => Describe(PinCodec.Parse(contentLines)),
            ".routes-atlas.tsv" => Describe(RouteCodec.Parse(contentLines)),
            ".survey-rejected.tsv" => DescribeRejected(contentLines),
            ".terrain-intent.tsv" => DescribeTerrainIntent(contentLines),
            _ => DescribeRows(contentLines),
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
            // Invariant, like the backup folder's stamp and for the same reason
            // (#367): interpolation formats under CultureInfo.CurrentCulture, so
            // under a culture whose default calendar is not Gregorian this line
            // reported a different era's year. The report is a diagnostic we
            // read off a bug report; a date we misread by 543 years is worse
            // than no date, and the existing assertion on this line's exact text
            // was itself only true in some locales.
            Scrub("generated-utc: " +
                generatedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z"),
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

    private static string DescribeRejected(IReadOnlyList<string> contentLines)
    {
        var entries = SurveyRejectedCodec.Parse(contentLines, out int malformedRows);
        return $"{entries.Count} rejected, {malformedRows} malformed";
    }

    private static string DescribeTerrainIntent(IReadOnlyList<string> contentLines)
    {
        TerrainIntentCodec.ParseResult result = TerrainIntentCodec.Parse(contentLines);
        string version = result.UnsupportedVersion ? ", unsupported version" : "";
        return $"{result.Mask.Count} cells, {result.MalformedRows} malformed{version}";
    }

    /// <summary>A sidecar kind this build has no codec for. Rows, and no claim
    /// about what they mean.</summary>
    private static string DescribeRows(IReadOnlyList<string> contentLines)
    {
        return $"{(contentLines is null ? 0 : contentLines.Count)} rows";
    }

    private static string Scrub(string line)
    {
        return CrashReportSanitizer.Sanitize(line, CrashReportSanitizer.MaxMessageLength);
    }
}
