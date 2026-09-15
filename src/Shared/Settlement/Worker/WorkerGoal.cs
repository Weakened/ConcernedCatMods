using System;

namespace TheConcernedCat.Settlement.Worker;

/// <summary>One designated point a worker has been told to stand at.
///
/// A goal is a destination and nothing else. It carries no behaviour to perform
/// on arrival, because CF-SET-002 is deliberately only "move and stop" — the
/// leaves that fell a tree or place a piece attach their own work to an arrival,
/// and keeping that out of here is what stops the spike from quietly becoming
/// the whole runtime.</summary>
internal readonly struct WorkerGoal : IEquatable<WorkerGoal>
{
    public WorkerGoal(SitePoint point, float arrivalTolerance)
    {
        if (!(arrivalTolerance > 0f) || float.IsInfinity(arrivalTolerance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(arrivalTolerance), arrivalTolerance,
                "Arrival tolerance must be a positive, finite number of metres. " +
                "A zero tolerance can never be satisfied by a moving body.");
        }

        Point = point;
        ArrivalTolerance = arrivalTolerance;
    }

    public SitePoint Point { get; }

    /// <summary>How close counts as arrived, on the ground plane.
    ///
    /// Positive by construction: a body with width and momentum cannot be
    /// asked to stand at a mathematical point, and a tolerance of zero would
    /// turn every order into a permanent near-miss.</summary>
    public float ArrivalTolerance { get; }

    /// <summary>The default standing distance: two metres, which is roughly a
    /// humanoid's own radius plus a step, and wide enough that arrival is not
    /// sensitive to where exactly the pathfinder puts the last node.</summary>
    public static WorkerGoal At(SitePoint point) => new(point, 2f);

    public bool Equals(WorkerGoal other)
    {
        return Point.Equals(other.Point)
            && ArrivalTolerance.Equals(other.ArrivalTolerance);
    }

    public override bool Equals(object? obj) => obj is WorkerGoal other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (Point.GetHashCode() * 31) + ArrivalTolerance.GetHashCode();
        }
    }

    public override string ToString() => $"{Point} +/-{ArrivalTolerance:0.##}m";
}

/// <summary>What the game adapter can see this tick.
///
/// Every field is something the adapter must actually establish. They are
/// passed in rather than queried so the planner stays engine-free and so a test
/// can construct any combination, including the ones that are hard to arrange
/// in a live world — no authority, hazardous ground, an unloaded goal.</summary>
internal readonly struct WorkerObservation
{
    public WorkerObservation(
        SitePoint position,
        bool hasPath,
        bool hasAuthority,
        bool goalInLoadedGround,
        bool goalIsHazardous)
    {
        Position = position;
        HasPath = hasPath;
        HasAuthority = hasAuthority;
        GoalInLoadedGround = goalInLoadedGround;
        GoalIsHazardous = goalIsHazardous;
    }

    public SitePoint Position { get; }

    /// <summary>True when the adapter currently holds a usable path to the
    /// goal — in the game adapter, what <c>BaseAI.HavePath</c> answered.</summary>
    public bool HasPath { get; }

    /// <summary>True only when this peer owns the actor and an authorised host
    /// is running the settlement. False is the safe default: the planner
    /// refuses rather than assuming, which is the authority ADR's rule that an
    /// unsupported check becomes a refusal.</summary>
    public bool HasAuthority { get; }

    /// <summary>True when the goal is inside loaded ground. The first proof
    /// does no offscreen work and reports the limit instead of hiding it.</summary>
    public bool GoalInLoadedGround { get; }

    /// <summary>True when the adapter established the goal is dangerous to
    /// stand at.</summary>
    public bool GoalIsHazardous { get; }

    /// <summary>The ordinary case: owned, loaded, safe, no path yet.</summary>
    public static WorkerObservation Ready(SitePoint position, bool hasPath = false)
    {
        return new WorkerObservation(
            position,
            hasPath,
            hasAuthority: true,
            goalInLoadedGround: true,
            goalIsHazardous: false);
    }
}
