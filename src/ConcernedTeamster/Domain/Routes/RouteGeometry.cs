using System.Collections.Generic;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

namespace TheConcernedCat.ConcernedTeamster.Domain.Routes;

/// <summary>Geometry identity for profile caching (CT-023). A profile is
/// valid exactly as long as the route's polyline is unchanged, so the cache
/// key is a content fingerprint over the points — renames never invalidate,
/// any vertex change always does. FNV-1a over the coordinates' hash bits:
/// deterministic within a session, which is all a session-scoped cache
/// needs (route revision is deliberately NOT part of the CT-021 contract).</summary>
public static class RouteGeometry
{
    public static ulong Fingerprint(IReadOnlyList<CartographerRoutePoint> points)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        hash = Mix(hash, unchecked((uint)points.Count), prime);
        for (int index = 0; index < points.Count; index++)
        {
            CartographerRoutePoint point = points[index];
            hash = Mix(hash, unchecked((uint)point.X.GetHashCode()), prime);
            hash = Mix(hash, unchecked((uint)point.Y.GetHashCode()), prime);
            hash = Mix(hash, unchecked((uint)point.Z.GetHashCode()), prime);
        }

        return hash;
    }

    private static ulong Mix(ulong hash, uint value, ulong prime)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= prime;
        }

        return hash;
    }
}
