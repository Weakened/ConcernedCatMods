using System;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>One place on an errand, as the ordering and the walking need to see
/// it: a name, a place, how badly it is wanted, and whether it can be got to at
/// all.
///
/// <b>Opaque on purpose.</b> <see cref="Key"/> is the role's own token. This
/// library orders stops, drops the ones that cannot be used, walks between them
/// and reports which were skipped; it never branches on what a stop is. Nothing
/// here knows that one of them is a chest.
///
/// <b>Priority is a number, not a meaning.</b> Higher is wanted sooner, and
/// stops of equal priority are ordered by where they are. The role decides what
/// the numbers mean - a lamp about to go out, a target the player asked for
/// first - and hands them in already decided.</summary>
public readonly struct RouteStop : IEquatable<RouteStop>
{
    internal RouteStop(string? key, NpcPoint at, int priority, bool isReachable)
    {
        Key = key ?? string.Empty;
        At = at;
        Priority = priority;
        IsReachable = isReachable;
    }

    /// <summary>The role's token for this stop. Also the tie-break when two
    /// stops are the same distance away, which is why it must be stable for the
    /// life of a round: the same inputs give the same route only if the
    /// tie-break is the same.</summary>
    public string Key { get; }

    /// <summary>Where it is.</summary>
    public NpcPoint At { get; }

    /// <summary>Higher is visited sooner. Equal priorities are ordered by
    /// travel.</summary>
    public int Priority { get; }

    /// <summary>Whether the role believes this stop can be got to at all.
    /// <b>False drops it from the route entirely</b> rather than leaving it to
    /// be discovered by walking into it, because the only thing worse than a
    /// long route is a long route that ends in a wall.</summary>
    public bool IsReachable { get; }

    /// <summary>A stop that can be ordered: it has a name and a place anyone
    /// could compute. A nameless stop is refused rather than given the empty
    /// key, because the empty key is what a defaulted struct has and two of
    /// those would be the same stop.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Key) && At.IsFinite;

    /// <summary>Stops are the same stop when they have the same name. The place
    /// is deliberately not part of it: a stop that moved a little is still that
    /// stop, and <see cref="StopStatus.Moved"/> is how a mover is noticed.
    /// </summary>
    public bool Equals(RouteStop other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is RouteStop other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Key.Length == 0 ? "<no stop>" : Key + " " + At;
}

/// <summary>What a stop turned out to be when it was looked at again, just
/// before going there.
///
/// <b>Six answers where a naive loop has two.</b> Every one of these is a reason
/// not to walk to a stop, and the right thing to do afterwards differs for each:
/// a job whose targets were all serviced by the player is finished, a job whose
/// targets are all unreadable has not started. Collapsing them is how an NPC
/// declares a round complete because a zone had not streamed in.</summary>
public enum StopStatus
{
    /// <summary>The role could not tell. <b>Never a yes and never a no</b>: the
    /// stop is left for the next round rather than counted as done or dropped
    /// as gone.</summary>
    Unreadable = 0,

    /// <summary>Still worth going to.</summary>
    Actionable = 1,

    /// <summary>Somebody already did it - the resource was collected, the lamp
    /// was refuelled, the wall was built. Skip it and <b>keep the
    /// material</b>.</summary>
    AlreadyDone = 2,

    /// <summary>It is not there any more. Skip it; the material it was for stays
    /// carried, and the reconciliation at the end of the round is what decides
    /// where it goes.</summary>
    Gone = 3,

    /// <summary>It is still there and it is somewhere else. Skip it this round
    /// and let it be offered again next round, at the place it is now: acting on
    /// the old place is acting on whatever is standing there instead.</summary>
    Moved = 4,

    /// <summary>It exists, it is where it was, and it cannot be got to from
    /// here.</summary>
    Unreachable = 5,

    /// <summary>Something refuses it that is neither the world nor the distance -
    /// a ward, a privacy setting, a permission the player has not given. Skipped
    /// exactly like the others, and named apart because the sentence a player
    /// reads has to tell them what to change.</summary>
    Refused = 6,
}

/// <summary>Asking the role whether a stop is still worth going to.
///
/// <b>The one place a role's completion condition enters this library.</b> What
/// "done" means for a lamp, a tree, a wall or a chest is the role's business
/// entirely; whether a stop is skipped, deferred or walked to is this library's.
/// That split is the reason this interface has one method and returns an enum
/// with no payload.
///
/// <b>It is asked before every stop, not once at the start.</b> A round is long
/// enough for the player to chop the tree himself, for a creature to knock the
/// lamp down, and for a zone to unload underneath both. Asking once and trusting
/// the answer for the rest of the round is the defect this seam exists to
/// prevent.
///
/// <b>It must not throw for a world reason.</b> An observer that cannot answer
/// returns <see cref="StopStatus.Unreadable"/>. This is called from inside a
/// driver that does not catch, so an exception here takes the NPC out entirely.
/// <see cref="RouteExecution"/> nevertheless treats a throwing observer as
/// unreadable rather than letting it escape, because one role's broken
/// completion condition must not stop an unrelated NPC in another
/// product.</summary>
public interface IStopObserver
{
    /// <summary>Is this stop still worth going to, as of now.</summary>
    StopStatus Observe(in RouteStop stop);
}
