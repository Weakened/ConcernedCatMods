using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Companions;

/// <summary>How close the resolved appearance got to what the owner asked
/// for. The distinction is the point: CC-NPC-006 says a missing stock preset
/// gets an honest fallback notice, never a silently claimed match.</summary>
internal enum AppearanceMatch
{
    /// <summary>Nothing suitable was available; the model keeps its own look.</summary>
    None = 0,

    /// <summary>The owner's exact customization-screen label was found in this
    /// build. This is the only value that means "the reference was matched".</summary>
    DisplayName = 1,

    /// <summary>The label did not match — localization missing, renamed, or a
    /// different game version — but the documented prefab id did.</summary>
    PrefabName = 2,

    /// <summary>Neither key resolved, so a same-shape stand-in was used.</summary>
    Shape = 3,

    /// <summary>Nothing preferred existed; any member of the same family beats
    /// a bald companion who looks like a bug.</summary>
    Family = 4,

    /// <summary>The player named this one themselves in configuration. An
    /// explicit choice is never overwritten by the stock default.</summary>
    Override = 5,
}

/// <summary>One thing this build actually offers for a slot: the prefab name
/// the game knows it by, and the label the customization screen shows for
/// it.</summary>
internal readonly struct AppearanceOption
{
    public AppearanceOption(string prefabName, string? displayName = null)
    {
        PrefabName = prefabName;
        DisplayName = displayName;
    }

    /// <summary>The <c>ObjectDB</c> prefab name, e.g. <c>Hair11</c>.</summary>
    public string PrefabName { get; }

    /// <summary>The localized name the customization screen shows, e.g.
    /// "Long Braid". Null when localization could not be read — which is a
    /// reason to fall back, not a reason to fail.</summary>
    public string? DisplayName { get; }

    public override string ToString()
    {
        return DisplayName == null ? PrefabName : DisplayName + " (" + PrefabName + ")";
    }
}

/// <summary>What one slot is being asked for, best key first.</summary>
internal sealed class AppearanceRequest
{
    public AppearanceRequest(
        string displayName,
        IReadOnlyList<string> prefabCandidates,
        IReadOnlyList<string> shapeFallbacks,
        string familyPrefix)
    {
        DisplayName = displayName;
        PrefabCandidates = prefabCandidates;
        ShapeFallbacks = shapeFallbacks;
        FamilyPrefix = familyPrefix;
    }

    /// <summary>The exact label read off the owner's customization screen. The
    /// authoritative key, because it is what the owner actually chose.</summary>
    public string DisplayName { get; }

    /// <summary>Documented prefab ids for that label. Secondary, because the
    /// mapping came from Jötunn's generated 1.0.7 item list rather than from
    /// this installation.</summary>
    public IReadOnlyList<string> PrefabCandidates { get; }

    /// <summary>Substrings describing the shape, used only once both exact
    /// keys have failed.</summary>
    public IReadOnlyList<string> ShapeFallbacks { get; }

    /// <summary>The slot's prefab-name prefix, for the last fallback.</summary>
    public string FamilyPrefix { get; }
}

/// <summary>One resolved slot: which prefab won, what it is called, and how
/// hard it had to look.</summary>
internal readonly struct AppearanceChoice
{
    public AppearanceChoice(string? prefabName, string? displayName, AppearanceMatch match)
    {
        PrefabName = prefabName;
        DisplayName = displayName;
        Match = match;
    }

    public string? PrefabName { get; }

    public string? DisplayName { get; }

    public AppearanceMatch Match { get; }

    /// <summary>True only when the owner's own reference label was found. The
    /// acceptance row says "matched" for this and "fallback" for everything
    /// else.</summary>
    public bool MatchesReference => Match == AppearanceMatch.DisplayName;

    public static AppearanceChoice None => new AppearanceChoice(null, null, AppearanceMatch.None);

    public override string ToString()
    {
        if (PrefabName == null)
        {
            return "<default>";
        }

        string label = DisplayName == null ? PrefabName : DisplayName + " (" + PrefabName + ")";
        return label + " [" + Match + "]";
    }
}

/// <summary>Resolves Hulgi's hair and beard against whatever this game build
/// actually has.
///
/// The stock preset names are asset-bundle data, not API, so nothing here
/// assumes one exists. The list of options is enumerated from <c>ObjectDB</c>
/// at runtime by the adapter — the same call the game's own customization
/// screen makes — and this picks from it.
///
/// CC-NPC-006 supplies the owner's exact customization-screen labels, so the
/// label is the primary key and the prefab id from Jötunn's generated 1.0.7
/// list is a secondary one. Both are looked <i>for</i>; neither is assumed.
/// The chain then degrades through shape, family and finally "no item at all",
/// because a missing preset must change how Hulgi looks and nothing else. It
/// can never fail his construction, and it certainly cannot block the
/// introduction or a player's tools.</summary>
internal static class AppearancePlan
{
    /// <summary>Hulgi's hair, from the owner's September 15 reference: the
    /// customization screen's "Long Braid".</summary>
    public static readonly AppearanceRequest HulgiHair = new AppearanceRequest(
        displayName: "Long Braid",
        prefabCandidates: new[] { "Hair11" },
        shapeFallbacks: new[] { "braid", "long", "sidetail", "swept", "short" },
        familyPrefix: "Hair");

