using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>Where an NPC is asked to walk, and from where.
///
/// <b>What it guarantees.</b> That a route request carries everything the
/// planner may use. There is no ambient position, no implicit "current" goal and
/// no clock of the planner's own: the caller passes its own time, exactly as
/// every bounded retry and deadline in this repository does, so a test can
/// advance time without waiting.</summary>
internal readonly struct RouteRequest
{
    internal RouteRequest(NpcPoint from, NpcPoint to, float arrivalToleranceMetres, int revision)
    {
        From = from;
        To = to;
        ArrivalToleranceMetres = arrivalToleranceMetres;
        Revision = revision;
    }

    /// <summary>Where the NPC is standing.</summary>
    internal NpcPoint From { get; }

    /// <summary>Where it should end up.</summary>
    internal NpcPoint To { get; }

    /// <summary>How close counts as arrived. Part of the request because it
    /// depends on what is being done there - reaching a chest is not the same
    /// distance as reaching a tree.</summary>
    internal float ArrivalToleranceMetres { get; }

    /// <summary>The revision of whatever produced this request, carried through
    /// to the plan so a goal from a superseded request can be recognised and
    /// dropped rather than followed.</summary>
    internal int Revision { get; }
}

/// <summary>Why a route is what it is. <b>The verdict a player is told</b> - one
/// sentence each, never an enum name.
///
/// A finer diagnostic vocabulary belongs beside the planner that produces it,
/// mapped onto exactly one of these. That two-level split is the shipped
/// pattern: thirty-odd findings for a log, ten verdicts for a person.</summary>
internal enum RouteVerdict
{
    /// <summary>Nobody planned. Never a route.</summary>
    Unspecified = 0,

    /// <summary>A route the NPC can walk.</summary>
    Suitable = 1,

    /// <summary>There is no way through.</summary>
    NoPath = 2,

    /// <summary>Further than this NPC plans for in one go. Measuring distance is
    /// free and asking the navigation mesh is not, so this is answered without
    /// spending a query.</summary>
    TooFar = 3,

    /// <summary>Either end is in ground that is not loaded. Ask again when the
    /// player is nearer; never a permanent no.</summary>
    OutsideLoadedArea = 4,

    /// <summary>The destination is somewhere an NPC would be hurt standing -
    /// fire, lava, deep water.</summary>
    Hazardous = 5,

    /// <summary>This walk failed recently and is still being avoided. <b>Not the
    /// same as no path</b>: the navigation mesh often believes in a gap a body
    /// does not fit through, and the only evidence that it is wrong is having
    /// tried. Refusing the same destination from the same heading for a while,
    /// while allowing the same place approached another way, is what turns
    /// getting stuck into going round.</summary>
    RefusedRecently = 6,

    /// <summary>The planner's query budget is spent for now. <b>Ask again
    /// later, and never a reason to stop a job</b> - a spent budget says
    /// nothing at all about whether a route exists.</summary>
    BudgetExhausted = 7,

    /// <summary>The request itself is not usable: a non-finite point, a negative
    /// tolerance.</summary>
    InvalidRequest = 8,
}

