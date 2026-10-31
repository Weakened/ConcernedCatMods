using System;
using System.Collections.Generic;

namespace TheConcernedCat.Workers.Felling;

/// <summary>One small tree a worker may fell (owner decision 2026-10-31, D19).
/// Every value is cited to the installed game: Valheim 1.0.21, main prefab
/// bundle <c>SoftRef/Bundles/c4210710</c> (756,139,651 bytes, read read-only
/// with UnityPy), its <c>Destructible</c> and <c>DropOnDestroyed</c>
/// components. Nothing here was guessed from a name.</summary>
internal sealed class SmallTreeKind
{
    public SmallTreeKind(string prefab, int minToolTier, float health, int dropPicksMin, int dropPicksMax, string drops)
    {
        Prefab = prefab;
        MinToolTier = minToolTier;
        Health = health;
        DropPicksMin = dropPicksMin;
        DropPicksMax = dropPicksMax;
        Drops = drops;
    }

    /// <summary>The network prefab name (the hash the world save stores).
    /// </summary>
    public string Prefab { get; }

    /// <summary><c>Destructible.m_minToolTier</c>.</summary>
    public int MinToolTier { get; }

    /// <summary><c>Destructible.m_health</c> at world level 0.</summary>
    public float Health { get; }

    /// <summary><c>DropOnDestroyed</c>'s pick count range.</summary>
    public int DropPicksMin { get; }

    public int DropPicksMax { get; }

    /// <summary>What the drop table holds, for people (weights as read).
    /// </summary>
    public string Drops { get; }
}

/// <summary>Why a candidate is not a small tree a worker may fell. Zero is the
/// unspecified value: a verdict nobody computed is never eligible.</summary>
internal enum SmallTreeRefusal
{
    Unspecified = 0,
    Eligible = 1,

    /// <summary>Not one of the four allowlisted prefabs (a full-size tree, a
    /// bush, a stump, anything else).</summary>
    NotAllowlisted = 2,

    /// <summary>Not exactly one destructible part, or no valid network object.
    /// </summary>
    WrongShape = 3,

    /// <summary>Planted by a player (<c>Plant</c>) or built (<c>Piece</c>,
    /// <c>WearNTear</c>): every <c>*_Sapling</c> is cultivation.</summary>
    Cultivated = 4,

    /// <summary>It carries a harvestable part (<c>Pickable</c>): berry bushes
    /// and anything else that feeds a player. Food is never felled.</summary>
    Food = 5,

    /// <summary>Its tool tier is not the allowlisted one: a patched or modded
    /// value fails closed.</summary>
    ConfigurationChanged = 6,

    /// <summary>Already destroyed, or its health is not positive.</summary>
    Gone = 7,
}

/// <summary>What the adapter reads from a candidate in the world.</summary>
internal readonly struct SmallTreeFacts
{
    public SmallTreeFacts(
        bool hasValidNetworkObject,
        string? networkPrefabName,
        int destructibleCount,
        bool hasPlant,
        bool hasPiece,
        bool hasWearNTear,
        bool hasPickable,
        int minToolTier,
        float health)
    {
        HasValidNetworkObject = hasValidNetworkObject;
        NetworkPrefabName = networkPrefabName;
        DestructibleCount = destructibleCount;
        HasPlant = hasPlant;
        HasPiece = hasPiece;
        HasWearNTear = hasWearNTear;
        HasPickable = hasPickable;
        MinToolTier = minToolTier;
        Health = health;
    }

    public bool HasValidNetworkObject { get; }

    public string? NetworkPrefabName { get; }

    public int DestructibleCount { get; }

    public bool HasPlant { get; }

    public bool HasPiece { get; }

    public bool HasWearNTear { get; }

    public bool HasPickable { get; }

    public int MinToolTier { get; }

    public float Health { get; }
}

/// <summary>Why a felling hit is not allowed here and now. Zero is never a
/// grant.</summary>
internal enum FellSiteRefusal
{
    Unspecified = 0,
    Allowed = 1,
    FeatureOff = 2,
    NoAuthority = 3,

    /// <summary>The order has no explicit work area (a harvest designation or
    /// a map area): the default camp circle never authorises felling.</summary>
    NoExplicitArea = 4,

    OutsideArea = 5,
    NotOwnedHere = 6,
    WardDenied = 7,
    PlayerTooClose = 8,
    BuiltPieceTooClose = 9,
    WorkerOutOfReach = 10,
    NoUsableAxe = 11,
    AxeTierTooLow = 12,
}

