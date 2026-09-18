using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Designations;

namespace TheConcernedCat.Settlement.Collection.Planning;

/// <summary>The vanilla values the natural-source allowlist pins, read from the
/// installed Valheim 1.0.12 bundles and decompile (PICKUP_SEAM_AUDIT.md §1.9,
/// §3.1, §4.2). A modded or patched value fails closed: it is not collected.
/// </summary>
internal static class NaturalSourceAllowlist
{
    /// <summary>The network prefab of a natural loose stone. Identity is the
    /// prefab hash the world save stores, never the GameObject's name.</summary>
    public const string LooseStonePrefab = "Pickable_Stone";

    /// <summary>The network prefab of a natural branch.</summary>
    public const string BranchPrefab = "Pickable_Branch";

    public const string StoneItemPrefab = "Stone";

    public const string StoneSharedName = "$item_stone";

    public const string WoodItemPrefab = "Wood";

    public const string WoodSharedName = "$item_wood";

    /// <summary>Both allowlisted prefabs are untagged; the procreation-born
    /// look-alike <c>Pickable_HardRockOffspring</c> is tagged <c>spawned</c>.
    /// </summary>
    public const string UntaggedTag = "Untagged";

    /// <summary>Components whose presence anywhere in the object makes it not a
    /// natural loose source (CONTRACTS.md §6 C2): placement, cultivation,
    /// destruction, storage, display, breeding, creature and random-yield
    /// semantics. The adapter checks the real types; these are their names.
    /// </summary>
    public static readonly IReadOnlyList<string> ForbiddenComponentNames = new[]
    {
        "Piece", "WearNTear", "Plant", "Destructible", "ItemDrop", "Container", "ItemStand", "Procreation",
        "Character", "PickableItem",
    };

    public static bool TryKindOfPrefab(string? networkPrefabName, out NaturalSourceKind kind)
    {
        switch (networkPrefabName)
        {
            case LooseStonePrefab:
                kind = NaturalSourceKind.LooseStone;
                return true;
            case BranchPrefab:
                kind = NaturalSourceKind.Branch;
                return true;
            default:
                kind = NaturalSourceKind.Unspecified;
                return false;
        }
    }

    public static CollectedResource YieldOf(NaturalSourceKind kind)
    {
        switch (kind)
        {
            case NaturalSourceKind.LooseStone:
                return CollectedResource.Stone;
            case NaturalSourceKind.Branch:
                return CollectedResource.Wood;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Not a natural source kind.");
        }
    }
}

/// <summary>Which clause of CONTRACTS.md §6 a candidate failed first. Zero means
/// it failed none.</summary>
internal enum SourceClause
{
    /// <summary>No clause failed.</summary>
    Unspecified = 0,

    /// <summary>The network object is not <c>Pickable_Stone</c> or
    /// <c>Pickable_Branch</c> by prefab hash, or has no valid network object.
    /// </summary>
    C1Identity = 1,

    /// <summary>Not exactly one <c>Pickable</c>, or a forbidden component is
    /// present.</summary>
    C2Shape = 2,

    /// <summary>The root is not <c>Untagged</c>.</summary>
    C2bTag = 3,

    /// <summary>The yielded item is not the game's Stone or Wood.</summary>
    C3Yield = 4,

    /// <summary>Amount, extra drops, aggravation, respawn or hide object differ
    /// from the vanilla values.</summary>
    C3Configuration = 5,

    /// <summary>A creator is recorded: something placed it.</summary>
    C4Provenance = 6,

    /// <summary>Already picked, disabled, or not pickable right now.</summary>
    C5State = 7,
}

