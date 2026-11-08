using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
namespace TheConcernedCat.ConcernedTeamster.Domain.Companions;

/// <summary>Gunnar's introduction and authored lines (#459). The English text
/// lives once, in <see cref="TeamsterStrings"/>' catalog, so the translator
/// template offers every line and a translator file can override any of them;
/// this type is only the view of those rows and the resolver the figure, the
/// buckle and the recruitment panel speak through.</summary>
internal static class GunnarDialogueStrings
{
    private const string Prefix = "gunnar.";
    private const string RecruitmentPrefix = "gunnar.recruit.";

    /// <summary>The dialogue rows of the catalog: every <c>gunnar.*</c> key that
    /// is not the recruitment panel's.</summary>
    public static readonly IReadOnlyDictionary<string, string> English =
        TeamsterStrings.Defaults
            .Where(row => row.Key.StartsWith(Prefix, StringComparison.Ordinal) &&
                !row.Key.StartsWith(RecruitmentPrefix, StringComparison.Ordinal))
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);

    /// <summary>The translation when one is given, else the catalog's own
    /// current text (English, or a loaded override), else the key.</summary>
    public static string Resolve(string key, Func<string, string>? translate = null)
    {
        string? value = translate?.Invoke(key);
        if (!string.IsNullOrWhiteSpace(value) && value != key) return value!;
        return TeamsterStrings.HasKey(key) ? TeamsterStrings.Get(key) : key;
    }
}
