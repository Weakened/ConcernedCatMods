using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Reporting;
using TheConcernedCat.ConcernedCartographer.Storage;

namespace TheConcernedCat.ConcernedCartographer.Persistence;

/// <summary>Loads the shareable survey-rules file, writing the starter set
/// on first use. The file is the import/export format: plain patterns and
/// suggestions, no machine paths or secrets. An UNTOUCHED starter file
/// from an earlier release (the sparse pre-RC8 set, the RC8/RC9 set, the
/// v1.0/v1.1.0 set that predates the issue #258 dungeon identities, or the
/// v1.0.3-v1.2.2 set that predates the issue #385 OreMines identities) is
/// upgraded in place to the current starter set; any file the player
/// edited never matches and is never modified. The recognition itself is
/// <see cref="SurveyStarterUpgrade"/>, in the domain, under test.
///
/// <b>This class no longer writes the file (#366).</b> The upgrade is
/// <see cref="SurveyRuleFile"/>'s, in the domain, because the rewrite and
/// the fresh-install probe's "was this here before us" question are one
/// behaviour and this layer cannot be compiled into a test: for the whole
/// life of the bug there was no test that could run both halves. Startup IO
/// for this file belongs there; what belongs here is the log wording and the
/// one place a failure turns into an inactive survey rather than a crash.
/// <see cref="Save"/> stays here and deliberately does move the timestamp —
/// it only runs when the player edits their own rules, which is exactly
/// what that timestamp means.</summary>
internal sealed class SurveyRulePersistence
{
    private readonly ManualLogSource _log;

    public SurveyRulePersistence(ManualLogSource log)
    {
        _log = log;
    }

    public static string RulePath =>
        CartographerPaths.InRoot("survey-rules.tsv");

    public SurveyRuleSet LoadOrCreate()
    {
        try
        {
            SurveyRuleFile.Outcome outcome = SurveyRuleFile.LoadOrCreate(
                RulePath, out SurveyRuleSet rules, out int malformed);

            switch (outcome)
            {
                case SurveyRuleFile.Outcome.Created:
                    _log.LogInfo("Wrote the starter survey rules to survey-rules.tsv.");
                    break;
                case SurveyRuleFile.Outcome.Upgraded:
                    _log.LogInfo(
                        "Upgraded the untouched starter survey rules (survey-rules.tsv) to the current starter set " +
                        "(edited files are never touched; the file's own modification time is left as it was, so " +
                        "this upgrade cannot make a returning player look like a new one).");
                    break;
                case SurveyRuleFile.Outcome.UpgradedButTimestampMoved:
                    // #366: the probe reads this file's modification time as
                    // "was this player here before us". Say so plainly - this
                    // is the one path where the upgrade can cost somebody the
                    // recognition of being a returning user, and a silent
                    // false negative is worse than a noisy log.
                    _log.LogWarning(
                        "Upgraded the untouched starter survey rules (survey-rules.tsv), but this system would not " +
                        "let the file's original modification time be restored. The rules are correct. If Hulgi " +
                        "introduces himself as though you were new here, that is why, and your tools are not lost.");
                    break;
            }

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
