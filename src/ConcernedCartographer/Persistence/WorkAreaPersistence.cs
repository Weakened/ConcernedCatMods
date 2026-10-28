using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>#296: the per-world sidecar for drawn work areas. Same folder and
/// tmp-then-copy write discipline as the other sidecars, and a member of
/// CartographerWorldSidecars, so backup, restore and the
/// support report cover it.
///
/// A file written by a newer build (an unknown header) is loaded as nothing
/// and is then never overwritten this session: a guess at its format, or an
/// empty save over it, would destroy areas the player drew.</summary>
internal sealed class WorkAreaPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;
    private readonly HashSet<long> _readOnlyWorlds = new();

    public WorkAreaPersistence(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public List<WorkArea> Load(long worldUid)
    {
        string path = GetPath(worldUid);
        if (!File.Exists(path))
        {
            return new List<WorkArea>();
        }

        try
        {
            List<WorkArea> areas = WorkAreaCodec.Parse(File.ReadLines(path), out int malformed, out bool unsupported);
            if (unsupported)
            {
                _readOnlyWorlds.Add(worldUid);
                _log.LogWarning(
                    "This world's work-area file was written by a newer version; it is left untouched and no work areas are shown.");
                return new List<WorkArea>();
            }

            if (malformed > 0)
            {
                _log.LogWarning($"Skipped {malformed} unreadable work-area row(s) in this world's sidecar; they are not treated as any other area.");
            }

            return areas;
        }
        catch (Exception exception)
        {
            // Unreadable right now (a lock, an antivirus scan): do not save an
            // empty list over it later this session.
            _readOnlyWorlds.Add(worldUid);
            _log.LogError($"Could not load this world's work areas: {SafeLogText.Describe(exception)}");
            return new List<WorkArea>();
        }
    }

    public bool IsReadOnly(long worldUid) => _readOnlyWorlds.Contains(worldUid);

    public bool Save(long worldUid, IEnumerable<WorkArea> areas)
    {
        if (_readOnlyWorlds.Contains(worldUid))
        {
            return false;
        }

        string path = GetPath(worldUid);
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(temporaryPath, WorkAreaCodec.Serialize(areas));
            File.Copy(temporaryPath, path, overwrite: true);
            File.Delete(temporaryPath);
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Error("work-area-save", $"Could not save this world's work areas: {SafeLogText.Describe(exception)}");
            return false;
        }
    }

    private static string GetPath(long worldUid)
    {
        return CartographerPaths.InRoot(worldUid.ToString(CultureInfo.InvariantCulture) + ".work-areas.tsv");
    }
}
