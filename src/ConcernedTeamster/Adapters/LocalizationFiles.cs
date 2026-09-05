using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Writes the translator template on first run and loads
/// `teamster-strings.tsv` overrides when present (CT-032), mirroring the
/// one-file-any-language flow proven in Concerned Cartographer. All parsing
/// and fallback live in the pure <see cref="TeamsterStrings"/> catalog; this
/// adapter only does file IO under the mod's own config path and logs one
/// summary line. Never throws into startup — a translation problem must not
/// stop the plugin loading.</summary>
public static class LocalizationFiles
{
    public static string OverridePath =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedTeamster", "teamster-strings.tsv");

    public static string TemplatePath =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedTeamster", "teamster-strings-template.tsv");

    public static void Initialize(ManualLogSource log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath)!);
            File.WriteAllLines(TemplatePath, new List<string>(TeamsterStrings.TranslatorTemplate()));

            if (File.Exists(OverridePath))
            {
                Dictionary<string, string> overrides =
                    TeamsterStrings.ParseOverrides(File.ReadAllLines(OverridePath), out int skipped);
                TeamsterStrings.LoadOverrides(overrides);
                log.LogInfo(
                    $"Loaded {overrides.Count} translated string(s) from teamster-strings.tsv" +
                    (skipped > 0 ? $" ({skipped} row(s) skipped — malformed, unknown key, or placeholder mismatch)." : "."));
            }
        }
        catch (System.Exception exception)
        {
            log.LogWarning($"Localization overrides unavailable this session; using English: {exception.GetType().Name}");
        }
    }
}
