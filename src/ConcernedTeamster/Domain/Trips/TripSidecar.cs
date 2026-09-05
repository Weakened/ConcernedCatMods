using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheConcernedCat.ConcernedTeamster.Domain.Trips;

/// <summary>The trip sidecar text format (CT-016): versioned header with
/// the owning world UID, trip blocks, sample rows. Parsing is fail-closed
/// per row (malformed rows skipped and reported), refuses a file whose
/// world UID does not match the requested one (cross-world isolation by
/// construction, on top of the per-world filename), and refuses unknown
/// format versions without touching the file (the caller backs it up
/// before any migration ever rewrites it).</summary>
public static class TripSidecar
{
    public const int FormatVersion = 1;

    public sealed class ParseResult
    {
        public ParseResult(IReadOnlyList<Trip> trips, IReadOnlyList<string> errors, bool refused)
        {
            Trips = trips;
            Errors = errors;
            Refused = refused;
        }

        public IReadOnlyList<Trip> Trips { get; }

        public IReadOnlyList<string> Errors { get; }

        /// <summary>True when the whole file was refused (wrong world,
        /// unknown version) — the caller must not overwrite it blindly.</summary>
        public bool Refused { get; }
    }

    public static string Compose(IReadOnlyList<Trip> trips, long worldUid, string pluginVersion)
    {
        var builder = new StringBuilder();
        builder.Append("# Concerned Teamster trip sidecar\n");
        builder.Append("format-version: ").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("world-uid: ").Append(worldUid.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("plugin-version: ").Append(pluginVersion).Append('\n');

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
        if (string.IsNullOrEmpty(text))
        {
            return new ParseResult(trips, errors, refused: false);
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
                if (formatVersion != FormatVersion)
                {
                    errors.Add($"unsupported format-version {formatVersion}; file left untouched");
                    return new ParseResult(Array.Empty<Trip>(), errors, refused: true);
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
                    return new ParseResult(Array.Empty<Trip>(), errors, refused: true);
                }

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

        return new ParseResult(trips, errors, refused: false);
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
