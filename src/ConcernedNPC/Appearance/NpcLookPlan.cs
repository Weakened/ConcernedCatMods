using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedNPC.Appearance;

/// <summary>How close a resolved slot got to what was asked for. A missing
/// stock preset gets an honest fallback notice, never a silently claimed match,
/// so only <see cref="DisplayName"/> and <see cref="Token"/> mean "matched".
///
/// <b>The numbers are durable</b> in the sense that a role may log or compare
/// them; a new value is appended, never inserted.</summary>
public enum NpcLookMatch
{
    /// <summary>Nothing suitable. The figure keeps the source model's own look.</summary>
    None = 0,

    /// <summary>The exact customization-screen label was found in this build.</summary>
    DisplayName = 1,

    /// <summary>The label was not found, but a documented prefab id was.</summary>
    PrefabName = 2,

    /// <summary>Neither key resolved, so a same-shape stand-in was used.</summary>
    Shape = 3,

    /// <summary>Nothing preferred existed, so any member of the family was used.</summary>
    Family = 4,

    /// <summary>The player named this one in configuration. An explicit choice
    /// is never overwritten by the stock default.</summary>
    Override = 5,

    /// <summary>The item's own localization token (for example
    /// <c>$customization_hair12</c>) was found. That token is the key the
    /// label is looked up by, so it identifies the same choice in every
    /// language, and it counts as a match.</summary>
    Token = 6,
}

/// <summary>One thing this build offers for a slot: the prefab name the game
/// knows it by, the label its customization screen shows, and the localization
/// token that label came from.</summary>
public readonly struct NpcLookOption
{
    public NpcLookOption(string prefabName, string? displayName = null, string? token = null)
    {
        PrefabName = prefabName ?? string.Empty;
        DisplayName = displayName;
        Token = token;
    }

    /// <summary>The item table's prefab name.</summary>
    public string PrefabName { get; }

    /// <summary>The localized label. Null when localization could not be read,
    /// which is a reason to fall back and not to fail.</summary>
    public string? DisplayName { get; }

    /// <summary>The item's name token, before localization. Null when it
    /// could not be read.</summary>
    public string? Token { get; }

    public override string ToString() =>
        DisplayName == null ? PrefabName : DisplayName + " (" + PrefabName + ")";
}

/// <summary>What one slot asks for, best key first.</summary>
public sealed class NpcLookRequest
{
    public NpcLookRequest(
        string displayName,
        IReadOnlyList<string> prefabCandidates,
        IReadOnlyList<string> shapeFallbacks,
        string familyPrefix,
        string? token = null)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        PrefabCandidates = prefabCandidates ?? throw new ArgumentNullException(nameof(prefabCandidates));
        ShapeFallbacks = shapeFallbacks ?? throw new ArgumentNullException(nameof(shapeFallbacks));
        FamilyPrefix = familyPrefix ?? throw new ArgumentNullException(nameof(familyPrefix));
        Token = string.IsNullOrWhiteSpace(token) ? null : token!.Trim();
    }

    /// <summary>The exact label read off the owner's customization screen. The
    /// authoritative key, because it is what the owner chose.</summary>
    public string DisplayName { get; }

    /// <summary>The label's localization token, when the role knows it. Tried
    /// right after the label, and it survives a game running in another
    /// language, which the label does not.</summary>
    public string? Token { get; }

    /// <summary>Documented prefab ids for that label. Secondary: they come from
    /// generated item lists, not from this installation.</summary>
    public IReadOnlyList<string> PrefabCandidates { get; }

    /// <summary>Substrings of the shape, tried once the exact keys fail.</summary>
    public IReadOnlyList<string> ShapeFallbacks { get; }

    /// <summary>The slot's prefab-name prefix, for the last fallback.</summary>
    public string FamilyPrefix { get; }
}

