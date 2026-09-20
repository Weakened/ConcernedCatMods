namespace TheConcernedCat.ConcernedNPC.Custody;

/// <summary>Where a real unit of material physically is. <b>Every unit the
/// ledger knows about is in exactly one of these</b>, and a reservation, an
/// estimate or a journal row is not a place.
///
/// <b>Why the list is short, and shorter than the one it came from.</b> The
/// shipped settlement ledger distinguishes a cart from a chest. This library
/// may not: a cart is a thing a particular mod knows about, and the moment this
/// enum names one, every role inherits a concept most of them have no use for.
/// What the library actually reasons about is three properties - can material
/// leave here, can this place be counted, and is being here the end of the
/// story - and the six values below are exactly the distinctions those three
/// questions need. A role that wants to tell two stored places apart does so
/// with the location's key, which is its own.</summary>
internal enum NpcCustodyPlace
{
    /// <summary>Nobody said. Never a valid location.</summary>
    Unspecified = 0,

    /// <summary>Lying in the world, traceable to this job's own action. Only
    /// what the job itself caused to be there - never anything else that
    /// happens to be on the floor.</summary>
    Ground = 1,

    /// <summary>In the NPC's own inventory. <b>The place that can vanish</b>:
    /// a death, a despawn, a zone unload. That is why the ledger and not the
    /// inventory is the truth.</summary>
    Carried = 2,

    /// <summary>In some container the job is using - a chest, a depot, a
    /// vehicle's hold. Which one is the location's key.</summary>
    Stored = 3,

    /// <summary>Arrived where the job was taking it. <b>Terminal and credited
    /// once</b>: the job never moves it again, and later changes to that
    /// container are the player's business, never a shortfall.</summary>
    Delivered = 4,

    /// <summary>Handed to the player. Terminal.</summary>
    Handed = 5,

    /// <summary>Observed gone. Terminal, recorded, and never silently reversed:
    /// material that reappears is a finding for a person, not a refund.
    /// </summary>
    Lost = 6,
}