    /// <summary>Hulgi's tunic, from the owner's September 15 wardrobe note:
    /// the vanilla "Rag tunic". Clothing only — nothing here is an item he
    /// owns, carries, or can be given, and it changes no stat.</summary>
    public static readonly AppearanceRequest HulgiChest = new AppearanceRequest(
        displayName: "Rag tunic",
        prefabCandidates: new[] { "ArmorRagsChest" },
        shapeFallbacks: new[] { "rag", "tunic" },
        familyPrefix: "Armor");

    /// <summary>Hulgi's trousers: the vanilla "Leather pants".</summary>
    public static readonly AppearanceRequest HulgiLegs = new AppearanceRequest(
        displayName: "Leather pants",
        prefabCandidates: new[] { "ArmorLeatherLegs" },
        shapeFallbacks: new[] { "leather", "pants", "trousers" },
        familyPrefix: "Armor");

    /// <summary>Hulgi's beard: "Handlebar". This replaces the earlier mutton
    /// chops prose, which predates the owner's screenshots.</summary>
    public static readonly AppearanceRequest HulgiBeard = new AppearanceRequest(
        displayName: "Handlebar",
        prefabCandidates: new[] { "Beard26" },
        shapeFallbacks: new[] { "handlebar", "moustache", "mustache", "braid", "thick", "short" },
        familyPrefix: "Beard");

    /// <summary>Picks one slot.</summary>
    /// <param name="available">Every option this build offers for the slot, as
    /// enumerated at runtime.</param>
    /// <param name="request">What was asked for.</param>
    /// <param name="playerOverride">A prefab name or label the player set in
    /// configuration. An override that names something this build does not
    /// have falls through to the stock chain rather than leaving him bald.</param>
    public static AppearanceChoice Choose(
        IReadOnlyList<AppearanceOption>? available,
        AppearanceRequest request,
        string? playerOverride = null)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (available == null || available.Count == 0)
        {
            return AppearanceChoice.None;
        }

        if (!string.IsNullOrWhiteSpace(playerOverride))
        {
            AppearanceChoice chosen = MatchExact(available, playerOverride!, AppearanceMatch.Override);
            if (chosen.PrefabName != null)
            {
                return chosen;
            }
        }

        AppearanceChoice byLabel = MatchExact(available, request.DisplayName, AppearanceMatch.DisplayName);
        if (byLabel.PrefabName != null)
        {
            return byLabel;
        }

        foreach (string candidate in request.PrefabCandidates)
        {
            AppearanceChoice byPrefab = MatchExact(available, candidate, AppearanceMatch.PrefabName);
            if (byPrefab.PrefabName != null)
            {
                return byPrefab;
            }
        }

        foreach (string fragment in request.ShapeFallbacks)
        {
            AppearanceChoice byShape = MatchContaining(available, fragment);
            if (byShape.PrefabName != null)
            {
                return byShape;
            }
        }

        // Nothing preferred. Any member of the same family still looks
        // deliberate, where no item at all can look like a bug.
        foreach (AppearanceOption option in available)
        {
            if (!string.IsNullOrEmpty(option.PrefabName) &&
                option.PrefabName.StartsWith(request.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new AppearanceChoice(
                    option.PrefabName, option.DisplayName, AppearanceMatch.Family);
            }
        }

        return AppearanceChoice.None;
    }

    /// <summary>Filters a whole option list down to one slot's family. The
    /// adapter hands over every option it can see; this is what makes "the hair
    /// list" out of it without assuming the game groups them for us.</summary>
    public static IReadOnlyList<AppearanceOption> FilterFamily(
        IReadOnlyList<AppearanceOption>? all, string familyPrefix)
    {
        var family = new List<AppearanceOption>();
        if (all == null || string.IsNullOrEmpty(familyPrefix))
        {
            return family;
        }

        foreach (AppearanceOption option in all)
        {
            if (!string.IsNullOrEmpty(option.PrefabName) &&
                option.PrefabName.StartsWith(familyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                family.Add(option);
            }
        }

        family.Sort((left, right) =>
            string.Compare(left.PrefabName, right.PrefabName, StringComparison.OrdinalIgnoreCase));
        return family;
    }

    /// <summary>Wraps bare prefab names as options with no label. Used when the
    /// adapter could read the prefab table but not localization.</summary>
    public static IReadOnlyList<AppearanceOption> FromPrefabNames(IReadOnlyList<string>? names)
    {
        var options = new List<AppearanceOption>();
        if (names == null)
        {
            return options;
        }

        foreach (string name in names)
        {
            if (!string.IsNullOrEmpty(name))
            {
                options.Add(new AppearanceOption(name));
            }
        }

        return options;
    }

    private static AppearanceChoice MatchExact(
        IReadOnlyList<AppearanceOption> available, string key, AppearanceMatch match)
    {
        string wanted = key.Trim();
        foreach (AppearanceOption option in available)
        {
            if (Equal(option.DisplayName, wanted) || Equal(option.PrefabName, wanted))
            {
                return new AppearanceChoice(option.PrefabName, option.DisplayName, match);
            }
        }

        return AppearanceChoice.None;
    }

    private static AppearanceChoice MatchContaining(
        IReadOnlyList<AppearanceOption> available, string fragment)
    {
        foreach (AppearanceOption option in available)
        {
            if (Contains(option.DisplayName, fragment) || Contains(option.PrefabName, fragment))
            {
                return new AppearanceChoice(
                    option.PrefabName, option.DisplayName, AppearanceMatch.Shape);
            }
        }

        return AppearanceChoice.None;
    }

    private static bool Equal(string? value, string wanted)
    {
        return !string.IsNullOrEmpty(value) &&
            string.Equals(value!.Trim(), wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string? value, string fragment)
    {
        return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(fragment) &&
            value!.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
