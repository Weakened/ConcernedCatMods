using System;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Storage;

/// <summary>The containers one job was told to use: a source, a destination, or
/// both. Named, explicitly, by whoever ordered the job.
///
/// <b>What it guarantees: a job uses the containers it was given and no
/// others.</b> There is nothing here that searches, ranks, or picks. A job with
/// no destination cannot deposit anywhere - not into the closest chest, not into
/// the one it took from, not into a chest that happens to be enabled. That is
/// the second forbidden behaviour in this leaf, and the guarantee is structural:
/// this type holds two references and offers no way to get a third.
///
/// <b>And the designation does not override the permission.</b> Being told to
/// use a chest is not being allowed to use it. Every authorisation goes through
/// the same gate as any other, so a source the player marked deposit-only
/// refuses the take and says which way round it is. The player's switch outranks
/// the order, in both directions and always, because the alternative is an order
/// being a way to reach a chest the player closed.</summary>
internal sealed class NpcContainerAssignment
{
    internal NpcContainerAssignment(INpcContainer? source, INpcContainer? destination)
    {
        if (source == null && destination == null)
        {
            throw new ArgumentException(
                "An assignment with neither a source nor a destination is not an assignment; a job that has " +
                "not been told which containers to use has not been told to move anything.",
                nameof(source));
        }

        Source = source;
        Destination = destination;
    }

    /// <summary>Where this job takes from, or null when it takes from
    /// nowhere.</summary>
    internal INpcContainer? Source { get; }

    /// <summary>Where this job puts things, or null when it keeps them.</summary>
    internal INpcContainer? Destination { get; }

    internal bool HasSource => Source != null;

    internal bool HasDestination => Destination != null;

    /// <summary>Asks to take from the designated source.</summary>
    internal NpcContainerAuthorization AuthorizeTake(NpcWorldEpoch world) =>
        Source == null
            ? NpcContainerAuthorization.Refused(
                NpcContainerRefusal.Gone,
                NpcContainerUse.Take,
                "this job names no container to take from, and there is no such thing as the nearest one")
            : NpcContainerGate.Authorize(Source, NpcContainerUse.Take, world);

    /// <summary>Asks to put things into the designated destination.</summary>
    internal NpcContainerAuthorization AuthorizeDeposit(NpcWorldEpoch world) =>
        Destination == null
            ? NpcContainerAuthorization.Refused(
                NpcContainerRefusal.Gone,
                NpcContainerUse.Deposit,
                "this job names no container to put things into, so the materials stay with him")
            : NpcContainerGate.Authorize(Destination, NpcContainerUse.Deposit, world);
}
