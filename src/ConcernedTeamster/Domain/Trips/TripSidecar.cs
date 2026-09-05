using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>The trip sidecar text format (CT-016; format-version 2 adds
/// CT-017 road-quality segment rows): versioned header with the owning
/// world UID, sorted segment rows, trip blocks, sample rows. Parsing is
/// fail-closed per row (malformed rows skipped and reported), refuses a
/// file whose world UID does not match the requested one (cross-world
/// isolation by construction, on top of the per-world filename), refuses
/// unknown future versions without touching the file, and flags known old
/// versions for migration (the caller backs the file up before the rewrite).
/// Segment rows persist additive accumulators, and composing sorts them by
/// cell so identical inputs yield byte-identical output. Segments aggregate
/// all recorded history — pruning old raw trips does not subtract their
/// contribution (by design).</summary>
public static class TripSidecar
{
    public const int FormatVersion = 2;

    public sealed class ParseResult
    {
        public ParseResult(
            IReadOnlyList<Trip> trips,
            RoadQualityIndex segments,
            IReadOnlyList<string> errors,
            bool refused,
            bool needsMigration)
        {
            Trips = trips;
            Segments = segments;
            Errors = errors;
            Refused = refused;
            NeedsMigration = needsMigration;
        }

        public IReadOnlyList<Trip> Trips { get; }

        public RoadQualityIndex Segments { get; }

        public IReadOnlyList<string> Errors { get; }

        /// <summary>True when the whole file was refused (wrong world,
        /// unknown future version) — the caller must not overwrite it
        /// blindly.</summary>
        public bool Refused { get; }

        /// <summary>True for a readable older format (version 1): trips
        /// loaded, segments must be recomputed, and the caller backs the
        /// file up before rewriting it in the current format.</summary>
        public bool NeedsMigration { get; }
    }

    public static string Compose(
        IReadOnlyList<Trip> trips, long worldUid, string pluginVersion,
        RoadQualityIndex? segments = null)
    {
        var builder = new StringBuilder();
        builder.Append("# Concerned Teamster trip sidecar\n");
        builder.Append("format-version: ").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("world-uid: ").Append(worldUid.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("plugin-version: ").Append(pluginVersion).Append('\n');

        if (segments is not null)
        {
            // Sorted by cell for byte-identical output on identical input.
            var keys = new List<RoadSegmentKey>(segments.Segments.Keys);
            keys.Sort((left, right) =>
                left.CellX != right.CellX
                    ? left.CellX.CompareTo(right.CellX)
                    : left.CellZ.CompareTo(right.CellZ));
            foreach (RoadSegmentKey key in keys)
            {
                RoadSegmentStats stats = segments.Segments[key];
                builder.Append("seg: ")
                    .Append(key.CellX.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(key.CellZ.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.SampleCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.PairCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.SumAbsGradeDelta.ToString("F3", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.GradeCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.SumGrade.ToString("F3", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.MaxAbsGrade.ToString("F3", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.LevelCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(stats.SumLevelSpeed.ToString("F3", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        for (int index = 0; index < trips.Count; index++)
        {
            Trip trip = trips[index];
            builder.Append("trip: ")
                .Append((index + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(trip.CartId).Append('\n');
            foreach (TripSample sample in trip.Samples)
            {
                builder.Append("s: ")
                    .Append(sample.TimeSeconds.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.PositionX.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.PositionZ.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(float.IsNaN(sample.GradePercent)
                        ? "-"
                        : sample.GradePercent.ToString("F1", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(float.IsNaN(sample.SpeedMetersPerSecond)
                        ? "-"
                        : sample.SpeedMetersPerSecond.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.TotalMass.ToString("F1", CultureInfo.InvariantCulture)).Append('\n');
            }

            builder.Append("end-trip\n");
        }

        return builder.ToString();
    }

    public static ParseResult Parse(string? text, long expectedWorldUid)
    {
        var trips = new List<Trip>();
        var errors = new List<string>();
        var segments = new RoadQualityIndex();
        if (string.IsNullOrEmpty(text))
        {
            return new ParseResult(trips, segments, errors, refused: false, needsMigration: false);
        }

        int formatVersion = -1;
        long worldUid = 0;
        string? currentCartId = null;
        List<TripSample>? currentSamples = null;

        string[] lines = text!.Split('\n');
        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            string line = lines[lineNumber].TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("format-version:", StringComparison.Ordinal))
            {
                int.TryParse(line.Substring(15).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out formatVersion);
                if (formatVersion != FormatVersion && formatVersion != 1)
                {
                    errors.Add($"unsupported format-version {formatVersion}; file left untouched");
                    return new ParseResult(Array.Empty<Trip>(), segments, errors,
                        refused: true, needsMigration: false);
                }

                continue;
            }

            if (line.StartsWith("world-uid:", StringComparison.Ordinal))
            {
                long.TryParse(line.Substring(10).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out worldUid);
                if (worldUid != expectedWorldUid)
                {
                    errors.Add($"world-uid {worldUid} does not match this world ({expectedWorldUid}); file refused");
                    return new ParseResult(Array.Empty<Trip>(), segments, errors,
                        refused: true, needsMigration: false);
                }

                continue;
            }

            if (line.StartsWith("seg:", StringComparison.Ordinal))
            {
                ParseSegment(line.Substring(4), lineNumber + 1, segments, errors);
                continue;
            }

            if (line.StartsWith("plugin-version:", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("trip:", StringComparison.Ordinal))
            {
                if (currentSamples is not null)
                {
                    errors.Add($"line {lineNumber + 1}: trip started before previous ended; previous kept");
                    FinishTrip(trips, currentCartId, currentSamples);
                }

                string[] parts = line.Substring(5).Split('|');
                currentCartId = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                currentSamples = new List<TripSample>();
                continue;
            }

            if (line.StartsWith("s:", StringComparison.Ordinal))
            {
                if (currentSamples is null)
                {
                    errors.Add($"line {lineNumber + 1}: sample outside a trip; skipped");
                    continue;
                }

                TripSample? sample = ParseSample(line.Substring(2), lineNumber + 1, errors);
                if (sample is not null)
                {
                    currentSamples.Add(sample.Value);
                }

                continue;
            }

            if (line.StartsWith("end-trip", StringComparison.Ordinal))
            {
                if (currentSamples is null)
                {
                    errors.Add($"line {lineNumber + 1}: end-trip without a trip; skipped");
                    continue;
                }

                FinishTrip(trips, currentCartId, currentSamples);
                currentCartId = null;
                currentSamples = null;
                continue;
            }

            errors.Add($"line {lineNumber + 1}: unrecognized line; skipped");
        }

        if (currentSamples is not null)
        {
            errors.Add("file ended inside a trip; partial trip kept");
            FinishTrip(trips, currentCartId, currentSamples);
        }

        if (formatVersion < 0 && trips.Count > 0)
        {
            errors.Add("missing format-version header");
        }

        return new ParseResult(trips, segments, errors, refused: false,
            needsMigration: formatVersion == 1);
    }

    private static void ParseSegment(
        string body, int lineNumber, RoadQualityIndex segments, List<string> errors)
    {
        string[] parts = body.Split('|');
        if (parts.Length < 10)
        {
            errors.Add($"line {lineNumber}: segment needs 10 fields; skipped");
            return;
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cellX) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cellZ) ||
            !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int samples) ||
            !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pairs) ||
            !float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sumAbsGradeDelta) ||
            !int.TryParse(parts[5].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int gradeCount) ||
            !float.TryParse(parts[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sumGrade) ||
            !float.TryParse(parts[7].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float maxAbsGrade) ||
            !int.TryParse(parts[8].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int levelCount) ||
            !float.TryParse(parts[9].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float sumLevelSpeed))
        {
            errors.Add($"line {lineNumber}: malformed segment; skipped");
            return;
        }

        var stats = new RoadSegmentStats();
        stats.Restore(samples, pairs, sumAbsGradeDelta, gradeCount, sumGrade,
            maxAbsGrade, levelCount, sumLevelSpeed);
        segments.RestoreSegment(new RoadSegmentKey(cellX, cellZ), stats);
    }

    /// <summary>The complete save-merge step, shared by the runtime service
    /// and the CT-020 retention gate test so the gate exercises production
    /// code, not a mirror: recompute segments from the stored trips when the
    /// parse flagged a v1 migration, fold the new trips into the segment
    /// scores, append them to the stored trips, prune to the retention cap,
    /// and compose the file text. Pure — no IO and no logging; refusal and
    /// backup decisions stay with the caller, which must back the file up
    /// BEFORE writing the result over a refused or migrating file.</summary>
    public static string MergeAndCompose(
        ParseResult existing, IReadOnlyList<Trip> newTrips, int maxTrips,
        long worldUid, string pluginVersion)
    {
        RoadQualityIndex segments = existing.NeedsMigration
            ? RoadQualityIndex.ComputeFromTrips(existing.Trips)
            : existing.Segments;

        foreach (Trip trip in newTrips)
        {
            segments.AddTrip(trip);
        }

        var merged = new List<Trip>(existing.Trips);
        merged.AddRange(newTrips);
        IReadOnlyList<Trip> pruned = Prune(merged, maxTrips);
        return Compose(pruned, worldUid, pluginVersion, segments);
    }

    /// <summary>Keeps the newest trips within the cap (oldest pruned) and
    /// renumbers ids densely from 1.</summary>
    public static IReadOnlyList<Trip> Prune(IReadOnlyList<Trip> trips, int maxTrips)
    {
        int keep = Math.Min(trips.Count, Math.Max(0, maxTrips));
        var pruned = new List<Trip>(keep);
        for (int index = trips.Count - keep; index < trips.Count; index++)
        {
            Trip trip = trips[index];
            pruned.Add(new Trip(pruned.Count + 1, trip.CartId, trip.Samples));
        }

        return pruned;
    }

    private static void FinishTrip(List<Trip> trips, string? cartId, List<TripSample> samples)
    {
        if (samples.Count > 0)
        {
            trips.Add(new Trip(trips.Count + 1, cartId ?? string.Empty, samples.ToArray()));
        }
    }

    private static TripSample? ParseSample(string body, int lineNumber, List<string> errors)
    {
        string[] parts = body.Split('|');
        if (parts.Length < 6)
        {
            errors.Add($"line {lineNumber}: sample needs 6 fields; skipped");
            return null;
        }

        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double time) ||
            !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !TryParseOptionalFloat(parts[3], out float grade) ||
            !TryParseOptionalFloat(parts[4], out float speed) ||
            !float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mass))
        {
            errors.Add($"line {lineNumber}: malformed sample; skipped");
            return null;
        }

        return new TripSample(time, x, z, grade, speed, mass);
    }

    private static bool TryParseOptionalFloat(string text, out float value)
    {
        string trimmed = text.Trim();
        if (trimmed == "-")
        {
            value = float.NaN;
            return true;
        }

        return float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
