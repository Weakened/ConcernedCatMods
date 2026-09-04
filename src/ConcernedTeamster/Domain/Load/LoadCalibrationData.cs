using System;
using System.Collections.Generic;
using System.Globalization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>The parsed, versioned calibration data set (CT-008) with its
/// provenance. Parsing is fail-closed per row: malformed lines are skipped
/// and reported, valid rows survive — the same discipline as sidecar
/// persistence. The model derives every verdict from these rows alone.</summary>
public sealed class LoadCalibrationData
{
    private LoadCalibrationData(
        int dataVersion,
        string gameVersion,
        string protocol,
        string generated,
        IReadOnlyList<CalibrationRow> rows,
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

    public IReadOnlyList<CalibrationRow> Rows { get; }

    /// <summary>One entry per skipped malformed line — surfaced once by the
    /// caller, never thrown.</summary>
    public IReadOnlyList<string> Errors { get; }

    public int MeasuredRowCount
    {
        get
        {
            int count = 0;
            foreach (CalibrationRow row in Rows)
            {
                if (row.Basis == CalibrationBasis.Measured)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public static LoadCalibrationData Parse(string? text)
    {
        var rows = new List<CalibrationRow>();
        var errors = new List<string>();
        int dataVersion = 0;
        string gameVersion = string.Empty;
        string protocol = string.Empty;
        string generated = string.Empty;

        if (text is null)
        {
            errors.Add("calibration text is null");
            return new LoadCalibrationData(0, gameVersion, protocol, generated, rows, errors);
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
                CalibrationRow? row = ParseRow(line.Substring(4), lineNumber + 1, errors);
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
                    // Unknown headers are tolerated for forward compatibility.
                    break;
            }
        }

        if (dataVersion <= 0)
        {
            errors.Add("missing or invalid data-version header");
        }

        return new LoadCalibrationData(dataVersion, gameVersion, protocol, generated, rows, errors);
    }

    private static CalibrationRow? ParseRow(string body, int lineNumber, List<string> errors)
    {
        string[] parts = body.Split('|');
        if (parts.Length < 4)
        {
            errors.Add($"line {lineNumber}: row needs grade | mass | outcome | basis");
            return null;
        }

        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float grade) ||
            float.IsNaN(grade) || float.IsInfinity(grade))
        {
            errors.Add($"line {lineNumber}: bad grade");
            return null;
        }

        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mass) ||
            float.IsNaN(mass) || float.IsInfinity(mass) || mass <= 0f)
        {
            errors.Add($"line {lineNumber}: bad mass");
            return null;
        }

        if (!Enum.TryParse(parts[2].Trim(), ignoreCase: false, out CalibrationOutcome outcome))
        {
            errors.Add($"line {lineNumber}: unknown outcome '{parts[2].Trim()}'");
            return null;
        }

        if (!Enum.TryParse(parts[3].Trim(), ignoreCase: false, out CalibrationBasis basis))
        {
            errors.Add($"line {lineNumber}: unknown basis '{parts[3].Trim()}'");
            return null;
        }

        string note = parts.Length > 4 ? parts[4].Trim() : string.Empty;
        return new CalibrationRow(grade, mass, outcome, basis, note);
    }
}