/// <summary>What the game said about one candidate, gathered by the adapter
/// from the live object. Every field is something the adapter read; nothing
/// here is inferred from a name or a size.</summary>
internal sealed class NaturalSourceFacts
{
    public NaturalSourceFacts(
        bool hasValidNetworkObject,
        string? networkPrefabName,
        int pickableCount,
        IReadOnlyList<string>? forbiddenComponents,
        string? rootTag,
        bool yieldHasItemDrop,
        string? yieldPrefabName,
        string? yieldSharedName,
        int amount,
        int minAmountScaled,
        bool dontScale,
        bool extraDropsEmpty,
        float aggravateRange,
        float respawnTimeMinutes,
        bool hasHideWhenPicked,
        long creator,
        bool picked,
        int enabled,
        bool canBePicked)
    {
        HasValidNetworkObject = hasValidNetworkObject;
        NetworkPrefabName = networkPrefabName;
        PickableCount = pickableCount;
        ForbiddenComponents = forbiddenComponents ?? Array.Empty<string>();
        RootTag = rootTag;
        YieldHasItemDrop = yieldHasItemDrop;
        YieldPrefabName = yieldPrefabName;
        YieldSharedName = yieldSharedName;
        Amount = amount;
        MinAmountScaled = minAmountScaled;
        DontScale = dontScale;
        ExtraDropsEmpty = extraDropsEmpty;
        AggravateRange = aggravateRange;
        RespawnTimeMinutes = respawnTimeMinutes;
        HasHideWhenPicked = hasHideWhenPicked;
        Creator = creator;
        Picked = picked;
        Enabled = enabled;
        CanBePicked = canBePicked;
    }

    public bool HasValidNetworkObject { get; }

    /// <summary>The name the game's own prefab table gives the object's
    /// network prefab hash (<c>ZDO.GetPrefab()</c>), or null.</summary>
    public string? NetworkPrefabName { get; }

    public int PickableCount { get; }

    /// <summary>Names of forbidden component types found anywhere in the
    /// object, inactive children included.</summary>
    public IReadOnlyList<string> ForbiddenComponents { get; }

    public string? RootTag { get; }

    public bool YieldHasItemDrop { get; }

    /// <summary><c>Utils.GetPrefabName(m_itemPrefab)</c>.</summary>
    public string? YieldPrefabName { get; }

    /// <summary><c>m_itemPrefab</c>'s <c>m_itemData.m_shared.m_name</c>.</summary>
    public string? YieldSharedName { get; }

    public int Amount { get; }

    /// <summary>The floor the game applies after world scaling. Vanilla's is 1;
    /// a patched one sizes the yield just as <c>m_amount</c> does.</summary>
    public int MinAmountScaled { get; }

    /// <summary>Whether the source opts out of the world's drop scaling.
    /// Vanilla's two do not.</summary>
    public bool DontScale { get; }

    public bool ExtraDropsEmpty { get; }

    public float AggravateRange { get; }

    public float RespawnTimeMinutes { get; }

    public bool HasHideWhenPicked { get; }

    /// <summary><c>ZDOVars.s_creator</c> on the source's network object.</summary>
    public long Creator { get; }

    public bool Picked { get; }

    /// <summary><c>Pickable.GetEnabled</c>: 1 enabled, 0 disabled, 2 unset.
    /// </summary>
    public int Enabled { get; }

    public bool CanBePicked { get; }
}

/// <summary>The predicate's answer for one candidate.</summary>
internal readonly struct NaturalSourceVerdict
{
    private NaturalSourceVerdict(NaturalSourceKind kind, SourceClause failed, string detail)
    {
        Kind = kind;
        FailedClause = failed;
        Detail = detail;
    }

    /// <summary>The kind, when identity was established (C1–C4 passed), even if
    /// C5 then failed. Unspecified otherwise.</summary>
    public NaturalSourceKind Kind { get; }

    public SourceClause FailedClause { get; }

    /// <summary>What exactly failed, for logs and the preview.</summary>
    public string Detail { get; }

    /// <summary>Every clause passed: a natural source that can be picked now.
    /// </summary>
    public bool IsEligible => FailedClause == SourceClause.Unspecified;

    /// <summary>A natural source by identity (C1–C4), whatever its state.
    /// </summary>
    public bool IsNatural => FailedClause == SourceClause.Unspecified || FailedClause == SourceClause.C5State;

    public CollectedResource Yields =>
        Kind == NaturalSourceKind.Unspecified ? CollectedResource.Unspecified : NaturalSourceAllowlist.YieldOf(Kind);

    internal static NaturalSourceVerdict Eligible(NaturalSourceKind kind) =>
        new NaturalSourceVerdict(kind, SourceClause.Unspecified, string.Empty);

    internal static NaturalSourceVerdict Failed(NaturalSourceKind kind, SourceClause clause, string detail) =>
        new NaturalSourceVerdict(kind, clause, detail ?? string.Empty);

    public override string ToString() => IsEligible ? Kind + ": eligible" : FailedClause + ": " + Detail;
}