/// <summary>The place and the worker, as the adapter measured them.</summary>
internal readonly struct FellSiteFacts
{
    public FellSiteFacts(
        bool featureEnabled,
        bool authorityGranted,
        bool explicitArea,
        bool insideArea,
        bool ownedHere,
        bool wardAllows,
        float nearestPlayerMetres,
        float nearestBuiltPieceMetres,
        float workerDistanceMetres,
        bool hasUsableAxe,
        int axeToolTier,
        int treeMinToolTier)
    {
        FeatureEnabled = featureEnabled;
        AuthorityGranted = authorityGranted;
        ExplicitArea = explicitArea;
        InsideArea = insideArea;
        OwnedHere = ownedHere;
        WardAllows = wardAllows;
        NearestPlayerMetres = nearestPlayerMetres;
        NearestBuiltPieceMetres = nearestBuiltPieceMetres;
        WorkerDistanceMetres = workerDistanceMetres;
        HasUsableAxe = hasUsableAxe;
        AxeToolTier = axeToolTier;
        TreeMinToolTier = treeMinToolTier;
    }

    public bool FeatureEnabled { get; }

    public bool AuthorityGranted { get; }

    public bool ExplicitArea { get; }

    public bool InsideArea { get; }

    public bool OwnedHere { get; }

    public bool WardAllows { get; }

    /// <summary>Horizontal distance to the nearest player; infinity when there
    /// is none to measure. A NaN fails closed.</summary>
    public float NearestPlayerMetres { get; }

    /// <summary>Horizontal distance to the nearest player-built piece;
    /// infinity when there is none within the search.</summary>
    public float NearestBuiltPieceMetres { get; }

    public float WorkerDistanceMetres { get; }

    public bool HasUsableAxe { get; }

    public int AxeToolTier { get; }

    public int TreeMinToolTier { get; }
}

/// <summary>What one felling hit did, as measured.</summary>
internal enum FellOutcome
{
    Unspecified = 0,

    /// <summary>The tree took the hit and still stands.</summary>
    Hit = 1,

    /// <summary>The tree is gone: vanilla's own drops were spawned by it.</summary>
    Felled = 2,

    /// <summary>The hit changed nothing (too hard, resisted, or refused by
    /// the game).</summary>
    NoEffect = 3,

    /// <summary>What happened cannot be told from what was measured. It is
    /// never counted as a fell.</summary>
    Uncertain = 4,
}

/// <summary>D19, game-free: which objects are small trees a worker may fell,
/// where and when a hit is allowed, and how a hit's result is read. Shared so
/// Teamster's Gunnar (#381) can apply the same rules; this file names no game
/// type and calls nothing.</summary>
internal static class SmallTreeFelling
{
    /// <summary>The owner's 2026-10-31 clearance: never within this distance of
    /// a player or of a player-built piece.</summary>
    public const float ClearanceMetres = 15f;

    /// <summary>The four small trees D19 allows, and nothing else. Read from the
    /// installed bundle; each is a <c>Destructible</c> with a
    /// <c>DropOnDestroyed</c>, tool tier 0, and no <c>Pickable</c>,
    /// <c>Plant</c> or <c>Piece</c>.</summary>
    public static readonly IReadOnlyList<SmallTreeKind> Allowlist = new[]
    {
        new SmallTreeKind("Beech_small1", 0, 20f, 2, 3, "Wood (weight 1), Resin (weight 0.2)"),
        new SmallTreeKind("Beech_small2", 0, 20f, 2, 3, "Wood (weight 1), Resin (weight 0.2)"),
        new SmallTreeKind("FirTree_small", 0, 40f, 4, 5, "Wood (weight 1), FirCone (weight 0.1), Resin (weight 0.1)"),
        new SmallTreeKind("FirTree_small_dead", 0, 40f, 4, 5, "Wood (weight 1)"),
    };

