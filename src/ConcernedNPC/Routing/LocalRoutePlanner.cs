using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Routing;

/// <summary>The shipped <see cref="INpcRoutePlanner"/>: one body, one leg at a
/// time, on ground a probe confirms.
///
/// <b>What it is.</b> A local errand planner. It takes the straight line to
/// where the NPC is going, samples it at a fixed spacing, asks the probe whether
/// each sample is ground an NPC may stand on, and steps a bounded, deterministic
/// set of lateral offsets around a sample that is refused. That is the placement
/// idiom <see cref="INpcAreaProbe"/> describes - propose in order, let the world
/// say no, fall through - applied along a line instead of around a point.
///
/// <b>What it is not, and must never become.</b> It is not a navigation-mesh
/// search and it is not a shortest path. It has no open set, no closed set and
/// no frontier, so there is no input on which it can spend an unbounded amount
/// of time. It will not find the way round a building; that is what
/// <see cref="RememberSetback"/> and the setback memory are for, and they work
/// by the NPC having tried, which is the only evidence that the ground lies.
///
/// <b>Cost, stated.</b> One call probes at most
/// <c>MostSamplesPerPlan * (1 + DetourOffsets.Length)</c> points and never more:
/// the sample spacing widens so that a long walk is the same number of samples
/// as a short one. At the shipped numbers that is a hard ceiling of 48 * 5 =
/// 240 probes for a pathological route and 48 for an open field, with no
/// allocation per probe. <see cref="RouteVerdict.BudgetExhausted"/> is answered
/// when a caller's own allowance runs out first, and it means ask again - never
/// that there is no way through.
///
/// <b>Why it is shared, and what it remembers.</b> One planner serves every NPC
/// in the process. Setback memory is the reason: what one body learns about a
/// gap it does not fit through is true for the others, and four NPCs each
/// discovering it separately is four NPCs visibly stuck. What it does <i>not</i>
/// remember is where anybody has got to along a route - that is the follower's,
/// handed back through <see cref="NextGoal"/>, which is what lets the planner
/// stay shared.</summary>
internal sealed class LocalRoutePlanner : INpcRoutePlanner
{
    /// <summary>Further than this and the walk is somebody else's problem -
    /// answered on distance alone, before a single probe is spent, exactly as
    /// <see cref="RouteVerdict.TooFar"/> says it must be.</summary>
    internal const float LongestRouteMetres = 128f;

    /// <summary>How far apart the line is sampled, when the walk is short enough
    /// for this spacing to fit inside <see cref="MostSamplesPerPlan"/>.</summary>
    internal const float SampleSpacingMetres = 4f;

    /// <summary>The hard ceiling on samples per plan. A longer walk is sampled
    /// more coarsely rather than more often, so the cost of planning does not
    /// grow with the distance.</summary>
    internal const int MostSamplesPerPlan = 48;

    /// <summary>Shorter than this and there is nothing to walk.</summary>
    internal const float ShortestWalkMetres = 0.25f;

    /// <summary>How close counts as having reached a waypoint on the way. Not
    /// the arrival tolerance of the destination, which the caller states per
    /// request.</summary>
    internal const float WaypointRadiusMetres = 1.5f;

    /// <summary>How many plans' arrival tolerances are remembered, so the final
    /// goal can be given the radius its request asked for. See
    /// <see cref="NextGoal"/> for why this exists rather than the plan carrying
    /// it.</summary>
    internal const int RememberedTolerances = 16;

    /// <summary>The lateral offsets tried, in order, around a sample the probe
    /// refuses: a step to the left, a step to the right, then twice as far each
    /// way. Fixed and ordered, so the detour is the same every time the same
    /// ground refuses.</summary>
    private static readonly float[] DetourOffsets = { 2f, -2f, 4f, -4f };

    private readonly NpcWalkSetbacks _setbacks = new NpcWalkSetbacks();
    private readonly List<KeyValuePair<int, float>> _tolerances = new List<KeyValuePair<int, float>>();
    private readonly INpcAreaProbe _probe;
    private int _planRevision;

