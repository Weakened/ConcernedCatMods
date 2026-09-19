using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Containers;

/// <summary>One container an NPC might use, as this library is allowed to see
/// it: an identity, a place, and whether it may be used right now.
///
/// <b>What it guarantees.</b> That permission is asked at the moment of use and
/// never cached into a decision made earlier. <see cref="Access"/> is a property
/// evaluated on each read, exactly as the shipped port is, because every
/// component of it can change between the tick that chose this chest and the
/// tick that opens it: the player can walk into it, a ward can go up, the
/// container can be destroyed, ownership can migrate.
///
/// <b>What it guarantees about identity.</b> That
/// <see cref="Key"/> is only meaningful with <see cref="Epoch"/>. A world load
/// renumbers every saved object, so a key from a previous load names a
/// different thing, and an NPC acting on it takes from whatever now answers.
/// Anything storing a container across a session stores something else as well -
/// where it stood and what kind of thing it is - and matches on that. What
/// exactly, and how much tolerance, is the container leaf's decision, and it is
/// not made here.
///
/// <b>What is deliberately absent: the inventory.</b> No count, no add, no
/// remove. Moving items is a custody question - a reservation, an intent, a
/// receipt, and a ledger that can say "this may or may not have happened" -
/// and half of that contract is worse than none: a port with an
/// <c>Add</c> and no ledger is an invitation to mint items on a retry. The port
/// arrives with the ledger, in the custody leaf.</summary>
public interface INpcContainer : INpcEpochScoped
{
    /// <summary>How the role names this container, opaque here. Only meaningful
    /// together with the <see cref="INpcEpochScoped.Epoch"/> it is named in -
    /// which is why a container is an epoch-scoped subject, and therefore
    /// something a reservation book can hold and can refuse when the world has
    /// moved on.</summary>
    string Key { get; }

    /// <summary>Where it stands. For deciding whether to walk over and for
    /// telling a player which chest is meant - never for deciding which
    /// container this is.</summary>
    NpcPoint Position { get; }

    /// <summary>What to call it in a sentence shown to a player - "the supply
    /// chest", "the chest by the workbench".</summary>
    string Describe { get; }

    /// <summary>What the player allows and what the world allows, as of now.
    /// Re-read before every use; never stored and acted on later.</summary>
    NpcContainerAccess Access { get; }
}
