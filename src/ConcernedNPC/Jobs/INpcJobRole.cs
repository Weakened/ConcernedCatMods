using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Planning;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Jobs;

/// <summary>Everything a role supplies to a job, and nothing else.
///
/// <b>Public because it is the parameterisation.</b> This interface is the whole
/// of "the role decides what, the library decides how": eligible targets, their
/// priorities, what each one takes, what the containers hold, whether a thing is
/// still worth doing, and the ground. <see cref="NpcJobDriver"/> supplies the
/// order those are used in, and nothing here can change that order.
///
/// <b>Asked again every round, on purpose.</b> A job is long enough for the
/// player to chop the tree himself, empty the chest, and walk away from the
/// zone. The driver takes a fresh snapshot for every round rather than planning
/// once against a world it saw at the start, and that is what makes a job that
/// was interrupted able to carry on rather than having to be cancelled.
///
/// <b>Nothing here may throw for a world reason.</b> The driver calls these from
/// inside a pump that does not catch, so an implementation that cannot answer
/// says so - an empty list, <see cref="StopStatus.Unreadable"/>, a null probe -
/// rather than taking the NPC out. A throw out of
/// <see cref="IStopObserver.Observe"/> is nevertheless caught and read as
/// unreadable, because one role's broken completion condition must not stop an
/// unrelated NPC in another product; a throw out of
/// <see cref="Candidates"/> or <see cref="Sources"/> is not, because a role that
/// cannot list its own work has nothing for this library to sequence.</summary>
public interface INpcJobRole : IStopObserver
{
    /// <summary>Everything that might be worth doing, with the place, the
    /// priority and what doing it takes already decided.
    ///
    /// The library never asks why one of these is wanted, never re-ranks them,
    /// and never invents one. It filters them by the work area, asks
    /// <see cref="IStopObserver.Observe"/> about each, probes the ground, totals
    /// what they need, splits them into trips and orders the walk.</summary>
    /// <param name="world">The world load the names must be minted in. A
    /// candidate from another load is refused rather than resolved.</param>
    IReadOnlyList<JobTarget> Candidates(NpcWorldEpoch world);

    /// <summary>The containers this job may draw from, and what each was seen to
    /// hold just now.
    ///
    /// Empty is an answer and a common one: a job whose targets consume nothing
    /// offers no sources and must not be refused for it. What was seen is the
    /// role's own reading and goes stale exactly as fast as everything else in a
    /// snapshot; nothing in this library withdraws anything on the strength of
    /// it, and permission is re-read at the moment of use through
    /// <c>INpcContainer.Access</c>.</summary>
    IReadOnlyList<SourceStock> Sources(NpcWorldEpoch world);

    /// <summary>The ground, or null to take the candidates' own places on trust.
    ///
    /// <b>Null is a decision with a cost.</b> A role whose targets are world
    /// objects it has just read has already proved they exist, and a probe would
    /// buy it nothing; a role proposing places has not, and without a probe it
    /// will walk an NPC at a point in the water. Optional because the first is
    /// a real case, not because the second is acceptable.</summary>
    INpcAreaProbe? Probe { get; }

    /// <summary>What other jobs have already set aside in the containers this
    /// one is about to plan against, or null to plan on the raw counts.
    ///
    /// <b>Null is the right answer for a role that keeps its own material
    /// accounting</b> - and it is what a role must pass while its accounting
    /// lives somewhere this library cannot see, which is every role today. The
    /// cost, stated rather than discovered: two jobs planned a tick apart both
    /// plan the same forty nails, both are told the material is there, and the
    /// second stalls when it arrives at an empty chest. Nothing here can detect
    /// that; supplying this is the only fix.</summary>
    INpcSourceAvailability? Availability { get; }
}