    public static bool TryFind(string? prefab, out SmallTreeKind? kind)
    {
        foreach (SmallTreeKind candidate in Allowlist)
        {
            if (string.Equals(candidate.Prefab, prefab, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = null;
        return false;
    }

    /// <summary>Is this object a small tree D19 allows? Checked in this order:
    /// identity, shape, cultivation, food, configuration, state.</summary>
    public static SmallTreeRefusal Classify(SmallTreeFacts facts)
    {
        if (!facts.HasValidNetworkObject || !TryFind(facts.NetworkPrefabName, out SmallTreeKind? kind))
        {
            return SmallTreeRefusal.NotAllowlisted;
        }

        if (facts.DestructibleCount != 1)
        {
            return SmallTreeRefusal.WrongShape;
        }

        if (facts.HasPlant || facts.HasPiece || facts.HasWearNTear)
        {
            return SmallTreeRefusal.Cultivated;
        }

        if (facts.HasPickable)
        {
            return SmallTreeRefusal.Food;
        }

        if (facts.MinToolTier != kind!.MinToolTier)
        {
            return SmallTreeRefusal.ConfigurationChanged;
        }

        if (!(facts.Health > 0f))
        {
            return SmallTreeRefusal.Gone;
        }

        return SmallTreeRefusal.Eligible;
    }

    /// <summary>May a hit land here, now? Checked in this order: the switch,
    /// authority, the explicit area, ownership, the ward, the clearances, reach
    /// and the axe. Any measurement that is not a number refuses.</summary>
    public static FellSiteRefusal CheckSite(FellSiteFacts site, float reachMetres)
    {
        if (!site.FeatureEnabled)
        {
            return FellSiteRefusal.FeatureOff;
        }

        if (!site.AuthorityGranted)
        {
            return FellSiteRefusal.NoAuthority;
        }

        if (!site.ExplicitArea)
        {
            return FellSiteRefusal.NoExplicitArea;
        }

        if (!site.InsideArea)
        {
            return FellSiteRefusal.OutsideArea;
        }

        if (!site.OwnedHere)
        {
            return FellSiteRefusal.NotOwnedHere;
        }

        if (!site.WardAllows)
        {
            return FellSiteRefusal.WardDenied;
        }

        if (!(site.NearestPlayerMetres >= ClearanceMetres))
        {
            return FellSiteRefusal.PlayerTooClose;
        }

        if (!(site.NearestBuiltPieceMetres >= ClearanceMetres))
        {
            return FellSiteRefusal.BuiltPieceTooClose;
        }

        if (!(site.WorkerDistanceMetres <= reachMetres))
        {
            return FellSiteRefusal.WorkerOutOfReach;
        }

        if (!site.HasUsableAxe)
        {
            return FellSiteRefusal.NoUsableAxe;
        }

        if (site.AxeToolTier < site.TreeMinToolTier)
        {
            return FellSiteRefusal.AxeTierTooLow;
        }

        return FellSiteRefusal.Allowed;
    }

    /// <summary>Reads one hit's result from what was measured before and after.
    /// Felled needs the object gone AND at least one new drop seen at the tree:
    /// a disappearance with nothing spawned is not credited as a fell.</summary>
    public static FellOutcome Read(float healthBefore, bool goneAfter, float healthAfter, int newDropsAtTree, bool threw)
    {
        if (goneAfter)
        {
            return newDropsAtTree > 0 ? FellOutcome.Felled : FellOutcome.Uncertain;
        }

        if (threw || float.IsNaN(healthAfter) || newDropsAtTree > 0)
        {
            return FellOutcome.Uncertain;
        }

        if (healthAfter < healthBefore)
        {
            return FellOutcome.Hit;
        }

        return healthAfter == healthBefore ? FellOutcome.NoEffect : FellOutcome.Uncertain;
    }

    public static string Describe(SmallTreeRefusal refusal) => refusal switch
    {
        SmallTreeRefusal.Eligible => "a small tree he may fell",
        SmallTreeRefusal.NotAllowlisted => "not one of the small trees he may fell (small beech and small fir only)",
        SmallTreeRefusal.WrongShape => "not a plain small tree",
        SmallTreeRefusal.Cultivated => "planted or built by a player; he never fells those",
        SmallTreeRefusal.Food => "it bears food; he never fells those",
        SmallTreeRefusal.ConfigurationChanged => "this tree is not configured the way the game ships it, so he leaves it alone",
        SmallTreeRefusal.Gone => "it is already down",
        _ => "not a small tree he may fell",
    };

    public static string Describe(FellSiteRefusal refusal) => refusal switch
    {
        FellSiteRefusal.Allowed => "allowed",
        FellSiteRefusal.FeatureOff => "felling is turned off (Collection/ThorsteinFellsSmallTrees)",
        FellSiteRefusal.NoAuthority => "work is not authorised here",
        FellSiteRefusal.NoExplicitArea => "he fells only inside a work area you chose or marked, never in the default camp circle",
        FellSiteRefusal.OutsideArea => "that tree is outside the work area",
        FellSiteRefusal.NotOwnedHere => "that tree is not owned by this game, so he leaves it alone",
        FellSiteRefusal.WardDenied => "a ward protects that tree",
        FellSiteRefusal.PlayerTooClose => "someone is within 15 m of that tree",
        FellSiteRefusal.BuiltPieceTooClose => "something you built is within 15 m of that tree",
        FellSiteRefusal.WorkerOutOfReach => "Thorstein is not standing at that tree",
        FellSiteRefusal.NoUsableAxe => "Thorstein has no usable axe of his own",
        FellSiteRefusal.AxeTierTooLow => "his axe is not good enough for that tree",
        _ => "not allowed",
    };
}