/// <summary>Why a site refuses a pick (CONTRACTS.md §6, site clauses). Zero
/// means no site clause refused.</summary>
internal enum SiteRefusal
{
    /// <summary>No site clause refused.</summary>
    Unspecified = 0,

    /// <summary>This process does not own the source's network object. It is
    /// never claimed.</summary>
    NotOwned = 1,

    OutsideScope = 2,

    /// <summary>Dungeon interiors are not loose ground.</summary>
    Interior = 3,

    /// <summary>Inside a world location, including the start temple (D10).
    /// </summary>
    InsideLocation = 4,

    /// <summary>Somebody else's ward covers the source.</summary>
    WardDenied = 5,

    /// <summary>The ward question could not be answered (ground around it not
    /// loaded, or the check failed).</summary>
    WardUnknown = 6,

    OutOfReach = 7,

    /// <summary>What the pick would give does not fit what he may carry.
    /// </summary>
    CarryCapacity = 8,

    /// <summary>Whether the source belongs to a world location could not be
    /// established. D10 excludes locations, so an unknown answer refuses.
    /// </summary>
    LocationUnknown = 10,

    /// <summary>The game's pick path dereferences the local player and would
    /// throw without one.</summary>
    NoLocalPlayer = 9,
}

/// <summary>Whether a source belongs to a world location (D10). Three-valued
/// on purpose: the game's own loaded-location list is empty for the moments
/// between a zone's objects being created and its location awaking, and
/// "we could not tell" must refuse rather than admit a temple stone.</summary>
internal enum LocationStanding
{
    /// <summary>Nobody asked. Refuses, like every other unfilled answer.
    /// </summary>
    Unspecified = 0,

    Outside = 1,

    Inside = 2,

    /// <summary>The world's location registry could not be consulted.
    /// </summary>
    Unknown = 3,
}

/// <summary>The site facts the adapter reads at the instant of a pick (or, for
/// the survey, the subset that does not depend on where the worker stands).
/// </summary>
internal readonly struct SourceSiteFacts
{
    public SourceSiteFacts(
        bool ownedHere,
        bool inScope,
        bool inInterior,
        LocationStanding location,
        AreaAccess ward,
        float workerDistanceMetres,
        bool capacityFits,
        bool localPlayerPresent)
    {
        OwnedHere = ownedHere;
        InScope = inScope;
        InInterior = inInterior;
        Location = location;
        Ward = ward;
        WorkerDistanceMetres = workerDistanceMetres;
        CapacityFits = capacityFits;
        LocalPlayerPresent = localPlayerPresent;
    }

    public bool OwnedHere { get; }

    public bool InScope { get; }

    public bool InInterior { get; }

    /// <summary>Whether a world location covers the source. Unknown refuses.
    /// </summary>
    public LocationStanding Location { get; }

    /// <summary>The existing designation-site answer: loaded margin, no flash,
    /// <c>wardCheck: true</c>, exceptions refuse.</summary>
    public AreaAccess Ward { get; }

    /// <summary>Horizontal distance from the worker to the source.</summary>
    public float WorkerDistanceMetres { get; }

    public bool CapacityFits { get; }

    public bool LocalPlayerPresent { get; }
}

