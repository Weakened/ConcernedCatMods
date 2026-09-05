using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>The parsed, versioned descent calibration set (CT-011) with its
/// provenance — same fail-closed per-row discipline as the load data:
/// malformed lines are skipped and reported, valid rows survive.</summary>
public sealed class DescentCalibrationData
{
    private DescentCalibrationData(
        int dataVersion,
        string gameVersion,
        string protocol,
        string generated,
        IReadOnlyList<DescentCalibrationRow> rows,
        IReadOnlyList<string> errors)
    {
        DataVersion = dataVersion;
        GameVersion = gameVersion;
        Protocol = protocol;
        Generated = generated;
        Rows = rows;
        Errors = errors;
    }

    public int DataVersion { get; }

    public string GameVersion { get; }

    public string Protocol { get; }

    public string Generated { get; }

    public IReadOnlyList<DescentCalibrationRow> Rows { get; }

    public IReadOnlyList<string> Errors { get; }

    public int MeasuredRowCount
    {
        get
        {
            int count = 0;
            foreach (DescentCalibrationRow row in Rows)
            {
                if (row.Basis == CalibrationBasis.Measured)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static DescentCalibrationData Parse(string? text)
    {
        var rows = new List<DescentCalibrationRow>();
        var errors = new List<string>();
        int dataVersion = 0;
        string gameVersion = string.Empty;
        string protocol = string.Empty;
        string generated = string.Empty;

        if (text is null)
        {
            errors.Add("calibration text is null");
            return new DescentCalibrationData(0, gameVersion, protocol, generated, rows, errors);
        }

        string[] lines = text.Split('\n');
        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            string line = lines[lineNumber].TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("row:", StringComparison.Ordinal))
            {
                DescentCalibrationRow? row = ParseRow(line.Substring(4), lineNumber + 1, errors);
                if (row is not null)
                {
                    rows.Add(row);
                }

                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                errors.Add($"line {lineNumber + 1}: not a header or row");
                continue;
            }

            string key = line.Substring(0, colon).Trim();
            string value = line.Substring(colon + 1).Trim();
            switch (key)
            {
                case "data-version":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dataVersion))
                    {
                        errors.Add($"line {lineNumber + 1}: bad data-version");
                    }

                    break;
                case "game-version":
                    gameVersion = value;
                    break;
                case "protocol":
                    protocol = value;
                    break;
                case "generated":
                    generated = value;
                    break;
                default:
                    break;
            }
        }

        if (dataVersion <= 0)
        {
            errors.Add("missing or invalid data-version header");
        }

        return new DescentCalibrationData(dataVersion, gameVersion, protocol, generated, rows, errors);
    }

    private static DescentCalibrationRow? ParseRow(string body, int lineNumber, List<string> errors)
    {
        string[] parts = body.Split('|');
        if (parts.Length < 5)
        {
            errors.Add($"line {lineNumber}: row needs grade | mass | speed | outcome | basis");
            return null;
        }

        if (!TryParseFinite(parts[0], out float grade) || grade < 0f)
        {
            errors.Add($"line {lineNumber}: bad down-grade");
            return null;
        }

        if (!TryParseFinite(parts[1], out float mass) || mass <= 0f)
        {
            errors.Add($"line {lineNumber}: bad mass");
            return null;
        }

        if (!TryParseFinite(parts[2], out float speed) || speed < 0f)
        {
            errors.Add($"line {lineNumber}: bad speed");
            return null;
        }

        if (!Enum.TryParse(parts[3].Trim(), ignoreCase: false, out DescentOutcome outcome))
        {
            errors.Add($"line {lineNumber}: unknown outcome '{parts[3].Trim()}'");
            return null;
        }

        if (!Enum.TryParse(parts[4].Trim(), ignoreCase: false, out CalibrationBasis basis))
        {
            errors.Add($"line {lineNumber}: unknown basis '{parts[4].Trim()}'");
            return null;
        }

        string note = parts.Length > 5 ? parts[5].Trim() : string.Empty;
        return new DescentCalibrationRow(grade, mass, speed, outcome, basis, note);
    }

    private static bool TryParseFinite(string text, out float value)
    {
        return float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
