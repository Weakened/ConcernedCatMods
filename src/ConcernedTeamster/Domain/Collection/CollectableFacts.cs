using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Which clause of the eligibility rule a candidate failed first. Zero
/// means none did.
///
/// <b>The order is the rule.</b> A candidate is judged by identity first,
/// because identity is the only clause a mod cannot make a look-alike pass, and
/// by state last, because "this is the right kind of thing but it has already
/// been taken" is a different answer from "this is not that kind of thing" and a
/// survey has to keep them apart.</summary>
internal enum CollectionClause
{
    /// <summary>Nothing failed.</summary>
    Unspecified = 0,

    /// <summary>No valid world object, or its network prefab is not one this
    /// slice collects.</summary>
    Identity = 1,

    /// <summary>It carries a component that means it is something else: a built
    /// piece, a cultivated plant, a container, a display stand, a creature, a
    /// breeding pair, a dropped item pile.</summary>
    Shape = 2,

    /// <summary>It bears food. Explicitly separate from <see cref="Shape"/>
    /// because "he ate the raspberry bushes" is the exclusion a player will
    /// actually notice, and a refusal that names it is worth the enum value.
    /// </summary>
    FoodBearing = 3,

    /// <summary>Its yield is not what the vanilla source of that kind gives, or
    /// its yield configuration has been patched. Fails closed: a source that
    /// would hand over something other than what the game hands a player is not
    /// collected at all.</summary>
    Yield = 4,

    /// <summary>Somebody placed or dropped it. A player's own pile is theirs.
    /// </summary>
    Provenance = 5,

    /// <summary>It regrows on a timer and has not regrown, or it is hidden, or
    /// it is not takeable right now. An exhausted source, not a forbidden one.
    /// </summary>
    State = 6,
}

/// <summary>Why the place refuses, even though the thing itself is eligible.
/// Zero means the place does not refuse.</summary>
internal enum CollectionSiteClause
{
    /// <summary>The place does not refuse.</summary>
    Unspecified = 0,

    /// <summary>This client does not own the object. It is never claimed:
    /// claiming ownership of a world object is exactly what Teamster's rules
    /// forbid.</summary>
    NotOwnedHere = 1,

    /// <summary>Outside the work area the player assigned. There is no wider
    /// search: an exhausted area is reported, never widened.</summary>
    OutsideWorkArea = 2,

    /// <summary>Somebody else's ward covers it.</summary>
    WardDenied = 3,

    /// <summary>Whether a ward covers it could not be established, because the
    /// ground around it is not loaded or the check failed. Unknown refuses:
    /// custody and authority fail closed.</summary>
    WardUnknown = 4,

    /// <summary>It belongs to a place the world generated - a temple, a crypt,
    /// a camp - or that could not be established. Unknown refuses.</summary>
    InsideLocation = 5,

    /// <summary>Taking it would put him over the carry limit the game gives
    /// him.</summary>
    OverCarryLimit = 6,
}

/// <summary>What the adapter read from one live candidate. Every field is
/// something the game was asked; nothing here is inferred from a name, a size or
/// a distance.
///
/// <b>A defaulted instance is refused.</b> Every flag is written so that
/// <c>default</c> means "not a valid world object, no prefab, no yield", which
/// fails the first clause. An adapter that forgets to fill a field produces a
/// refusal, never an admission.</summary>
internal sealed class CollectableFacts
{
    public CollectableFacts(
        bool hasValidWorldObject,
        string? networkPrefabName,
        int takeableComponentCount,
        IReadOnlyList<string>? forbiddenComponents,
        bool bearsFood,
        bool yieldIsAnItem,
        string? yieldItemPrefabName,
        int yieldAmount,
        bool extraDropsEmpty,
        float respawnTimeMinutes,
        bool hasHideWhenTaken,
        bool hasCreator,
        bool alreadyTaken,
        bool visible,
        bool takeableNow,
        bool markedByPlayer)
    {
        HasValidWorldObject = hasValidWorldObject;
        NetworkPrefabName = networkPrefabName;
        TakeableComponentCount = takeableComponentCount;
        ForbiddenComponents = forbiddenComponents ?? Array.Empty<string>();
        BearsFood = bearsFood;
        YieldIsAnItem = yieldIsAnItem;
        YieldItemPrefabName = yieldItemPrefabName;
        YieldAmount = yieldAmount;
        ExtraDropsEmpty = extraDropsEmpty;
        RespawnTimeMinutes = respawnTimeMinutes;
        HasHideWhenTaken = hasHideWhenTaken;
        HasCreator = hasCreator;
        AlreadyTaken = alreadyTaken;
        Visible = visible;
        TakeableNow = takeableNow;
        MarkedByPlayer = markedByPlayer;
    }

