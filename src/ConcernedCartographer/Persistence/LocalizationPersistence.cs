using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Writes the translator template on first run and loads
/// `cartographer-strings.tsv` overrides when present. One file, any
/// language — translators fill the template and rename it.</summary>
internal static class LocalizationPersistence
{
    public static string OverridePath =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "cartographer-strings.tsv");

    public static string TemplatePath =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "cartographer-strings-template.tsv");

    public static void Initialize(ManualLogSource log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath)!);
            File.WriteAllLines(TemplatePath, AtlasStrings.TranslatorTemplate());

            if (File.Exists(OverridePath))
            {
                Dictionary<string, string> overrides =
                    AtlasStrings.ParseOverrides(File.ReadAllLines(OverridePath), out int skipped);
                AtlasStrings.LoadOverrides(overrides);
                log.LogInfo($"Loaded {overrides.Count} translated string(s) from cartographer-strings.tsv" +
                    (skipped > 0 ? $" ({skipped} row(s) skipped)." : "."));
            }
        }
        catch (Exception exception)
        {
            log.LogWarning($"Localization stayed at English defaults: {SafeLogText.Brief(exception)}");
        }
    }
}
