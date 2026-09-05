using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Routes;

/// <summary>Session-scoped profile cache (CT-023), keyed by route identity
/// AND geometry fingerprint: a geometry edit changes the fingerprint, so the
/// stale profile can never be served — the miss is the invalidation. Renames
/// keep the fingerprint and stay cached. Bounded: at the cap the cache
/// resets rather than grow (profiles are cheap to recompute; unbounded
/// memory is not acceptable). Cleared on world exit by the owner — route
/// ids are world-scoped.</summary>
public sealed class RouteProfileCache
{
    public const int MaxEntries = 64;

    private readonly Dictionary<Guid, KeyValuePair<ulong, RouteProfile>> _entries = new();

    public int Count => _entries.Count;

    public bool TryGet(Guid routeId, ulong fingerprint, out RouteProfile? profile)
    {
        if (_entries.TryGetValue(routeId, out KeyValuePair<ulong, RouteProfile> entry) &&
            entry.Key == fingerprint)
        {
            profile = entry.Value;
            return true;
        }

        profile = null;
        return false;
    }

    public void Store(Guid routeId, ulong fingerprint, RouteProfile profile)
    {
        if (!_entries.ContainsKey(routeId) && _entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }

        _entries[routeId] = new KeyValuePair<ulong, RouteProfile>(fingerprint, profile);
    }

    public void Clear()
    {
        _entries.Clear();
    }
}
