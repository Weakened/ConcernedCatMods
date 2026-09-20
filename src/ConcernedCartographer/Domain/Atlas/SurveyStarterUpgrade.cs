using System.Collections.Generic;
using System.Text;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The one decision behind "upgrade an untouched starter file,
/// never touch an edited one".</summary>
/// <remarks>It lives in the domain so it is provable: the rules file is a
/// player-owned document, and the only thing standing between a shipped
/// rule addition and silently overwriting somebody's hand-edited file is
/// this exact-content comparison. Every previously shipped starter set is
/// kept verbatim in <see cref="SurveyRuleSet"/>; a file that is not
/// byte-for-byte one of them (ignoring trailing whitespace per line) is
/// treated as the player's own work and left alone — including a file
/// that merely disabled a single rule, reordered rows, or added one.
///
/// Issue #385 added the fourth snapshot, <see
/// cref="SurveyRuleSet.V103StarterSet"/>: the set shipped from v1.0.3
/// through v1.2.2, whose owners should receive the documented OreMines
/// mine identities without being asked to edit anything.</remarks>
internal static class SurveyStarterUpgrade
{
    /// <summary>True when the on-disk rules file is an untouched starter
    /// file from an earlier release and therefore safe to rewrite with the
    /// current starter set. False for a file the player edited, for a file
    /// that is already the current starter set (nothing to do), and for
    /// anything unrecognized.</summary>
    public static bool ShouldUpgrade(IEnumerable<string> currentFileLines)
    {
        string current = Normalize(currentFileLines);
        if (current == Normalize(SurveyRuleSet.Default().Serialize()))
        {
            return false;
        }

        return current == Normalize(SurveyRuleSet.LegacyStarterSet().Serialize()) ||
               current == Normalize(SurveyRuleSet.Rc8StarterSet().Serialize()) ||
               current == Normalize(SurveyRuleSet.V1StarterSet().Serialize()) ||
               current == Normalize(SurveyRuleSet.V103StarterSet().Serialize());
    }

    /// <summary>Line-wise normalization: trailing whitespace and the line
    /// ending are not content, everything else is.</summary>
    public static string Normalize(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (string line in lines)
        {
            builder.Append(line.TrimEnd()).Append('\n');
        }

        return builder.ToString();
    }
}
