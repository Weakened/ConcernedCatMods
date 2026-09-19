using System;
using TheConcernedCat.ConcernedNPC.Routing;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One thing a job has to be done to: a lamp, a tree, a wall section, a
/// haul point. The role's own token, its place, how badly it is wanted, what
/// doing it takes, and what doing it is called.
///
/// <b>Everything about it is opaque here.</b> <see cref="Key"/>,
/// <see cref="Action"/> and every item inside <see cref="Needs"/> are the role's
/// strings. This library counts them, totals them, groups them into tours,
/// orders them and reports which were skipped. It never branches on what any of
/// them mean, and a <c>switch</c> over one of these strings anywhere in this
/// package is the defect the package is defined against.
///
/// <b>Why the need is a manifest and not a number.</b> Because a wall section
/// takes wood <i>and</i> nails, and a job that totals only units cannot tell a
/// chest holding both from two chests holding one each - which is the single
/// judgement <see cref="SourceSelector"/> exists to make. The per-target
/// manifest summed over the targets is the job manifest, and that is the whole
/// of "the manifest is calculated before execution".
///
/// <b>Why it is epoch-scoped.</b> A target names a world object, and a world
/// load renumbers every one of them. It is a reservation subject, so it has to
/// be able to be refused when the name stops meaning anything -
/// <see cref="INpcEpochScoped"/> is what makes that a check rather than a
/// hope.</summary>
public readonly struct JobTarget : INpcEpochScoped, IEquatable<JobTarget>
{
    public JobTarget(
        string? key,
        NpcWorldEpoch epoch,
        NpcPoint at,
        string? action,
        int priority,
        JobManifest needs)
    {
        Key = key ?? string.Empty;
        Epoch = epoch;
        At = at;
        Action = action ?? string.Empty;
        Priority = priority;
        Needs = needs;
    }

    /// <summary>The role's token for this target. Stable for as long as the
    /// world load is, because half of every reservation over it is this name and
    /// the whole of the route's tie-break is.</summary>
    public string Key { get; }

    /// <summary>The world load this name was minted in.</summary>
    public NpcWorldEpoch Epoch { get; }

    /// <summary>Where it is.</summary>
    public NpcPoint At { get; }

    /// <summary>The role's token for what is done to it. Empty means "whatever
    /// this job's ordinary action is", which the planner supplies from the
    /// role's vocabulary.</summary>
    public string Action { get; }

    /// <summary>Higher is serviced sooner. The role has already weighed the walk
    /// against the urgency; nothing here second-guesses it.</summary>
    public int Priority { get; }

    /// <summary>What doing this one takes. Empty for a target that consumes
    /// nothing - collecting is a job with targets and no manifest, and it must
    /// not be refused for being unprovisioned.</summary>
    public JobManifest Needs { get; }

    /// <summary>A target that can be planned: it has a name, a place anybody
    /// could compute, and a world it belongs to.</summary>
    public bool IsValid => Key.Length != 0 && At.IsFinite && !Epoch.IsUnknown;

    /// <summary>How many units of material this one target takes.</summary>
    public int Units => Needs.TotalUnits;

    /// <summary>The same target as far as the route is concerned: a name, a
    /// place, its priority, and reachable until something says otherwise.
    /// <b>The projection is how there is one revalidation seam in this package
    /// instead of two</b> - a role answers <see cref="IStopObserver"/> about a
    /// stop, and the planner and the walk both ask the same question the same
    /// way.</summary>
    internal RouteStop AsStop() => new RouteStop(Key, At, Priority, true);

    /// <summary>Two targets are the same target when they have the same name in
    /// the same world load. The place is not part of it, because a target that
    /// moved is still that target - <see cref="StopStatus.Moved"/> is how a
    /// mover is noticed, and folding the place into identity would make it a
    /// different target and quietly service it twice.</summary>
    public bool Equals(JobTarget other) =>
        string.Equals(Key, other.Key, StringComparison.Ordinal) && Epoch.Equals(other.Epoch);

    public override bool Equals(object? obj) => obj is JobTarget other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (StringComparer.Ordinal.GetHashCode(Key) * 397) ^ Epoch.GetHashCode();
        }
    }

    public override string ToString() => Key.Length == 0 ? "<no target>" : Key + " " + At;
}
