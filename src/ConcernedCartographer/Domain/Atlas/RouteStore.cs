using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>The in-memory route table, mirroring the pin lifecycle
/// contract: monotonic per-entity revisions, tombstone deletes,
/// higher-revision-wins upserts, and a change stream for journaling.</summary>
internal sealed class RouteStore
{
    private readonly Dictionary<Guid, AtlasRoute> _routes = new();
    private readonly Func<DateTime> _clock;

    public RouteStore(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public RouteStore(IEnumerable<AtlasRoute> routes, Func<DateTime>? clock = null)
        : this(clock)
    {
        foreach (AtlasRoute route in routes)
        {
            _routes[route.Id.Value] = route;
        }

        IsDirty = false;
    }

    public event Action<AtlasRoute>? Changed;

    public bool IsDirty { get; private set; }
    public int Count => _routes.Count;

    public IEnumerable<AtlasRoute> All => _routes.Values;

    public IEnumerable<AtlasRoute> Living
    {
        get
        {
            foreach (AtlasRoute route in _routes.Values)
            {
                if (!route.Deleted)
                {
                    yield return route;
                }
            }
        }
    }

    public bool TryGet(AtlasId id, out AtlasRoute route)
    {
        return _routes.TryGetValue(id.Value, out route!);
    }

    public AtlasRoute Create(Action<AtlasRoute>? initialize = null)
    {
        DateTime now = _clock();
        var route = new AtlasRoute(new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 1,
            CreatedUtc = now,
            ModifiedUtc = now,
        };
        initialize?.Invoke(route);
        _routes[route.Id.Value] = route;
        Publish(route);
        return route;
    }

    public bool Mutate(AtlasId id, Action<AtlasRoute> edit)
    {
        if (!_routes.TryGetValue(id.Value, out AtlasRoute? route))
        {
            return false;
        }

        edit(route);
        route.Revision++;
        route.ModifiedUtc = _clock();
        Publish(route);
        return true;
    }

    public bool Delete(AtlasId id)
    {
        return Mutate(id, route =>
        {
            route.Deleted = true;
            route.DeletedUtc = _clock();
        });
    }

    public bool Restore(AtlasId id)
    {
        if (!_routes.TryGetValue(id.Value, out AtlasRoute? route) || !route.Deleted)
        {
            return false;
        }

        return Mutate(id, restored =>
        {
            restored.Deleted = false;
            restored.DeletedUtc = null;
        });
    }

    public bool Upsert(AtlasRoute incoming)
    {
        if (_routes.TryGetValue(incoming.Id.Value, out AtlasRoute? existing))
        {
            if (incoming.Revision <= existing.Revision)
            {
                return false;
            }

            existing.CopyFrom(incoming);
            Publish(existing);
            return true;
        }

        _routes[incoming.Id.Value] = incoming;
        Publish(incoming);
        return true;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    private void Publish(AtlasRoute route)
    {
        IsDirty = true;
        Changed?.Invoke(route);
    }
}
