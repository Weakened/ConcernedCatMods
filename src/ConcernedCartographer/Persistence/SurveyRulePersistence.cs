using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Loads the shareable survey-rules file, writing the starter set
/// on first use. The file is the import/export format: plain patterns and
/// suggestions, no machine paths or secrets. An UNTOUCHED starter file
/// from an earlier release (the sparse pre-RC8 set, the RC8/RC9 set, the
/// v1.0/v1.1.0 set that predates the issue #258 dungeon identities, or the
/// v1.0.3-v1.2.2 set that predates the issue #385 OreMines identities) is
/// upgraded in place to the current starter set; any file the player
/// edited never matches and is never modified. The recognition itself is
/// <see cref="SurveyStarterUpgrade"/>, in the domain, under test.</summary>
internal sealed class SurveyRulePersistence
{
    private readonly ManualLogSource _log;

    public SurveyRulePersistence(ManualLogSource log)
    {
        _log = log;
    }

    public static string RulePath =>
        CartographerPaths.InConfig("survey-rules.tsv");

    public SurveyRuleSet LoadOrCreate()
    {
        try
        {
            if (!File.Exists(RulePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RulePath)!);
                File.WriteAllLines(RulePath, SurveyRuleSet.Default().Serialize());
                _log.LogInfo("Wrote the starter survey rules to survey-rules.tsv.");
            }
            else
            {
                if (SurveyStarterUpgrade.ShouldUpgrade(File.ReadAllLines(RulePath)))
                {
                    File.WriteAllLines(RulePath, SurveyRuleSet.Default().Serialize());
                    _log.LogInfo(
                        "Upgraded the untouched starter survey rules (survey-rules.tsv) to the current starter set " +
                        "(edited files are never touched).");
                }
            }

            SurveyRuleSet rules = SurveyRuleSet.Parse(File.ReadAllLines(RulePath), out int malformed);
            if (malformed > 0)
            {
                _log.LogWarning($"Skipped {malformed} malformed survey rule(s) in survey-rules.tsv.");
            }

            return rules;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not load survey rules; the survey stays inactive: {SafeLogText.Describe(exception)}");
            return new SurveyRuleSet();
        }
    }

    /// <summary>Writes the current rule set back to the shareable file
    /// (RC11 blocker 10: the Survey UI edits rules; the file remains the
    /// import/export format).</summary>
    public bool Save(SurveyRuleSet rules)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RulePath)!);
            File.WriteAllLines(RulePath, rules.Serialize());
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not save the survey rules to disk: {SafeLogText.Describe(exception)}");
            return false;
        }
    }
}
