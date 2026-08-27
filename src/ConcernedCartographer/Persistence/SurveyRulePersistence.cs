using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Loads the shareable survey-rules file, writing the conservative
/// starter set on first use. The file is the import/export format: plain
/// patterns and suggestions, no machine paths or secrets.</summary>
internal sealed class SurveyRulePersistence
{
    private readonly ManualLogSource _log;

    public SurveyRulePersistence(ManualLogSource log)
    {
        _log = log;
    }

    public static string RulePath =>
        Path.Combine(Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "survey-rules.tsv");

    public SurveyRuleSet LoadOrCreate()
    {
        try
        {
            if (!File.Exists(RulePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RulePath)!);
                File.WriteAllLines(RulePath, SurveyRuleSet.Default().Serialize());
                _log.LogInfo($"Wrote the starter survey rules to {RulePath}.");
            }

            SurveyRuleSet rules = SurveyRuleSet.Parse(File.ReadAllLines(RulePath), out int malformed);
            if (malformed > 0)
            {
                _log.LogWarning($"Skipped {malformed} malformed survey rule(s) in {RulePath}.");
            }

            return rules;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load survey rules; the survey stays inactive: {exception}");
            return new SurveyRuleSet();
        }
    }
}
