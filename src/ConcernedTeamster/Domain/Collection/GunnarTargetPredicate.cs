using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>The vanilla facts the eligibility rule pins. A modded or patched
/// value fails closed: it is not collected.
///
/// <b>These are Valheim's own prefab names, not another product's.</b> They name
/// objects in the game's asset bundles and appear in every save that contains
/// one. Nothing here names a Concerned Cat prefab or key prefix belonging to any
/// other mod, which is the rule a shared runtime makes worth stating.</summary>
internal static class GunnarCollectionAllowlist
{
    /// <summary>The network prefab of a natural loose stone.</summary>
    public const string LooseStonePrefab = "Pickable_Stone";

    /// <summary>The network prefab of a natural fallen branch.</summary>
    public const string BranchPrefab = "Pickable_Branch";

    /// <summary>The item a loose stone gives.</summary>
    public const string StoneItemPrefab = "Stone";

    /// <summary>The item a branch gives.</summary>
    public const string WoodItemPrefab = "Wood";

    /// <summary>What a candidate must not be, in this layer's own words.
    ///
    /// <b>Words, not type names, and the architecture requires it.</b> Only the
    /// adapter layer of this product may name a game type; the domain is proved
    /// game-free by compiling into a test assembly that has no game assemblies
    /// at all. So the reader that looks at a live object maps the game's
    /// component types onto these ten tokens, and this layer decides from the
    /// tokens. The cost is that the mapping itself needs its own test where the
    /// reader lives; the benefit is that every refusal below can be exercised
    /// without a running game, which is where every one of these exclusions
    /// actually gets proved.
    ///
    /// <b>Two of them are exclusions #381 names outright.</b> "a container" is
    /// no unrelated containers - a chest standing in the work area is not a
    /// resource, whatever it holds. "a dropped item pile" is no arbitrary player
    /// piles - a stack somebody dropped on the ground is theirs. Both are
    /// recognised by what the object <i>is</i> rather than by what it is called,
    /// because a name is a thing a mod renames.</summary>
    public static readonly IReadOnlyList<string> ForbiddenSemantics = new[]
    {
        "a built piece",
        "structural health",
        "a cultivated plant",
        "a destructible",
        "a dropped item pile",
        "a container",
        "a display stand",
        "a breeding pair",
        "a creature",
        "a carried item",
    };

    /// <summary>Which kind a network prefab name is, if it is one at all.</summary>
    public static bool TryKindOfPrefab(string? networkPrefabName, out CollectableKind kind)
    {
        switch (networkPrefabName)
        {
            case LooseStonePrefab:
                kind = CollectableKind.LooseStone;
                return true;
            case BranchPrefab:
                kind = CollectableKind.Branch;
                return true;
            default:
                kind = CollectableKind.Unspecified;
                return false;
        }
    }

    /// <summary>The item prefab a kind is expected to give.</summary>
    public static string ExpectedYieldOf(CollectableKind kind)
    {
        switch (kind)
        {
            case CollectableKind.LooseStone:
                return StoneItemPrefab;
            case CollectableKind.Branch:
                return WoodItemPrefab;
            default:
                return string.Empty;
        }
    }
}

/// <summary>What the predicate decided about one candidate.</summary>
internal readonly struct CollectableVerdict
{
    private CollectableVerdict(CollectableKind kind, CollectionClause failed, string detail)
    {
        Kind = kind;
        FailedClause = failed;
        Detail = detail ?? string.Empty;
    }

    /// <summary>The kind, once identity was established, even if a later clause
    /// then refused. Unspecified when identity itself failed.</summary>
    public CollectableKind Kind { get; }

    public CollectionClause FailedClause { get; }

    /// <summary>What exactly failed, for a log line and the player's preview.
    /// </summary>
    public string Detail { get; }

    /// <summary>Every clause passed: a source he may take right now.</summary>
    public bool IsEligible => FailedClause == CollectionClause.Unspecified;

    /// <summary>A source of the right kind, whatever its state. The distinction
    /// that lets a survey report "this area is picked clean" rather than "there
    /// is nothing here", which are different things to tell a player.</summary>
    public bool IsCollectableKind =>
        FailedClause == CollectionClause.Unspecified || FailedClause == CollectionClause.State;

    /// <summary>The item prefab one take would give, or empty.</summary>
    public string Yields => GunnarCollectionAllowlist.ExpectedYieldOf(Kind);

    internal static CollectableVerdict Eligible(CollectableKind kind) =>
        new CollectableVerdict(kind, CollectionClause.Unspecified, string.Empty);

    internal static CollectableVerdict Failed(CollectableKind kind, CollectionClause clause, string detail) =>
        new CollectableVerdict(kind, clause, detail);

    public override string ToString() =>
        IsEligible ? Kind + ": eligible" : FailedClause + ": " + Detail;
}