/// <summary>A route, or the reason there is not one.
///
/// <b>What it guarantees.</b> That a refusal cannot be walked. Every consumer
/// asks <see cref="IsSuitable"/>, which requires both a suitable verdict and at
/// least two waypoints, so a "suitable" plan with an empty path - the shape a
/// partially-initialised result takes - is not followable.
///
/// <b>Visibly sensible, not optimal.</b> A route is bounded, deterministic and
/// good enough that a player watching does not wince. It is explicitly not the
/// shortest path, and no leaf of this library is permitted to spend an unbounded
/// search making it one: an NPC that hitches for a quarter of a second every few
/// steps is worse than an NPC that walks a little further.</summary>
internal readonly struct RoutePlan
{
    private readonly NpcPoint[]? _waypoints;

    internal RoutePlan(
        RouteVerdict verdict,
        IReadOnlyList<NpcPoint>? waypoints,
        float lengthMetres,
        int requestRevision,
        int planRevision)
    {
        Verdict = verdict;
        LengthMetres = lengthMetres;
        RequestRevision = requestRevision;
        PlanRevision = planRevision;

        if (waypoints == null || waypoints.Count == 0)
        {
            _waypoints = null;
        }
        else
        {
            var copy = new NpcPoint[waypoints.Count];
            for (int index = 0; index < waypoints.Count; index++)
            {
                copy[index] = waypoints[index];
            }

            _waypoints = copy;
        }
    }

    /// <summary>Why this route is what it is.</summary>
    internal RouteVerdict Verdict { get; }

    /// <summary>Start to end, both included. Empty for a refusal.</summary>
    internal IReadOnlyList<NpcPoint> Waypoints => _waypoints ?? Array.Empty<NpcPoint>();

    /// <summary>How far the whole route is, in metres.</summary>
    internal float LengthMetres { get; }

    /// <summary>The revision of the <b>request</b> this answers - what was
    /// asked for, not which answer this is. Two different routes to the same
    /// destination share it.</summary>
    internal int RequestRevision { get; }

    /// <summary>Which answer this is: a number the planner increases for every
    /// plan it produces, in this process.
    ///
    /// <b>Distinct from <see cref="RequestRevision"/>, and the distinction is
    /// the whole point.</b> The ordinary case for re-planning is that the
    /// destination has not changed at all - the NPC learned that a gap it
    /// believed in does not fit, and the route now goes the other way round the
    /// building. Both plans answer the same request. If a goal carried only the
    /// request's revision, a goal from the abandoned route would be
    /// bit-identical in the one field meant to tell them apart, and the follower
    /// would walk it: the exact corner-cutting failure
    /// <see cref="RouteGoal"/> exists to prevent, one level up.</summary>
    internal int PlanRevision { get; }

    /// <summary>The one question before walking. Requires a suitable verdict,
    /// at least two waypoints, a positive length and a destination that is not
    /// where the NPC already stands - a route to where you already are is not a
    /// route, and treating one as followable is how an NPC reports arriving
    /// somewhere it never went.</summary>
    internal bool IsSuitable =>
        Verdict == RouteVerdict.Suitable
        && Waypoints.Count >= 2
        && LengthMetres > 0f
        && !Waypoints[0].Equals(Waypoints[Waypoints.Count - 1]);

    /// <summary>A plan that is not one. Refuses to be built with a verdict that
    /// would claim success.</summary>
    internal static RoutePlan Refused(RouteVerdict verdict, int requestRevision, int planRevision)
    {
        if (verdict == RouteVerdict.Suitable || verdict == RouteVerdict.Unspecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verdict), "A refused route needs a verdict that explains it.");
        }

        return new RoutePlan(verdict, null, 0f, requestRevision, planRevision);
    }
}

/// <summary>The next thing to walk to: one waypoint at a time, because steering
/// is local.
///
/// <b>What it guarantees.</b> That a follower cannot skip ahead. A route that
/// bends back near itself passes close to its own later waypoints, and a
/// follower that simply picked the nearest one would cut the corner and walk
/// through whatever the bend was going round.
///
/// <b>Where the index lives, and why it is here rather than in the planner.</b>
/// The follower holds it and hands it back, because the alternative - the
/// planner remembering where each NPC has got to - forces one planner instance
/// per NPC, and then setback memory is per NPC too, so four NPCs each
/// rediscover the same impassable gap by each getting stuck in it. One planner,
/// shared, remembering what does not work for everybody; the progress along a
/// route belongs to whoever is walking it.</summary>
internal readonly struct RouteGoal
{
    internal RouteGoal(NpcPoint target, float arrivalRadiusMetres, bool isFinal, int waypointIndex, int planRevision)
    {
        Target = target;
        ArrivalRadiusMetres = arrivalRadiusMetres;
        IsFinal = isFinal;
        WaypointIndex = waypointIndex;
        PlanRevision = planRevision;
    }

    internal NpcPoint Target { get; }

    internal float ArrivalRadiusMetres { get; }

    /// <summary>Whether arriving here ends the route.</summary>
    internal bool IsFinal { get; }

    /// <summary>Which waypoint this is. The follower passes it back on the next
    /// ask, and the planner never returns a lower one for the same plan.
    /// </summary>
    internal int WaypointIndex { get; }

    /// <summary>The <see cref="RoutePlan.PlanRevision"/> this goal came from, so
    /// a goal held across a re-plan is discarded rather than walked - including
    /// when the re-plan answered the same request as the plan it
    /// replaced.</summary>
    internal int PlanRevision { get; }
}
