using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>How close the resolved appearance got to what was asked for.</summary>
internal enum AppearanceMatch
{
    /// <summary>Nothing suitable was available; the model keeps its own look.</summary>
    None = 0,

    /// <summary>A named preference was found.</summary>
    Preferred = 1,

    /// <summary>A named preference was absent and a same-family alternative
    /// was used instead.</summary>
    Alternate = 2,
}

/// <summary>One resolved slot: which prefab name won, and how hard it had to
/// look.</summary>
internal readonly struct AppearanceChoice
{
    public AppearanceChoice(string? prefabName, AppearanceMatch match)
    {
        PrefabName = prefabName;
        Match = match;
    }

    public string? PrefabName { get; }

    public AppearanceMatch Match { get; }

    public static AppearanceChoice None => new AppearanceChoice(null, AppearanceMatch.None);

    public override string ToString()
    {
        return PrefabName == null ? "<default>" : PrefabName + " (" + Match + ")";
    }
}

/// <summary>Resolves Hulgi's hair and beard against whatever this game build
/// actually has.
///
/// The audit is explicit that the stock preset names are asset-bundle data and
/// not API, and it refused to guess them. So this takes the live list of prefab
/// names — enumerated from <c>ObjectDB</c> at runtime by the adapter — and picks
/// from it. Names in the preference lists are things to look <i>for</i>; none
/// of them is assumed to exist.
///
/// The fallback chain is deliberately three-deep and ends in "no item at all",
/// because a missing preset must change how Hulgi looks and nothing else. It
/// can never fail his construction, and it certainly cannot block the
/// introduction or a player's tools.</summary>
internal static class AppearancePlan
{
    /// <summary>Beard preferences: mutton chops first, then a moustache, then
    /// anything bearded. Matched case-insensitively as substrings, because the
    /// game's own spelling of these is exactly what the audit could not
    /// establish.</summary>
    public static readonly string[] BeardPreferences =
    {
        "muttonchops",
        "mutton",
        "braid",
        "thick",
        "short",
    };

    /// <summary>Hair preferences. Strawberry blond is a colour applied
    /// separately, so these describe shape only: something short and tidy,
    /// suiting a cartographer rather than a raider.</summary>
    public static readonly string[] HairPreferences =
    {
        "short",
        "swept",
        "sidetail",
        "braided",
        "long",
    };

    /// <summary>The colour applied through <c>SetHairColor</c>. Documented as a
    /// tuning value to be confirmed on screen, not a fact: the audit could not
    /// supply the vector that reads as strawberry blond, so this is a starting
    /// point that a single edit can move.</summary>
    public const float StrawberryBlondR = 0.85f;
    public const float StrawberryBlondG = 0.55f;
    public const float StrawberryBlondB = 0.30f;

    /// <summary>Picks one slot.</summary>
    /// <param name="available">Every prefab name this build offers for the
    /// slot, as enumerated at runtime.</param>
    /// <param name="preferences">Substrings to look for, best first.</param>
    /// <param name="familyPrefix">The slot's prefab-name prefix, used for the
    /// second fallback step: any member of the same family beats nothing.</param>
    public static AppearanceChoice Choose(
        IReadOnlyList<string>? available,
        IReadOnlyList<string> preferences,
        string familyPrefix)
    {
        if (available == null || available.Count == 0)
        {
            return AppearanceChoice.None;
        }

        foreach (string preference in preferences)
        {
            string? hit = FindContaining(available, preference);
            if (hit != null)
            {
                return new AppearanceChoice(hit, AppearanceMatch.Preferred);
            }
        }

        // Nothing preferred. Any member of the same family still looks
        // deliberate, where no item at all can look like a bug.
        string? family = FindStartingWith(available, familyPrefix);
        if (family != null)
        {
            return new AppearanceChoice(family, AppearanceMatch.Alternate);
        }

        return AppearanceChoice.None;
    }

    /// <summary>Filters a whole prefab list down to one slot's family. The
    /// adapter hands over every name it can see; this is what makes "the hair
    /// list" out of it without assuming the game groups them for us.</summary>
    public static IReadOnlyList<string> FilterFamily(
        IReadOnlyList<string>? allPrefabNames, string familyPrefix)
    {
        var family = new List<string>();
        if (allPrefabNames == null || string.IsNullOrEmpty(familyPrefix))
        {
            return family;
        }

        foreach (string name in allPrefabNames)
        {
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                family.Add(name);
            }
        }

        family.Sort(StringComparer.OrdinalIgnoreCase);
        return family;
    }

    private static string? FindContaining(IReadOnlyList<string> available, string fragment)
    {
        foreach (string name in available)
        {
            if (!string.IsNullOrEmpty(name) &&
                name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return name;
            }
        }

        return null;
    }

    private static string? FindStartingWith(IReadOnlyList<string> available, string prefix)
    {
        foreach (string name in available)
        {
            if (!string.IsNullOrEmpty(name) &&
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }
}