/// <summary>One resolved slot.</summary>
public readonly struct NpcLookChoice
{
    public NpcLookChoice(string? prefabName, string? displayName, NpcLookMatch match)
    {
        PrefabName = prefabName;
        DisplayName = displayName;
        Match = match;
    }

    public string? PrefabName { get; }

    public string? DisplayName { get; }

    public NpcLookMatch Match { get; }

    /// <summary>Only the owner's own label, or its token, counts as a match.</summary>
    public bool MatchesReference => Match == NpcLookMatch.DisplayName || Match == NpcLookMatch.Token;

    public static NpcLookChoice None => new NpcLookChoice(null, null, NpcLookMatch.None);

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

/// <summary>The rule that resolves a requested look against whatever this game
/// build actually has (lifted from Concerned Foreman's Thorstein, #295/#298,
/// which transcribed it from Hulgi's <c>AppearancePlan</c>).
///
/// <b>Nothing is assumed to exist.</b> Preset names are asset data, not API.
/// The options are enumerated from the game's own item table at run time, the
/// same query its customization screen makes. The order is: a player's
/// override, the owner's label, the label's token, a documented prefab id, a
/// shape, the family, and then nothing. A missing preset changes how the
/// figure looks and nothing else.</summary>
public static class NpcLookPlan
{
    /// <summary>Picks one slot.</summary>
    /// <param name="available">What this build offers for the slot.</param>
    /// <param name="request">What was asked for.</param>
    /// <param name="playerOverride">A prefab name or label from configuration.
    /// One that names something this build does not have falls through to the
    /// stock chain rather than leaving the slot empty.</param>
    public static NpcLookChoice Choose(
        IReadOnlyList<NpcLookOption>? available, NpcLookRequest request, string? playerOverride = null)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (available == null || available.Count == 0)
        {
            return NpcLookChoice.None;
        }

        if (!string.IsNullOrWhiteSpace(playerOverride))
        {
            NpcLookChoice chosen = MatchExact(available, playerOverride!, NpcLookMatch.Override);
            if (chosen.PrefabName != null)
            {
                return chosen;
            }
        }

        NpcLookChoice byLabel = MatchExact(available, request.DisplayName, NpcLookMatch.DisplayName);
        if (byLabel.PrefabName != null)
        {
            return byLabel;
        }

        if (request.Token != null)
        {
            foreach (NpcLookOption option in available)
            {
                if (Equal(option.Token, request.Token))
                {
                    return new NpcLookChoice(option.PrefabName, option.DisplayName, NpcLookMatch.Token);
                }
            }
        }

        foreach (string candidate in request.PrefabCandidates)
        {
            NpcLookChoice byPrefab = MatchExact(available, candidate, NpcLookMatch.PrefabName);
            if (byPrefab.PrefabName != null)
            {
                return byPrefab;
            }
        }

        foreach (string fragment in request.ShapeFallbacks)
        {
            foreach (NpcLookOption option in available)
            {
                if (Contains(option.DisplayName, fragment) || Contains(option.PrefabName, fragment))
                {
                    return new NpcLookChoice(option.PrefabName, option.DisplayName, NpcLookMatch.Shape);
                }
            }
        }

        foreach (NpcLookOption option in available)
        {
            if (option.PrefabName.StartsWith(request.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new NpcLookChoice(option.PrefabName, option.DisplayName, NpcLookMatch.Family);
            }
        }

        return NpcLookChoice.None;
    }

    private static NpcLookChoice MatchExact(IReadOnlyList<NpcLookOption> available, string key, NpcLookMatch match)
    {
        string wanted = key.Trim();
        foreach (NpcLookOption option in available)
        {
            if (Equal(option.DisplayName, wanted) || Equal(option.PrefabName, wanted))
            {
                return new NpcLookChoice(option.PrefabName, option.DisplayName, match);
            }
        }

        return NpcLookChoice.None;
    }

    private static bool Equal(string? value, string wanted) =>
        !string.IsNullOrEmpty(value)
        && string.Equals(value!.Trim(), wanted, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string fragment) =>
        !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(fragment)
        && value!.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
