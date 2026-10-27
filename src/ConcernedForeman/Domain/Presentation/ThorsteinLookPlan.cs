using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedForeman.Domain.Presentation;

/// <summary>How close a resolved slot got to what the owner asked for. #298 is
/// explicit that a missing stock preset gets an honest fallback notice, never a
/// silently claimed match, so only <see cref="DisplayName"/> means "matched".
/// </summary>
internal enum LookMatch
{
    /// <summary>Nothing suitable. The figure keeps the source model's own look.</summary>
    None = 0,

    /// <summary>The owner's exact customization-screen label was found in this
    /// build. The only value that means the reference was matched.</summary>
    DisplayName = 1,

    /// <summary>The label was not found, but the documented prefab id was.</summary>
    PrefabName = 2,

    /// <summary>Neither key resolved, so a same-shape stand-in was used.</summary>
    Shape = 3,

    /// <summary>Nothing preferred existed, so any member of the family was used.</summary>
    Family = 4,

    /// <summary>The player named this one in configuration. An explicit choice
    /// is never overwritten by the stock default.</summary>
    Override = 5,
}

/// <summary>One thing this build offers for a slot: the prefab name the game
/// knows it by, and the label its customization screen shows.</summary>
internal readonly struct LookOption
{
    internal LookOption(string prefabName, string? displayName = null)
    {
        PrefabName = prefabName ?? string.Empty;
        DisplayName = displayName;
    }

    /// <summary>The item table's prefab name, e.g. <c>Hair32</c>.</summary>
    internal string PrefabName { get; }

    /// <summary>The localized label, e.g. "Mullet". Null when localization
    /// could not be read, which is a reason to fall back and not to fail.</summary>
    internal string? DisplayName { get; }

    public override string ToString() =>
        DisplayName == null ? PrefabName : DisplayName + " (" + PrefabName + ")";
}

/// <summary>What one slot asks for, best key first.</summary>
internal sealed class LookRequest
{
    internal LookRequest(
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
    /// authoritative key, because it is what the owner chose.</summary>
    internal string DisplayName { get; }

    /// <summary>Documented prefab ids for that label. Secondary: they come from
    /// Jötunn's generated 1.0.7 item list, not from this installation.</summary>
    internal IReadOnlyList<string> PrefabCandidates { get; }

    /// <summary>Substrings of the shape, tried once both exact keys fail.</summary>
    internal IReadOnlyList<string> ShapeFallbacks { get; }

    /// <summary>The slot's prefab-name prefix, for the last fallback.</summary>
    internal string FamilyPrefix { get; }
}

/// <summary>One resolved slot.</summary>
internal readonly struct LookChoice
{
    internal LookChoice(string? prefabName, string? displayName, LookMatch match)
    {
        PrefabName = prefabName;
        DisplayName = displayName;
        Match = match;
    }

    internal string? PrefabName { get; }

    internal string? DisplayName { get; }

    internal LookMatch Match { get; }

    /// <summary>Only the owner's own label counts as a match.</summary>
    internal bool MatchesReference => Match == LookMatch.DisplayName;

    internal static LookChoice None => new LookChoice(null, null, LookMatch.None);

    public override string ToString()
    {
        if (PrefabName == null)
        {
            return "<none>";
        }

        string label = DisplayName == null ? PrefabName : DisplayName + " (" + PrefabName + ")";
        return label + " [" + Match + "]";
    }
}

/// <summary>Thorstein's look, from the owner's September 15 character
/// reference (#298, <c>docs/companions/APPEARANCE_REFERENCE.md</c>), and the
/// rule that resolves it against whatever this game build actually has.
///
/// <b>The reference.</b> Hair "Mullet", beard "Mustache". This replaces the
/// earlier short-hair and short-beard placeholder. The complexion is deeper and
/// tanned, substantially less blond than Hulgi's
/// (<see cref="ThorsteinLookColour"/>).
///
/// <b>Nothing is assumed to exist.</b> Preset names are asset data, not API.
/// The options are enumerated from the game's own item table at run time, the
/// same query its customization screen makes, and the owner's label is the
/// primary key. The Jötunn 1.0.7 id (<c>Hair32</c>, <c>Beard22</c>) is the
/// secondary one, then shape, then family, then nothing. A missing preset
/// changes how he looks and nothing else.
///
/// <b>Clothes are this build's proposal, not the owner's.</b> The reference
/// screenshots lock hair, beard and colouring only, and #298 says clothes are
/// not locked by them but must be safe and modest. A carpenter's working
/// leathers (the vanilla leather tunic and trousers) are proposed here.
/// <c>Thorstein/ChestGarment</c> and <c>LegsGarment</c> override them, and the
/// owner may replace the proposal.
///
/// The algorithm is the one Hulgi's <c>AppearancePlan</c> uses in Concerned
/// Cartographer, copied rather than referenced: products never reference each
/// other (D2).</summary>
internal static class ThorsteinLookPlan
{
    internal static readonly LookRequest Hair = new LookRequest(
        displayName: "Mullet",
        prefabCandidates: new[] { "Hair32" },
        shapeFallbacks: new[] { "mullet", "short", "swept" },
        familyPrefix: "Hair");