    internal LocalRoutePlanner(INpcAreaProbe probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    /// <summary>What has failed lately. Exposed so an interruption leaf can
    /// forget everything when a world unloads: a setback names a place in a
    /// world that no longer exists.</summary>
    internal NpcWalkSetbacks Setbacks => _setbacks;

    /// <summary>How many plans this planner has produced, refusals included.
    /// Every one of them carries a different
    /// <see cref="RoutePlan.PlanRevision"/>.</summary>
    internal int PlansProduced => _planRevision;

    /// <inheritdoc />
    public RoutePlan Plan(in RouteRequest request, float now)
    {
        int revision = ++_planRevision;

        if (!request.From.IsFinite || !request.To.IsFinite || request.ArrivalToleranceMetres < 0f ||
            float.IsNaN(request.ArrivalToleranceMetres))
        {
            return RoutePlan.Refused(RouteVerdict.InvalidRequest, request.Revision, revision);
        }

        float straight = request.From.HorizontalDistanceTo(request.To);
        if (straight > LongestRouteMetres)
        {
            // Measuring distance is free and asking the ground is not.
            return RoutePlan.Refused(RouteVerdict.TooFar, request.Revision, revision);
        }

        float walk = straight - request.ArrivalToleranceMetres;
        if (walk < ShortestWalkMetres)
        {
            // A route to where you already stand is not a route, and treating
            // one as followable is how an NPC reports arriving somewhere it
            // never went.
            return RoutePlan.Refused(RouteVerdict.InvalidRequest, request.Revision, revision);
        }

        if (_setbacks.RefusesSpot(request.To, now))
        {
            return RoutePlan.Refused(RouteVerdict.RefusedRecently, request.Revision, revision);
        }

        var waypoints = new List<NpcPoint> { request.From };
        RouteVerdict verdict = Trace(request, walk, waypoints);
        if (verdict != RouteVerdict.Suitable)
        {
            return RoutePlan.Refused(verdict, request.Revision, revision);
        }

        // The destination itself is never probed. An NPC walks up to a chest or
        // a tree, not onto it, and the arrival tolerance is exactly how far short
        // it stops - so requiring the destination to be standable would refuse
        // every errand that has a point.
        waypoints.Add(request.To);
        Collapse(waypoints);

        if (waypoints.Count < 2)
        {
            return RoutePlan.Refused(RouteVerdict.NoPath, request.Revision, revision);
        }

        if (_setbacks.RefusesRoute(waypoints, now))
        {
            return RoutePlan.Refused(RouteVerdict.RefusedRecently, request.Revision, revision);
        }

        RememberTolerance(revision, request.ArrivalToleranceMetres);
        return new RoutePlan(RouteVerdict.Suitable, waypoints, Length(waypoints), request.Revision, revision);
    }

    /// <inheritdoc />
    public RouteGoal? NextGoal(in RoutePlan plan, NpcPoint at, int lastWaypointIndex)
    {
        if (!plan.IsSuitable || !at.IsFinite)
        {
            return null;
        }

        IReadOnlyList<NpcPoint> waypoints = plan.Waypoints;
        int last = waypoints.Count - 1;

        // The index only moves forward, and this is the whole of how that is
        // true: the next goal is computed from the index the follower was last
        // given, never from which waypoint happens to be nearest. A pure
        // function of plan and position could only pick the nearest, which is
        // the corner-cutting a goal exists to prevent - so the follower holds
        // the index and hands it back, and the planner stays shared.
        int next = lastWaypointIndex < 0 ? 1 : lastWaypointIndex + 1;
        if (next < 1)
        {
            next = 1;
        }

        if (next > last)
        {
            return null;
        }

        // Waypoints on the way that the NPC is already standing on are stepped
        // over - forwards only, and never the last one, which is the
        // destination and is arrived at rather than passed.
        while (next < last && at.HorizontalDistanceTo(waypoints[next]) <= WaypointRadiusMetres)
        {
            next++;
        }

        bool isFinal = next == last;
        float radius = isFinal ? ArrivalRadiusFor(plan.PlanRevision) : WaypointRadiusMetres;
        return new RouteGoal(waypoints[next], radius, isFinal, next, plan.PlanRevision);
    }

    /// <inheritdoc />
    public void RememberSetback(NpcPoint destination, NpcPoint stoppedAt, NpcPoint headingTowards, float now)
    {
        if (!destination.IsFinite)
        {
            return;
        }

        if (!stoppedAt.IsFinite || !headingTowards.IsFinite)
        {
            _setbacks.Remember(destination, now);
            return;
        }

        _setbacks.Remember(destination, stoppedAt, headingTowards, now);
    }

    /// <summary>Walks the straight line from the start towards the destination,
    /// stopping <c>ArrivalToleranceMetres</c> short, and collects the ground the
    /// probe confirms. A refused sample is stepped around, not given up
    /// on.</summary>
    private RouteVerdict Trace(in RouteRequest request, float walk, List<NpcPoint> waypoints)
    {
        int legs = (int)Math.Ceiling(walk / SampleSpacingMetres);
        if (legs < 1)
        {
            legs = 1;
        }

        if (legs > MostSamplesPerPlan)
        {
            // Sample a long walk more coarsely rather than more often: the cost
            // of planning must not grow with the distance.
            legs = MostSamplesPerPlan;
        }

        float alongX = request.To.X - request.From.X;
        float alongZ = request.To.Z - request.From.Z;
        float flat = (float)Math.Sqrt((alongX * alongX) + (alongZ * alongZ));
        if (flat <= 0f)
        {
            return RouteVerdict.InvalidRequest;
        }

        alongX /= flat;
        alongZ /= flat;
        float sidewaysX = -alongZ;
        float sidewaysZ = alongX;

        for (int leg = 1; leg <= legs; leg++)
        {
            float along = walk * leg / legs;
            var on = new NpcPoint(
                request.From.X + (alongX * along),
                Height(request.From, request.To, along, walk),
                request.From.Z + (alongZ * along));

            AreaSample sample = _probe.Probe(on);
            if (sample.Verdict == AreaSampleVerdict.Standable)
            {
                waypoints.Add(sample.Ground);
                continue;
            }

            if (sample.Verdict == AreaSampleVerdict.NotLoaded)
            {
                // Ask again when the player is nearer. Never a permanent no.
                return RouteVerdict.OutsideLoadedArea;
            }

            if (sample.Verdict == AreaSampleVerdict.Unreadable)
            {
                // "I could not tell" is not "there is no way". A spent budget
                // is the one verdict that means ask again without claiming
                // anything about the ground.
                return RouteVerdict.BudgetExhausted;
            }

            if (!TryDetour(on, sidewaysX, sidewaysZ, out NpcPoint round))
            {
                return (sample.Rejection & AreaRejection.Hazard) == AreaRejection.Hazard
                    ? RouteVerdict.Hazardous
                    : RouteVerdict.NoPath;
            }

            waypoints.Add(round);
        }

        return RouteVerdict.Suitable;
    }

    /// <summary>The bounded, ordered set of ways round one refused sample: a
    /// step to each side, then twice as far to each side. Four probes at most
    /// and always the same four, so a detour is reproducible rather than
    /// lucky.</summary>
    private bool TryDetour(NpcPoint refused, float sidewaysX, float sidewaysZ, out NpcPoint round)
    {
        foreach (float offset in DetourOffsets)
        {
            var candidate = new NpcPoint(
                refused.X + (sidewaysX * offset), refused.Y, refused.Z + (sidewaysZ * offset));
            AreaSample sample = _probe.Probe(candidate);
            if (sample.Verdict == AreaSampleVerdict.Standable)
            {
                round = sample.Ground;
                return true;
            }
        }

        round = default;
        return false;
    }

    /// <summary>The arrival radius the request asked for, looked up by the plan
    /// that answered it.
    ///
    /// <b>This exists because the seam has a gap, and papering over it silently
    /// would be worse than saying so.</b> <see cref="RoutePlan"/> does not carry
    /// the request's <c>ArrivalToleranceMetres</c>, so a planner asked for the
    /// next goal of a plan has no way to know how close the destination counted
    /// as reached - and the final goal's radius is exactly that number. A small
    /// ring of the last few plans' tolerances closes it honestly: the plan
    /// revision is unique per plan, so the lookup is exact when it hits, and a
    /// miss falls back to the waypoint radius rather than inventing a number.
    /// The proper fix is a field on the plan, which is a change to a frozen
    /// contract and therefore not this leaf's to make.</summary>
    private float ArrivalRadiusFor(int planRevision)
    {
        for (int index = _tolerances.Count - 1; index >= 0; index--)
        {
            if (_tolerances[index].Key == planRevision)
            {
                return _tolerances[index].Value;
            }
        }

        return WaypointRadiusMetres;
    }

    private void RememberTolerance(int planRevision, float tolerance)
    {
        _tolerances.Add(new KeyValuePair<int, float>(planRevision, tolerance));
        while (_tolerances.Count > RememberedTolerances)
        {
            _tolerances.RemoveAt(0);
        }
    }

    /// <summary>Height along the line, interpolated. The probe answers with the
    /// ground it actually found, so this is only where to ask.</summary>
    private static float Height(NpcPoint from, NpcPoint to, float along, float walk) =>
        walk <= 0f ? from.Y : from.Y + ((to.Y - from.Y) * (along / walk));

    /// <summary>Drops a waypoint that is on top of the one before it. Two
    /// identical waypoints are a leg of zero length, and a route whose first and
    /// last are the same is not suitable at all.</summary>
    private static void Collapse(List<NpcPoint> waypoints)
    {
        for (int index = waypoints.Count - 1; index > 0; index--)
        {
            if (waypoints[index].HorizontalDistanceTo(waypoints[index - 1]) < ShortestWalkMetres &&
                waypoints[index].VerticalDistanceTo(waypoints[index - 1]) < ShortestWalkMetres)
            {
                waypoints.RemoveAt(index);
            }
        }
    }

    private static float Length(List<NpcPoint> waypoints)
    {
        float total = 0f;
        for (int index = 0; index + 1 < waypoints.Count; index++)
        {
            total += waypoints[index].HorizontalDistanceTo(waypoints[index + 1]);
        }

        return total;
    }
}
