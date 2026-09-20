using System;

namespace TheConcernedCat.ConcernedNPC.Work;

/// <summary>The area a job was accepted against, frozen at acceptance.
///
/// <b>Frozen is the whole idea.</b> A later edit to the designation, the map or
/// the bed never silently moves or widens work already under way. The job
/// carries the area it agreed to, its revision at that moment, and the world
/// load it was named in, and revalidates all three at a safe checkpoint. This is
/// the shipped <c>WorkScope</c>'s contract, kept exactly, with the circle
/// replaced by a shape nobody here has to know.
///
/// <b>Why the id travels with it.</b> So a job resumed after a reload can name
/// what it was working in without holding the rebuilt object across a load. The
/// role re-resolves the id through the registry and gets either the same area or
/// an honest refusal - and a refusal is the job pausing, never the job widening.
/// </summary>
internal sealed class NpcWorkAreaCommitment
{
    internal NpcWorkAreaCommitment(NpcWorkAreaId id, INpcWorkArea area, NpcWorldEpoch world)
    {
        if (area == null)
        {
            throw new ArgumentNullException(nameof(area), "A job is committed to an area or to nothing.");
        }

        if (!id.IsNamed)
        {
            throw new ArgumentException(
                "A committed area has to be nameable, or a job resumed after a reload has nothing to ask the " +
                "registry for and the only way back is to guess.",
                nameof(id));
        }

        if (world.IsUnknown)
        {
            throw new ArgumentException(
                "A commitment belongs to one world load, and an unknown epoch matches nothing.", nameof(world));
        }

        Id = id;
        Area = area;
        World = world;
        Revision = area.Revision;
    }

    internal NpcWorkAreaId Id { get; }

    /// <summary>The area as it was when the job started. Containment questions
    /// go here and nowhere else.</summary>
    internal INpcWorkArea Area { get; }

    /// <summary>Its revision at acceptance, compared against the world's now.
    /// Held rather than re-read from <see cref="Area"/> so that an area object a
    /// role mutates in place still shows up as changed.</summary>
    internal int Revision { get; }

    /// <summary>The world load this commitment was made in.</summary>
    internal NpcWorldEpoch World { get; }

    /// <summary>May the job work at this point? The committed area's own answer,
    /// never the bounding circle's.</summary>
    internal bool Contains(NpcPoint point)
    {
        try
        {
            return Area.Contains(point);
        }
        catch (Exception)
        {
            // An area that cannot answer has not said yes. A throwing Contains
            // reaches the checkpoint as an invalid area on its next pass; until
            // then it refuses every point, which is a worker who does nothing.
            return false;
        }
    }
}

/// <summary>Whether the area a job started against still means what it meant -
/// asked at a safe point, before a reservation, a pick or a delivery leg.
///
/// <b>It only ever answers.</b> It never moves anything, never widens anything,
/// and has no path from an invalid area to a default one. That absence is the
/// contract: the shipped scope checkpoint states it in a comment, and here it is
/// structural, because there is no radius in scope to fall back to.
///
/// Carried from the shipped <c>ScopeCheckpoint.Revalidate</c> with one addition:
/// the observation now carries the world it was taken in, so "the world it
/// belonged to is not this one" is derivable rather than assumed. The shipped
/// version compares an epoch the caller passed in beside the observation; the
/// difference is that a caller can no longer state a world the observation does
/// not have.</summary>
internal static class NpcWorkAreaCheckpoint
{
    /// <summary>Judges one observation against one commitment.
    ///
    /// Order matters and is the shipped order: identity and existence first
    /// (<see cref="WorkAreaState.Invalid"/>, fail closed), then geometry
    /// (<see cref="WorkAreaState.Changed"/>, pause for a person), then loading
    /// (<see cref="WorkAreaState.Unloaded"/>, ask again). Unloaded last, because
    /// an area that is gone is gone whether or not its ground happens to be
    /// streamed in.</summary>
    internal static WorkAreaState Judge(NpcWorkAreaCommitment? accepted, WorkAreaObservation now)
    {
        if (accepted == null)
        {
            return WorkAreaState.Invalid;
        }

        if (!now.World.Matches(accepted.World))
        {
            // Covers both halves deliberately: a different world load, and an
            // observation taken before any world was loaded. An unknown epoch
            // matches nothing, which is the fail-closed answer.
            return WorkAreaState.Invalid;
        }

        if (!now.SourcePresent)
        {
            // "Could not be asked" is not "is still there". An adapter that
            // could not read the designation book has not proved the area
            // exists, and proving it is the only thing that lets work continue.
            return WorkAreaState.Invalid;
        }

        if (now.CurrentRevision != accepted.Revision)
        {
            return WorkAreaState.Changed;
        }

        return now.AnyGroundLoaded ? WorkAreaState.Valid : WorkAreaState.Unloaded;
    }

    /// <summary>Whether work may go on right now. True for
    /// <see cref="WorkAreaState.Valid"/> alone - stated as a method so no caller
    /// writes the "and unloaded is probably fine" version of it.</summary>
    internal static bool MayWork(WorkAreaState state) => state == WorkAreaState.Valid;
}
