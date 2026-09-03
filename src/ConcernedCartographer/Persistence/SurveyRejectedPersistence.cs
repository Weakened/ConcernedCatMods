using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Per-world sidecar for the survey Rejected list (RC11 blocker
/// 9): rejected observations survive restarts, so the same object never
/// re-spams the pending list in a later session. Same folder and
/// tmp-then-copy write discipline as the other sidecars.</summary>
internal sealed class SurveyRejectedPersistence
{
    private readonly ManualLogSource _log;
    private readonly Runtime.RateLimitedLog _rateLimited;

    public SurveyRejectedPersistence(ManualLogSource log)
    {
        _log = log;
        _rateLimited = new Runtime.RateLimitedLog(log, 60f);
    }

    public List<SurveyEngine.RejectedObservation> Load(long worldUid)
    {
        string path = GetPath(worldUid);
        if (!File.Exists(path))
        {
            return new List<SurveyEngine.RejectedObservation>();
        }

        try
        {
            List<SurveyEngine.RejectedObservation> entries =
                SurveyRejectedCodec.Parse(File.ReadLines(path), out int malformed);
            if (malformed > 0)
            {
                _log.LogWarning($"Skipped {malformed} malformed rejected-survey row(s) in this world's sidecar.");
            }

            return entries;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load the rejected-survey list from disk: {SafeLogText.Describe(exception)}");
            return new List<SurveyEngine.RejectedObservation>();
        }
    }

    public bool Save(long worldUid, IEnumerable<SurveyEngine.RejectedObservation> entries)
    {
        string path = GetPath(worldUid);
        string temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(temporaryPath, SurveyRejectedCodec.Serialize(entries));
            File.Copy(temporaryPath, path, overwrite: true);
            File.Delete(temporaryPath);
            return true;
        }
        catch (Exception exception)
        {
            _rateLimited.Error("survey-rejected-save", $"Could not save the rejected-survey list to disk: {SafeLogText.Describe(exception)}");
            return false;
        }
    }

    private static string GetPath(long worldUid)
    {
        return Path.Combine(
            Paths.ConfigPath,
            "ConcernedCatMods",
            "ConcernedCartographer",
            worldUid.ToString(CultureInfo.InvariantCulture) + ".survey-rejected.tsv");
    }
}