    internal static readonly LookRequest Beard = new LookRequest(
        displayName: "Mustache",
        prefabCandidates: new[] { "Beard22" },
        shapeFallbacks: new[] { "mustache", "moustache", "short" },
        familyPrefix: "Beard");

    /// <summary>Proposed, not owner-locked: see the class remarks.</summary>
    internal static readonly LookRequest Chest = new LookRequest(
        displayName: "Leather tunic",
        prefabCandidates: new[] { "ArmorLeatherChest" },
        shapeFallbacks: new[] { "leather", "tunic" },
        familyPrefix: "Armor");

    /// <summary>Proposed, not owner-locked: see the class remarks.</summary>
    internal static readonly LookRequest Legs = new LookRequest(
        displayName: "Leather pants",
        prefabCandidates: new[] { "ArmorLeatherLegs" },
        shapeFallbacks: new[] { "leather", "pants", "trousers" },
        familyPrefix: "Armor");

    /// <summary>Picks one slot.</summary>
    /// <param name="available">What this build offers for the slot.</param>
    /// <param name="request">What was asked for.</param>
    /// <param name="playerOverride">A prefab name or label from configuration.
    /// One that names something this build does not have falls through to the
    /// stock chain rather than leaving him bald.</param>
    internal static LookChoice Choose(
        IReadOnlyList<LookOption>? available, LookRequest request, string? playerOverride = null)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (available == null || available.Count == 0)
        {
            return LookChoice.None;
        }

        if (!string.IsNullOrWhiteSpace(playerOverride))
        {
            LookChoice chosen = MatchExact(available, playerOverride!, LookMatch.Override);
            if (chosen.PrefabName != null)
            {
                return chosen;
            }
        }

        LookChoice byLabel = MatchExact(available, request.DisplayName, LookMatch.DisplayName);
        if (byLabel.PrefabName != null)
        {
            return byLabel;
        }

        foreach (string candidate in request.PrefabCandidates)
        {
            LookChoice byPrefab = MatchExact(available, candidate, LookMatch.PrefabName);
            if (byPrefab.PrefabName != null)
            {
                return byPrefab;
            }
        }

        foreach (string fragment in request.ShapeFallbacks)
        {
            foreach (LookOption option in available)
            {
                if (Contains(option.DisplayName, fragment) || Contains(option.PrefabName, fragment))
                {
                    return new LookChoice(option.PrefabName, option.DisplayName, LookMatch.Shape);
                }
            }
        }

        foreach (LookOption option in available)
        {
            if (option.PrefabName.StartsWith(request.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new LookChoice(option.PrefabName, option.DisplayName, LookMatch.Family);
            }
        }

        return LookChoice.None;
    }

    private static LookChoice MatchExact(IReadOnlyList<LookOption> available, string key, LookMatch match)
    {
        string wanted = key.Trim();
        foreach (LookOption option in available)
        {
            if (Equal(option.DisplayName, wanted) || Equal(option.PrefabName, wanted))
            {
                return new LookChoice(option.PrefabName, option.DisplayName, match);
            }
        }

        return LookChoice.None;
    }

    private static bool Equal(string? value, string wanted) =>
        !string.IsNullOrEmpty(value)
        && string.Equals(value!.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string fragment) =>
        !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(fragment)
        && value!.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
