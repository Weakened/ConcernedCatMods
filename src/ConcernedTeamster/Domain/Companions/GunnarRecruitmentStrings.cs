using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;
/// <summary>The recruitment panel's resolver over the catalog rows.</summary>
internal static class GunnarRecruitmentStrings
{
    internal const string Status="status",Choose="choose",Confirm="confirm",Cancel="cancel",Offer="offer";
    /// <summary>The recruitment panel's rows of the catalog (#459 moved the
    /// English text into TeamsterStrings, its one home).</summary>
    internal static readonly IReadOnlyDictionary<string,string> English=
        TeamsterStrings.Defaults.Where(row=>row.Key.StartsWith("gunnar.recruit.",StringComparison.Ordinal))
            .ToDictionary(row=>row.Key,row=>row.Value,StringComparer.Ordinal);
    internal static string Resolve(string key,Func<string,string>? translate=null)
    {
        string? value=translate?.Invoke(key);
        return !string.IsNullOrWhiteSpace(value)&&value!=key?value!:
            TeamsterStrings.HasKey(key)?TeamsterStrings.Get(key):key;
    }
}
