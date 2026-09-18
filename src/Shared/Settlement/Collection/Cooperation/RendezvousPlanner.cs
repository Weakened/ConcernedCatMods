using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Collection.Cooperation;

/// <summary>Where Gunnar and Thorstein meet, and where Thorstein stands.
///
/// The consumer only <i>proposes</i> meeting points; whether a loaded cart can
/// actually get there is Gunnar's navigation's call (CART-05), which refuses an
/// unsuitable route with its reason and moves nothing. So this is a short,
/// deterministic list tried in order - a route refusal moves to the next point
/// - and not a claim about the ground. The heights are the work area's (or the
/// cart's own); the provider's planner measures the real ground.</summary>
internal static class RendezvousPlanner
{
    /// <summary>Two candidates closer than this are the same place.</summary>
    public const float SamePlaceMetres = 2f;

    /// <summary>Meeting points in the order they are offered:
    /// <list type="number">
    /// <item>halfway from the work area's centre toward the chest - short
    /// carries for Thorstein, a shorter haul for Gunnar;</item>
    /// <item>the work area's edge toward the chest - usually more open ground
    /// than the middle of a grove;</item>
    /// <item>the work area's centre;</item>
    /// <item>where the cart already stands - no travel at all, for a patch the
    /// cart cannot enter while Thorstein can carry back to it.</item>
    /// </list></summary>
    public static IReadOnlyList<SitePoint> Candidates(WorkScope scope, SitePoint destination, SitePoint? cart, int maxCandidates)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (maxCandidates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), "At least one candidate is needed.");
        }

        SitePoint centre = scope.Centre;
        float dx = destination.X - centre.X;
        float dz = destination.Z - centre.Z;
        float distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
        if (distance < 0.5f && cart.HasValue)
        {
            dx = cart.Value.X - centre.X;
            dz = cart.Value.Z - centre.Z;
            distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
        }

        var candidates = new List<SitePoint>(4);
        if (distance >= 0.5f)
        {
            float ux = dx / distance;
            float uz = dz / distance;
            float half = Math.Min(scope.RadiusMetres * 0.5f, distance);
            float edge = Math.Min(scope.RadiusMetres, distance);
            AddDistinct(candidates, new SitePoint(centre.X + (ux * half), centre.Y, centre.Z + (uz * half)));
            AddDistinct(candidates, new SitePoint(centre.X + (ux * edge), centre.Y, centre.Z + (uz * edge)));
        }

        AddDistinct(candidates, centre);
        if (cart.HasValue)
        {
            AddDistinct(candidates, cart.Value);
        }

        if (candidates.Count > maxCandidates)
        {
            candidates.RemoveRange(maxCandidates, candidates.Count - maxCandidates);
        }

        return candidates;
    }

    /// <summary>A point <paramref name="standOffMetres"/> from
    /// <paramref name="target"/> on the side facing <paramref name="from"/>:
    /// where a worker coming from there stops to reach the target without
    /// walking into it. The target itself when he is already that close.
    /// </summary>
    public static SitePoint StandOff(SitePoint target, SitePoint from, float standOffMetres)
    {
        float dx = from.X - target.X;
        float dz = from.Z - target.Z;
        float distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
        if (distance <= standOffMetres || distance < 0.01f)
        {
            return from;
        }

        float scale = standOffMetres / distance;
        return new SitePoint(target.X + (dx * scale), target.Y, target.Z + (dz * scale));
    }

    /// <summary>Where to stand to reach both a cart and a chest: halfway
    /// between them.</summary>
    public static SitePoint Between(SitePoint first, SitePoint second) =>
        new SitePoint((first.X + second.X) * 0.5f, (first.Y + second.Y) * 0.5f, (first.Z + second.Z) * 0.5f);

    private static void AddDistinct(List<SitePoint> candidates, SitePoint point)
    {
        foreach (SitePoint existing in candidates)
        {
            if (existing.HorizontalDistanceTo(point) < SamePlaceMetres)
            {
                return;
            }
        }

        candidates.Add(point);
    }
}
