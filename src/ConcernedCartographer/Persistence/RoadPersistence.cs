using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

internal sealed class RoadPersistence
{
    private const string Header = "# ConcernedCartographer roads v1";
    private readonly ManualLogSource _log;

    public RoadPersistence(ManualLogSource log)
    {
        _log = log;
    }

    public RoadAtlas Load(long worldUid)
    {
        string path = GetPath(worldUid);
        if (!File.Exists(path))
        {
            return new RoadAtlas();
        }

        try
        {
            var orderedStrokes = new List<RoadStroke>();
            var strokesById = new Dictionary<Guid, RoadStroke>();
            int malformedRows = 0;

            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length != 7 ||
                    !Guid.TryParse(parts[0], out Guid strokeId) ||
                    !Enum.TryParse(parts[1], ignoreCase: true, out RoadKind kind) ||
                    !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pointIndex) ||
                    !TryParseFloat(parts[3], out float x) ||
                    !TryParseFloat(parts[4], out float y) ||
                    !TryParseFloat(parts[5], out float z) ||
                    parts[6] != "1")
                {
                    malformedRows++;
                    continue;
                }

                if (!strokesById.TryGetValue(strokeId, out RoadStroke stroke))
                {
                    stroke = new RoadStroke(strokeId, kind);
                    strokesById.Add(strokeId, stroke);
                    orderedStrokes.Add(stroke);
                }

                if (stroke.Kind != kind || pointIndex != stroke.Points.Count)
                {
                    malformedRows++;
                    continue;
                }

                stroke.Points.Add(new Vector3(x, y, z));
            }

            orderedStrokes.RemoveAll(stroke => stroke.Points.Count == 0);
            if (malformedRows > 0)
            {
                _log.LogWarning($"Skipped {malformedRows} malformed road-atlas row(s) in {path}.");
            }

            return new RoadAtlas(orderedStrokes);
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load road atlas from {path}: {exception}");
            return new RoadAtlas();
        }
    }

    public bool Save(long worldUid, RoadAtlas atlas)
    {
        string path = GetPath(worldUid);
        string temporaryPath = path + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var writer = new StreamWriter(temporaryPath, append: false))
            {
                writer.WriteLine(Header);
                foreach (RoadStroke stroke in atlas.Strokes)
                {
                    for (int index = 0; index < stroke.Points.Count; index++)
                    {
                        Vector3 point = stroke.Points[index];
                        writer.Write(stroke.Id.ToString("D", CultureInfo.InvariantCulture));
                        writer.Write('\t');
                        writer.Write(stroke.Kind);
                        writer.Write('\t');
                        writer.Write(index.ToString(CultureInfo.InvariantCulture));
                        writer.Write('\t');
                        writer.Write(point.x.ToString("R", CultureInfo.InvariantCulture));
                        writer.Write('\t');
                        writer.Write(point.y.ToString("R", CultureInfo.InvariantCulture));
                        writer.Write('\t');
                        writer.Write(point.z.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteLine("\t1");
                    }
                }
            }

            File.Copy(temporaryPath, path, overwrite: true);
            File.Delete(temporaryPath);
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not save road atlas to {path}: {exception}");
            TryDelete(temporaryPath);
            return false;
        }
    }

    private static string GetPath(long worldUid)
    {
        return Path.Combine(
            Paths.ConfigPath,
            "ConcernedCatMods",
            "ConcernedCartographer",
            worldUid.ToString(CultureInfo.InvariantCulture) + ".roads.tsv");
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; the original write error has already been logged.
        }
    }
}
