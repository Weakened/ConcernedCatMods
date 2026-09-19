using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Custody;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Reservations;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Jobs;

/// <summary>The one set of reservation books every job in a world shares.
///
/// <b>Internal on purpose, and this is the reason a role hands in no books.</b>
/// A reservation is only worth anything if the two jobs that want the same tree
/// ask the same book. If a role supplied its own, two roles in one process would
/// each hold a book nobody else consults, every reservation would succeed, and
/// the whole mechanism would be decoration that cost a frame. So the books are
/// the library's, keyed by world load, and a driver finds them rather than being
/// given them - which is also what keeps
/// <see cref="IReservationBook{TSubject}"/>, <c>ReservationId</c> and
/// <c>ReservationOutcome</c> off the public surface.
///
/// <b>One world at a time.</b> Asking for a different world replaces the set:
/// every name in the old one was minted in a scene that has gone, and an
/// epoch-scoped book would refuse them all anyway. Holding exactly one set is
/// what stops a process that has loaded forty worlds holding forty books.
///
/// <b>The comparer is not optional.</b> A container is a wrapper a role re-reads
/// each tick, so a book that compared them by reference would miss, grant the
/// reservation again, and hand one chest to two jobs that each believe they are
/// the only holder. Targets are a value and compare as one.</summary>
internal sealed class NpcJobBooks
{
    private static NpcJobBooks? _current;

    private NpcJobBooks(NpcWorldEpoch world)
    {
        World = world;
        Targets = new NpcReservationBook<JobTarget>(world);
        Containers = new NpcReservationBook<INpcContainer>(
            world, NpcSubjectComparer.ByKey<INpcContainer>(container => container?.Key));
    }

    /// <summary>The world load these books belong to.</summary>
    internal NpcWorldEpoch World { get; }

    /// <summary>Who has the right to act on which target.</summary>
    internal IReservationBook<JobTarget> Targets { get; }

    /// <summary>Who has the right to draw from which container.</summary>
    internal IReservationBook<INpcContainer> Containers { get; }

    /// <summary>The books for one world load. The same instance for the same
    /// world, so two jobs conflict; a fresh one the moment the world
    /// changes.</summary>
    internal static NpcJobBooks ForWorld(NpcWorldEpoch world)
    {
        NpcJobBooks? current = _current;
        if (current != null && current.World.Equals(world))
        {
            return current;
        }

        var fresh = new NpcJobBooks(world);
        _current = fresh;
        return fresh;
    }

    /// <summary>Drops the books. For a test that must not inherit another
    /// test's holds; never called by the runtime, which lets a world change do
    /// it.</summary>
    internal static void Forget() => _current = null;
}
