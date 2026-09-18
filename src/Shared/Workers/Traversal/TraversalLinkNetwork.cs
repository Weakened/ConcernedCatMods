using System;
using System.Collections.Generic;

namespace TheConcernedCat.Workers.Traversal;

/// <summary>What a walker should do about a destination.</summary>
internal enum TraversalRouteKind
{
    Unspecified = 0,

    /// <summary>Same level. Walk; no link is involved, and the ground planner
    /// needs to hear nothing about traversal at all.</summary>
    Walk = 1,

    /// <summary>Walk to the link's near end, traverse it, and carry on from the
    /// far end.</summary>
    ThroughLink = 2,

    /// <summary>The destination is on another level and no published link this
    /// actor may use reaches it. <b>Not</b> the same as "unreachable": a
    /// staircase or a slope is a route this layer knows nothing about. It means
    /// only that traversal has nothing to offer here.</summary>
    NoRoute = 3,
}

/// <summary>A destination broken into legs a walker can actually act on.
///
/// The whole contract of a link, in one type: a path that ends at
/// <see cref="EntryPoint"/> continues from <see cref="ExitPoint"/>.</summary>
internal readonly struct TraversalRoute
{
    private TraversalRoute(
        TraversalRouteKind kind,
        TraversalLink link,
        TraversalDirection direction,
        WorkPoint entryPoint,
        WorkPoint exitPoint,
        float estimatedMetres)
    {
        Kind = kind;
        Link = link;
        Direction = direction;
        EntryPoint = entryPoint;
        ExitPoint = exitPoint;
        EstimatedMetres = estimatedMetres;
    }

    public TraversalRouteKind Kind { get; }

    /// <summary>Meaningless unless <see cref="Kind"/> is
    /// <see cref="TraversalRouteKind.ThroughLink"/>.</summary>
    public TraversalLink Link { get; }

    public TraversalDirection Direction { get; }

    /// <summary>Where the first walk ends: the link's near end.</summary>
    public WorkPoint EntryPoint { get; }

    /// <summary>Where the second walk starts: the link's far end.</summary>
    public WorkPoint ExitPoint { get; }

    /// <summary>The whole route's cost in metres of walking, link included.
    /// <b>An estimate from straight lines</b>, because this layer has no
    /// pathfinder and must not pretend to: it is for choosing between links,
    /// never for deciding that somewhere is reachable.</summary>
    public float EstimatedMetres { get; }

    public static TraversalRoute JustWalk(float metres) =>
        new TraversalRoute(TraversalRouteKind.Walk, default, TraversalDirection.Unspecified, default, default, metres);

    public static TraversalRoute NoRoute =>
        new TraversalRoute(
            TraversalRouteKind.NoRoute,
            default,
            TraversalDirection.Unspecified,
            default,
            default,
            float.PositiveInfinity);

    public static TraversalRoute Through(in TraversalLink link, TraversalDirection direction, float metres) =>
        new TraversalRoute(
            TraversalRouteKind.ThroughLink,
            link,
            direction,
            link.EntryFor(direction),
            link.ExitFor(direction),
            metres);
}

/// <summary>Every link that is published right now, and the one question a
/// walker asks of them.
///
/// <b>Publishing and retiring is the whole lifecycle.</b> A link exists while
/// the thing it describes exists. When a ladder is destroyed, unloaded or
/// rebuilt, the adapter retires the link; a handle taken out on the old one
/// stops being valid in the same step, which is how "the ladder is gone" reaches
/// a body that is half way up it rather than being discovered later.
///
/// <b>What this is not.</b> It is not a pathfinder and it is not a graph search.
/// It answers "is there <i>one</i> published link that gets this actor from this
/// level to that one", which is the honest limit of what can be built without
/// redesigning how workers plan movement. Chaining two links — up one ladder,
/// across, down another — is the missing piece, and it is a filed issue rather
/// than a hidden approximation (`LADDERS.md` decision L6).</summary>
internal sealed class TraversalLinkNetwork
{
    private readonly Dictionary<string, Entry> _links = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private int _nextGeneration = 1;

    public int Count => _links.Count;

    /// <summary>Publish a link, or replace the one already under that id.
    /// Returns its generation, which is what makes a rebuilt ladder a different
    /// link even when the adapter mints the same id for it. Zero means the link
    /// was not published because it was not well formed.</summary>
    public int Publish(in TraversalLink link)
    {
        if (link.IsEmpty)
        {
            return 0;
        }

        int generation = _nextGeneration;
        if (_nextGeneration < int.MaxValue)
        {
            _nextGeneration++;
        }

        _links[link.Id] = new Entry(link, generation);
        return generation;
    }

    /// <summary>The ladder went. Retiring something that is not published is not
    /// an error: an adapter that retires on both destruction and unload must not
    /// have to remember which came first.</summary>
    public bool Retire(string? id)
    {
        return !string.IsNullOrEmpty(id) && _links.Remove(id!);
    }

