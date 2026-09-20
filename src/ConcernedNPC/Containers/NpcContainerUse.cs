using System;

namespace TheConcernedCat.ConcernedNPC.Containers;

/// <summary>What an NPC may do with one container, decided per container by the
/// player.
///
/// <b>Off is zero, and that is the whole design.</b> A container nobody has
/// spoken about is a container an NPC may not touch - not read, not take from,
/// not put into. Every way of arriving at this value without a deliberate
/// choice - a defaulted struct, a dictionary miss, a field never assigned, a
/// row that failed to parse - lands on <see cref="Off"/>. A player's chests are
/// theirs until they say otherwise, once, per chest.
///
/// <b>Why four values and not a boolean.</b> Because take and deposit are
/// different trusts. A player will happily let an NPC put firewood into the
/// shed without letting it help itself to the contents, and the shipped Steward
/// already needs both halves separately - it withdraws from a depot and deposits
/// the remainder back. A single "enabled" flag forces the generous reading of
/// both.
///
/// <b>The precedent this follows.</b> Door permissions: off per door by default,
/// with one global setting that opts every door in at once, and a door the
/// player has not allowed is never opened - not even to close it again. The
/// owner approved that shape for doors; containers are the same question about a
/// different world object, and the answer had better be the same shape.
///
/// <b>What this value is not.</b> It is not permission on its own. A container
/// the player enabled can still be refused for a ward, a privacy setting, an
/// owner that is not this process, somebody having it open, or distance - see
/// <see cref="NpcContainerRefusal"/>. This says what the player allows; that
/// says what the world allows; an NPC needs both.</summary>
[Flags]
public enum NpcContainerUse
{
    /// <summary>Not enabled for NPCs. The default, and what every unknown
    /// answer becomes.</summary>
    Off = 0,

    /// <summary>An NPC may take from this container.</summary>
    Take = 1 << 0,

    /// <summary>An NPC may put things into this container.</summary>
    Deposit = 1 << 1,

    /// <summary>Both. Written out rather than left to the caller to compose, so
    /// the four values a player chooses between are the four values in the
    /// type.</summary>
    Both = Take | Deposit,
}
