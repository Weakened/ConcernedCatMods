using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Profile-level saved-view storage (views are user preferences,
/// not world data): one escaped-TSV file, atomic writes, malformed rows
/// skipped.</summary>
internal sealed class SavedViewPersistence
{
    private readonly ManualLogSource _log;

    public SavedViewPersistence(ManualLogSource log)
    {
        _log = log;
    }

    public SavedViewStore Load()
    {
        string path = GetPath();
        try
        {
            if (!File.Exists(path))
            {
                return new SavedViewStore();
            }

            SavedViewStore store = SavedViewStore.Parse(File.ReadAllLines(path), out int malformed);
            if (malformed > 0)
            {
                _log.LogWarning($"Skipped {malformed} malformed saved-view row(s) in the saved-views file.");
            }

            return store;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load saved views: {SafeLogText.Describe(exception)}");
            return new SavedViewStore();
        }
    }

    public void Save(SavedViewStore store)
    {
        if (!store.IsDirty)
        {
            return;
        }

        string path = GetPath();
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(temporary, store.Serialize());
            File.Copy(temporary, path, overwrite: true);
            File.Delete(temporary);
            store.MarkClean();
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not save views: {SafeLogText.Describe(exception)}");
        }
    }

    private static string GetPath()
    {
        return Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "views.tsv");
    }
}
