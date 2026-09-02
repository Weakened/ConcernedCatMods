using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Loads the shareable survey-rules file, writing the starter set
/// on first use. The file is the import/export format: plain patterns and
/// suggestions, no machine paths or secrets. An UNTOUCHED starter file
/// from an earlier RC (the sparse pre-RC8 set or the RC8/RC9 set) is
/// upgraded in place to the current broadened starter set; any file the
/// player edited never matches and is never modified.</summary>
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
            else
            {
                string current = Normalize(File.ReadAllLines(RulePath));
                if (current == Normalize(SurveyRuleSet.LegacyStarterSet().Serialize()) ||
                    current == Normalize(SurveyRuleSet.Rc8StarterSet().Serialize()))
                {
                    File.WriteAllLines(RulePath, SurveyRuleSet.Default().Serialize());
                    _log.LogInfo(
                        $"Upgraded the untouched starter survey rules in {RulePath} to the v1 starter set " +
                        "(edited files are never touched).");
                }
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

    private static string Normalize(System.Collections.Generic.IEnumerable<string> lines)
    {
        var builder = new System.Text.StringBuilder();
        foreach (string line in lines)
        {
            builder.Append(line.TrimEnd()).Append('\n');
        }

        return builder.ToString();
    }
}