/// <summary>Whether Gunnar may collect one candidate, as a decision over facts
/// somebody else read, so every clause and every look-alike can be exercised
/// without the game running (#381).
///
/// <b>Why identity is an allowlist rather than a capability test.</b> "Has a
/// takeable component and gives Stone" admits two things it must not: the
/// hoe-placed stone a player put down, and the procreation-born rock that grows
/// on a timer. Only the network prefab name rejects the second before anything
/// else is read, which is why the name is load-bearing and why the provenance
/// and state clauses are still there behind it rather than instead of it.
///
/// <b>A player's mark widens which candidates are looked at, never which rules
/// apply.</b> <see cref="Classify"/> takes marked candidates through exactly the
/// same clauses, because a mark is the player saying "I want that one", not
/// "ignore what it is". A marked chest is still a chest.</summary>
internal static class GunnarTargetPredicate
{
    public static CollectableVerdict Classify(CollectableFacts? facts)
    {
        if (facts == null)
        {
            return CollectableVerdict.Failed(CollectableKind.Unspecified, CollectionClause.Identity, "no facts");
        }

        // Identity. The network prefab, and nothing else.
        if (!facts.HasValidWorldObject)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Identity, "no valid world object");
        }

        if (!GunnarCollectionAllowlist.TryKindOfPrefab(facts.NetworkPrefabName, out CollectableKind kind))
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Identity,
                "network prefab '" + (facts.NetworkPrefabName ?? "<unknown>") + "' is not one he collects");
        }

        // Shape. Exactly one takeable component, and none of the semantics that
        // make it something else.
        if (facts.TakeableComponentCount != 1)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Shape,
                facts.TakeableComponentCount + " takeable components, expected exactly one");
        }

        if (facts.ForbiddenComponents.Count > 0)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Shape,
                "carries " + string.Join(", ", ToArray(facts.ForbiddenComponents)));
        }

        // Food. Checked on its own so the refusal can say so.
        if (facts.BearsFood)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.FoodBearing,
                "it bears food, and food-bearing plants are left alone");
        }

        // Yield. What the game would actually hand over, and how much.
        string expected = GunnarCollectionAllowlist.ExpectedYieldOf(kind);
        if (!facts.YieldIsAnItem ||
            !string.Equals(facts.YieldItemPrefabName, expected, StringComparison.Ordinal))
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Yield,
                "gives '" + (facts.YieldItemPrefabName ?? "<nothing>") + "', expected '" + expected + "'");
        }

        if (facts.YieldAmount != 1 || !facts.ExtraDropsEmpty)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Yield,
                "amount " + facts.YieldAmount + ", extra drops " +
                (facts.ExtraDropsEmpty ? "none" : "present"));
        }

        // The vanilla shape of each kind: a loose stone does not come back and
        // has nothing to hide; a branch does come back and does. A source that
        // regrows on a timer but hides nothing is a timer wearing a branch's
        // name, which is the "no timer-generated resources" exclusion.
        bool renewable = facts.RespawnTimeMinutes > 0f;
        bool stone = kind == CollectableKind.LooseStone;
        if (stone ? (renewable || facts.HasHideWhenTaken) : (!renewable || !facts.HasHideWhenTaken))
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Yield,
                "respawn " + facts.RespawnTimeMinutes + " min, hide object " +
                (facts.HasHideWhenTaken ? "present" : "none") + " (vanilla " +
                (stone ? "stone: neither" : "branch: both") + ")");
        }

        // Provenance. Nobody put it there.
        if (facts.HasCreator)
        {
            return CollectableVerdict.Failed(
                CollectableKind.Unspecified, CollectionClause.Provenance,
                "somebody placed or dropped it, so it is theirs");
        }

        // State. Right kind of thing; is there anything left of it?
        if (facts.AlreadyTaken || !facts.Visible || !facts.TakeableNow)
        {
            return CollectableVerdict.Failed(
                kind, CollectionClause.State,
                facts.AlreadyTaken ? "already taken"
                    : !facts.Visible ? "not there to be seen; it is a timer, not a resource"
                    : "not takeable right now");
        }

        return CollectableVerdict.Eligible(facts.MarkedByPlayer ? CollectableKind.MarkedResource : kind);
    }

    /// <summary>The clauses about the place, checked in the order a player would
    /// fix them. Every unknown refuses.</summary>
    public static CollectionSiteClause CheckSite(CollectableSite site)
    {
        if (!site.OwnedHere)
        {
            return CollectionSiteClause.NotOwnedHere;
        }

        if (!site.InsideWorkArea)
        {
            return CollectionSiteClause.OutsideWorkArea;
        }

        switch (site.Location)
        {
            case LocationStanding.Outside:
                break;
            case LocationStanding.Inside:
                return CollectionSiteClause.InsideLocation;
            default:
                // Unknown refuses, and it refuses as InsideLocation on purpose:
                // the question is "does this belong to somewhere", and an
                // unanswerable question about a temple has to be answered the
                // way a temple would be.
                return CollectionSiteClause.InsideLocation;
        }

        switch (site.Ward)
        {
            case WardStanding.Granted:
                break;
            case WardStanding.Denied:
                return CollectionSiteClause.WardDenied;
            default:
                return CollectionSiteClause.WardUnknown;
        }

        return site.FitsCarryLimit ? CollectionSiteClause.Unspecified : CollectionSiteClause.OverCarryLimit;
    }

    /// <summary>One sentence per clause, for the log and the status line. The
    /// role owns the words; nothing in a shared runtime writes these.</summary>
    public static string Describe(CollectionClause clause)
    {
        switch (clause)
        {
            case CollectionClause.Unspecified:
                return "Nothing about it stops him.";
            case CollectionClause.Identity:
                return "It is not a loose stone or a fallen branch.";
            case CollectionClause.Shape:
                return "It is something else wearing the same shape - a built piece, a plant, a container or a pile.";
            case CollectionClause.FoodBearing:
                return "It bears food, and he leaves food-bearing plants alone.";
            case CollectionClause.Yield:
                return "What it would give is not what the game gives for one of these, so he does not touch it.";
            case CollectionClause.Provenance:
                return "Somebody put it there, so it is theirs.";
            case CollectionClause.State:
                return "There is nothing left of it right now.";
            default:
                return "Refused for a reason nobody recorded; that is a bug.";
        }
    }

    /// <summary>One sentence per site clause.</summary>
    public static string Describe(CollectionSiteClause clause)
    {
        switch (clause)
        {
            case CollectionSiteClause.Unspecified:
                return "Nothing about the place stops him.";
            case CollectionSiteClause.NotOwnedHere:
                return "That object is not under this session's control right now, and he never takes control of it.";
            case CollectionSiteClause.OutsideWorkArea:
                return "It is outside the work area you gave him, and he does not look further afield.";
            case CollectionSiteClause.WardDenied:
                return "Somebody else's ward covers it.";
            case CollectionSiteClause.WardUnknown:
                return "He could not tell whether a ward covers it, because the ground around it is not loaded.";
            case CollectionSiteClause.InsideLocation:
                return "It belongs to a place the world built, or he could not tell, so he leaves it alone.";
            case CollectionSiteClause.OverCarryLimit:
                return "He cannot carry what it would give him.";
            default:
                return "Refused for a reason nobody recorded; that is a bug.";
        }
    }

    private static string[] ToArray(IReadOnlyList<string> items)
    {
        var array = new string[items.Count];
        for (int index = 0; index < items.Count; index++)
        {
            array[index] = items[index];
        }

        return array;
    }
}