    /// <summary>Whether the object has a valid network object at all. Identity
    /// comes from the network prefab, so without one there is no identity.
    /// </summary>
    public bool HasValidWorldObject { get; }

    /// <summary>The name the game's own prefab table gives the object's network
    /// prefab hash - never the scene object's name, which a world location's
    /// embedded copy decorates with a numbered suffix.</summary>
    public string? NetworkPrefabName { get; }

    /// <summary>How many "this can be taken" components the object carries,
    /// inactive children included. Exactly one is the vanilla shape; two is a
    /// modded composite whose yield nobody here can predict.</summary>
    public int TakeableComponentCount { get; }

    /// <summary>Names of components found anywhere in the object that mean it is
    /// something other than a loose natural source.</summary>
    public IReadOnlyList<string> ForbiddenComponents { get; }

    /// <summary>Whether the object bears food - a berry bush, a mushroom, a
    /// crop. Read from the yield's own item data, not from its name.</summary>
    public bool BearsFood { get; }

    /// <summary>Whether what it gives is an item at all.</summary>
    public bool YieldIsAnItem { get; }

    /// <summary>The prefab name of the item it gives.</summary>
    public string? YieldItemPrefabName { get; }

    /// <summary>How many of that item one take gives.</summary>
    public int YieldAmount { get; }

    /// <summary>Whether it has no extra drop table beyond its single yield. A
    /// populated one is a randomised yield, which is not a resource with a
    /// known quantity.</summary>
    public bool ExtraDropsEmpty { get; }

    /// <summary>How long it takes to come back, in minutes. Zero means it does
    /// not come back.</summary>
    public float RespawnTimeMinutes { get; }

    /// <summary>Whether it has an object that is hidden once it has been taken -
    /// the vanilla shape of a thing that regrows.</summary>
    public bool HasHideWhenTaken { get; }

    /// <summary>Whether a creator is recorded on it: somebody placed or dropped
    /// it.</summary>
    public bool HasCreator { get; }

    /// <summary>Whether it has already been taken and has not come back.
    /// </summary>
    public bool AlreadyTaken { get; }

    /// <summary>Whether it is actually there to be seen. An invisible source is
    /// a timer counting down, not a resource.</summary>
    public bool Visible { get; }

    /// <summary>Whether the game says it can be taken at this moment.</summary>
    public bool TakeableNow { get; }

    /// <summary>Whether the player explicitly marked this one for collection.
    /// </summary>
    public bool MarkedByPlayer { get; }
}

/// <summary>Where a candidate is, and what the world says about that place.
/// </summary>
internal readonly struct CollectableSite
{
    public CollectableSite(
        bool ownedHere,
        bool insideWorkArea,
        WardStanding ward,
        LocationStanding location,
        bool fitsCarryLimit)
    {
        OwnedHere = ownedHere;
        InsideWorkArea = insideWorkArea;
        Ward = ward;
        Location = location;
        FitsCarryLimit = fitsCarryLimit;
    }

    public bool OwnedHere { get; }

    public bool InsideWorkArea { get; }

    public WardStanding Ward { get; }

    public LocationStanding Location { get; }

    public bool FitsCarryLimit { get; }
}

/// <summary>Whether a ward covers a place. Three-valued, and zero is the one
/// that refuses: an unasked question and an unanswerable one are the same thing
/// to somebody deciding whether to take a stranger's stone.</summary>
internal enum WardStanding
{
    /// <summary>Nobody asked, or the check could not be run.</summary>
    Unknown = 0,

    /// <summary>No ward refuses this place.</summary>
    Granted = 1,

    /// <summary>Somebody else's ward covers it.</summary>
    Denied = 2,
}

/// <summary>Whether a candidate belongs to a place the world generated. Three
/// valued for the same reason as <see cref="WardStanding"/>: the game's own
/// loaded-location list is empty for the moments between a zone's objects being
/// created and its location waking up, and "could not tell" must refuse rather
/// than admit a temple stone.</summary>
internal enum LocationStanding
{
    /// <summary>Nobody asked, or the registry could not be consulted.</summary>
    Unknown = 0,

    /// <summary>Open ground.</summary>
    Outside = 1,

    /// <summary>Inside a generated location.</summary>
    Inside = 2,
}
