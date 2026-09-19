using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>Everything a planner is given: who, where, in which world load, and
/// what the job is for.
///
/// <b>What it guarantees.</b> That a plan is a pure function of what is in here.
/// A planner reads nothing else - no clock it did not receive, no world it was
/// not handed a probe for, no configuration of its own - so the same request
/// produces the same plan, in a test without the game installed and in a
/// session with it.</summary>
internal readonly struct JobPlanRequest
{
    internal JobPlanRequest(
        NpcIdentity identity,
        string jobId,
        INpcWorkArea? area,
        NpcWorldEpoch epoch,
        JobManifest wanted,
        NpcPoint startingFrom)
    {
        Identity = identity;
        JobId = jobId ?? string.Empty;
        Area = area;
        Epoch = epoch;
        Wanted = wanted;
        StartingFrom = startingFrom;
    }

    /// <summary>Which NPC the job is for.</summary>
    internal NpcIdentity Identity { get; }

    /// <summary>The job, as the runtime names it. Half of every reservation id
    /// the plan's steps will take out, so it is stable for the life of the job
    /// and the same after a restart.</summary>
    internal string JobId { get; }

    /// <summary>Where the NPC may work. Never null for a plannable request; a
    /// null one is refused with <see cref="JobPlanVerdict.AreaInvalid"/> rather
    /// than defaulting to anywhere.</summary>
    internal INpcWorkArea? Area { get; }

    /// <summary>The world load this plan will belong to.</summary>
    internal NpcWorldEpoch Epoch { get; }

    /// <summary>What the job is for, in total. The role computed it; this
    /// library plans how to satisfy it.</summary>
    internal JobManifest Wanted { get; }

    /// <summary>Where the NPC is standing now, so the order of the steps is
    /// sensible from where it actually is.</summary>
    internal NpcPoint StartingFrom { get; }
}

/// <summary>Working out the whole job before any of it is done.
///
/// <b>What it guarantees.</b> Three things.
///
/// <i>It never throws for a world reason.</i> A planner answers a verdict.
/// Planning runs inside a driver that does not catch, so an exception out of it
/// takes the NPC out entirely - which is how a fault in one product's planner
/// would stop an unrelated NPC in another.
///
/// <i>It mutates nothing.</i> Planning takes no reservations, moves no items,
/// writes no journal row and changes no world state. A plan that is never acted
/// on leaves no trace to clean up, which is what makes it safe to re-plan on
/// every interruption.
///
/// <i>It claims no optimality.</i> The answer is bounded, deterministic and
/// visibly sensible. The shipped selector says this of itself in as many words,
/// and it is the right promise: a player forgives an NPC that takes a slightly
/// long way round, and does not forgive one that freezes while it thinks.
///
/// <b>Who implements it.</b> A leaf of this library, per kind of job - not a
/// role. What a step means stays opaque; what order steps go in, how a job is
/// provisioned in batches, and what happens when it is interrupted are the
/// shared parts, and they are the reason this package exists.</summary>
internal interface IJobPlanner
{
    /// <summary>Plans the whole job, or explains why it cannot. Never throws for
    /// a world reason.</summary>
    JobPlan Plan(in JobPlanRequest request);
}