/// <summary>The natural-source allowlist predicate of CONTRACTS.md §6, as a
/// decision over facts, so every clause and every audited look-alike can be
/// exercised without the game (D10).
///
/// <b>Why every clause, and in this order.</b> The pickup audit found two
/// look-alikes that a capability test admits: the hoe-placed
/// <c>Placeable_Stone</c> (same Pickable, plus <c>Piece</c> and
/// <c>WearNTear</c>, and a creator) and the procreation-born
/// <c>Pickable_HardRockOffspring</c> (same components, no creator, Stone
/// yield). Only the network prefab name (C1) and the <c>spawned</c> tag (C2b)
/// reject the second, so the name allowlist is load-bearing and the tag is an
/// independent second rejection. Shape (C2) rejects placed and cultivated
/// things and anything a mod bolts on; configuration and yield (C3) pin the
/// vanilla values so a patched amount or a Frostwood branch fails closed;
/// provenance (C4) rejects a placed variant renamed by a mod; state (C5)
/// separates an exhausted source from a live one.</summary>
internal static class NaturalSourcePredicate
{
    public static NaturalSourceVerdict Classify(NaturalSourceFacts facts)
    {
        if (facts == null)
        {
            return NaturalSourceVerdict.Failed(NaturalSourceKind.Unspecified, SourceClause.C1Identity, "no facts");
        }

        // C1: identity by the network prefab, and nothing else.
        if (!facts.HasValidNetworkObject)
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C1Identity, "no valid network object");
        }

        if (!NaturalSourceAllowlist.TryKindOfPrefab(facts.NetworkPrefabName, out NaturalSourceKind kind))
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C1Identity,
                "network prefab '" + (facts.NetworkPrefabName ?? "<unknown>") + "' is not allowlisted");
        }

        // C2: exactly one Pickable and none of the forbidden semantics.
        if (facts.PickableCount != 1)
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C2Shape, facts.PickableCount + " Pickable components");
        }

        if (facts.ForbiddenComponents.Count > 0)
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C2Shape,
                "has " + string.Join(", ", ToArray(facts.ForbiddenComponents)));
        }

        // C2b: the untagged root.
        if (!string.Equals(facts.RootTag, NaturalSourceAllowlist.UntaggedTag, StringComparison.Ordinal))
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C2bTag, "tagged '" + (facts.RootTag ?? "<none>") + "'");
        }

        // C3: the yield is the game's own Stone or Wood...
        bool stone = kind == NaturalSourceKind.LooseStone;
        string expectedPrefab = stone ? NaturalSourceAllowlist.StoneItemPrefab : NaturalSourceAllowlist.WoodItemPrefab;
        string expectedShared = stone ? NaturalSourceAllowlist.StoneSharedName : NaturalSourceAllowlist.WoodSharedName;
        if (!facts.YieldHasItemDrop ||
            !string.Equals(facts.YieldPrefabName, expectedPrefab, StringComparison.Ordinal) ||
            !string.Equals(facts.YieldSharedName, expectedShared, StringComparison.Ordinal))
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C3Yield,
                "yields '" + (facts.YieldPrefabName ?? "<nothing>") + "' (" + (facts.YieldSharedName ?? "?") +
                "), expected '" + expectedPrefab + "'");
        }

        // ...and the configuration is the vanilla one.
        // The three numbers the game multiplies into the drop count are all
        // pinned: m_amount, the scaled floor, and whether scaling applies at
        // all. A patched one of any of them would size the yield.
        if (facts.Amount != 1 || facts.MinAmountScaled != 1 || facts.DontScale ||
            !facts.ExtraDropsEmpty || facts.AggravateRange != 0f)
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C3Configuration,
                "amount " + facts.Amount + ", scaled floor " + facts.MinAmountScaled +
                (facts.DontScale ? ", unscaled" : string.Empty) +
                ", extra drops " + (facts.ExtraDropsEmpty ? "none" : "present") +
                ", aggravate range " + facts.AggravateRange);
        }

        bool renewable = facts.RespawnTimeMinutes > 0f;
        if (stone ? (renewable || facts.HasHideWhenPicked) : (!renewable || !facts.HasHideWhenPicked))
        {
            return NaturalSourceVerdict.Failed(
                NaturalSourceKind.Unspecified, SourceClause.C3Configuration,
                "respawn " + facts.RespawnTimeMinutes + " min, hide object " + (facts.HasHideWhenPicked ? "present" : "none") +
                " (vanilla " + (stone ? "stone: none and none" : "branch: renewable with a hide object") + ")");
        }

        // C4: nobody placed it.
        if (facts.Creator != 0L)
        {
            return NaturalSourceVerdict.Failed(NaturalSourceKind.Unspecified, SourceClause.C4Provenance, "has a creator");
        }

        // C5: a natural source, but is it pickable now?
        if (facts.Picked || facts.Enabled != 1 || !facts.CanBePicked)
        {
            return NaturalSourceVerdict.Failed(
                kind, SourceClause.C5State,
                facts.Picked ? "already picked" : facts.Enabled != 1 ? "disabled" : "not pickable now");
        }

        return NaturalSourceVerdict.Eligible(kind);
    }

    /// <summary>The site clauses checked in the same tick as the pick, in the
    /// order a player would fix them.</summary>
    public static SiteRefusal CheckSite(SourceSiteFacts site, float pickupReachMetres)
    {
        if (!site.LocalPlayerPresent)
        {
            return SiteRefusal.NoLocalPlayer;
        }

        if (!site.OwnedHere)
        {
            return SiteRefusal.NotOwned;
        }

        if (!site.InScope)
        {
            return SiteRefusal.OutsideScope;
        }

        if (site.InInterior)
        {
            return SiteRefusal.Interior;
        }

        switch (site.Location)
        {
            case LocationStanding.Outside:
                break;
            case LocationStanding.Inside:
                return SiteRefusal.InsideLocation;
            default:
                // Unknown, and nobody asked, both refuse: D10 excludes
                // locations, and a window in which the answer is not available
                // is not a window in which the answer is "no".
                return SiteRefusal.LocationUnknown;
        }

        switch (site.Ward)
        {
            case AreaAccess.Granted:
                break;
            case AreaAccess.Denied:
                return SiteRefusal.WardDenied;
            default:
                return SiteRefusal.WardUnknown;
        }

        // NaN is out of reach, not within it.
        if (!(site.WorkerDistanceMetres <= pickupReachMetres))
        {
            return SiteRefusal.OutOfReach;
        }

        return site.CapacityFits ? SiteRefusal.Unspecified : SiteRefusal.CarryCapacity;
    }

    /// <summary>What a survey records for a natural candidate. Worker distance,
    /// capacity and the local player are pick-time questions and are not asked
    /// here. Exhausted, inaccessible and unknown stay distinct; a candidate
    /// outside the scope or not natural is not an observation at all (null).
    /// </summary>
    public static SourceAvailability? SurveyAvailability(NaturalSourceVerdict verdict, SourceSiteFacts site)
    {
        if (!verdict.IsNatural || !site.InScope)
        {
            return null;
        }

        if (verdict.FailedClause == SourceClause.C5State)
        {
            return SourceAvailability.Exhausted;
        }

        if (site.InInterior || site.Location == LocationStanding.Inside || site.Ward == AreaAccess.Denied)
        {
            return SourceAvailability.Inaccessible;
        }

        if (!site.OwnedHere || site.Ward != AreaAccess.Granted || site.Location != LocationStanding.Outside)
        {
            // Not owned here, or the ward could not be asked: we do not know
            // whether he could pick it, which is not the same as "he cannot".
            return SourceAvailability.Unknown;
        }

        return SourceAvailability.Available;
    }

    /// <summary>One sentence per site refusal.</summary>
    public static string Describe(SiteRefusal refusal)
    {
        switch (refusal)
        {
            case SiteRefusal.Unspecified:
                return "Nothing about the place stops him.";
            case SiteRefusal.NotOwned:
                return "That stone or branch is not under this world's control right now; he does not take it.";
            case SiteRefusal.OutsideScope:
                return "It is outside the work area.";
            case SiteRefusal.Interior:
                return "It is inside a dungeon, not on open ground.";
            case SiteRefusal.InsideLocation:
                return "It belongs to a place in the world, such as the start temple, and he leaves those alone.";
            case SiteRefusal.LocationUnknown:
                return "He could not tell whether it belongs to a place in the world, so he leaves it alone.";
            case SiteRefusal.WardDenied:
                return "Somebody else's ward covers it.";
            case SiteRefusal.WardUnknown:
                return "He could not tell whether a ward covers it, because the ground around it is not loaded.";
            case SiteRefusal.OutOfReach:
                return "He is not close enough to pick it up.";
            case SiteRefusal.CarryCapacity:
                return "He cannot carry what it would give.";
            case SiteRefusal.NoLocalPlayer:
                return "Picking needs a player in the world; nobody is there right now.";
            default:
                return "Refused for a reason that was not recorded; that is a bug.";
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