    /// <summary>Everything goes: a world unloaded, the runtime stopped.</summary>
    public void Clear() => _links.Clear();

    public bool TryGet(string? id, out TraversalLink link, out int generation)
    {
        link = default;
        generation = 0;
        if (string.IsNullOrEmpty(id) || !_links.TryGetValue(id!, out Entry entry))
        {
            return false;
        }

        link = entry.Link;
        generation = entry.Generation;
        return true;
    }

    /// <summary>True when this exact link, at this exact generation, is still
    /// published. The question a traversal in flight asks every step.</summary>
    public bool IsCurrent(in TraversalHandle handle) =>
        handle.IsValid &&
        _links.TryGetValue(handle.LinkId!, out Entry entry) &&
        entry.Generation == handle.Generation;

    /// <summary>Every published link, ordered by id so that two runs of the same
    /// world answer the same way.</summary>
    public IReadOnlyList<TraversalLink> All()
    {
        var links = new List<TraversalLink>(_links.Count);
        foreach (KeyValuePair<string, Entry> pair in _links)
        {
            links.Add(pair.Value.Link);
        }

        links.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return links;
    }

    /// <summary>What this actor should do about getting from here to there.
    ///
    /// <b>Height is the whole question.</b> Two points less than a step apart in
    /// height are a walk, and this layer says so and gets out of the way. Two
    /// points further apart than that cannot be walked between, whatever their
    /// horizontal distance says — which is precisely the thing a planner that
    /// measures distance on the ground plane cannot see, and precisely why a
    /// rooftop goal reads as "two metres away" to the settlement planner
    /// today.</summary>
    public TraversalRoute PlanRoute(
        in TraversalActor actor,
        TraversalCapabilities capabilities,
        WorkPoint from,
        WorkPoint to,
        TraversalLimits limits)
    {
        if (capabilities == null)
        {
            throw new ArgumentNullException(nameof(capabilities));
        }

        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (!from.IsFinite || !to.IsFinite)
        {
            return TraversalRoute.NoRoute;
        }

        float rise = to.Y - from.Y;
        if (Math.Abs(rise) <= limits.MinHeightMetres)
        {
            return TraversalRoute.JustWalk(from.HorizontalDistanceTo(to));
        }

        TraversalDirection wanted = rise > 0f ? TraversalDirection.Up : TraversalDirection.Down;

        var best = TraversalRoute.NoRoute;
        float bestMetres = float.PositiveInfinity;
        string? bestId = null;

        // The dictionary is walked directly rather than through All(): planning
        // must not allocate and sort a list every time a worker is given a goal.
        // The ordinal tie-break below is what makes the answer independent of
        // the order the world happened to load its pieces in.
        foreach (KeyValuePair<string, Entry> pair in _links)
        {
            TraversalLink link = pair.Value.Link;
            if (!capabilities.Can(actor, link.Requires))
            {
                continue;
            }

            if (!WithinHeightAllowance(actor, link, limits))
            {
                continue;
            }

            WorkPoint entry = link.EntryFor(wanted);
            WorkPoint exit = link.ExitFor(wanted);

            // The near end has to be on the level the actor is already on, and
            // the far end on the level it wants: otherwise this link is part of
            // a chain, and chains are the filed follow-up rather than a guess.
            if (entry.VerticalDistanceTo(from) > limits.MinHeightMetres ||
                exit.VerticalDistanceTo(to) > limits.MinHeightMetres)
            {
                continue;
            }

            float toEntry = from.HorizontalDistanceTo(entry);
            float fromExit = exit.HorizontalDistanceTo(to);
            if (toEntry > limits.MaxSearchMetres || fromExit > limits.MaxSearchMetres)
            {
                continue;
            }

            float metres = toEntry + link.EquivalentWalkMetres(limits) + fromExit;

            // Ordinal id breaks a tie, so the answer does not depend on the
            // order a world happened to load its pieces in.
            bool better = metres < bestMetres ||
                (metres.Equals(bestMetres) && string.CompareOrdinal(link.Id, bestId) < 0);
            if (!better)
            {
                continue;
            }

            bestMetres = metres;
            bestId = link.Id;
            best = TraversalRoute.Through(link, wanted, metres);
        }

        return best;
    }

    /// <summary>A player climbs whatever they built. A worker does not commit to
    /// a run taller than its allowance, because nobody has yet watched one come
    /// back down.</summary>
    internal static bool WithinHeightAllowance(in TraversalActor actor, in TraversalLink link, TraversalLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        return !actor.IsWorker || link.Height <= limits.MaxWorkerHeightMetres;
    }

    private readonly struct Entry
    {
        public Entry(TraversalLink link, int generation)
        {
            Link = link;
            Generation = generation;
        }

        public TraversalLink Link { get; }

        public int Generation { get; }
    }
}
