namespace TheConcernedCat.ConcernedCartographer.Atlas;

/// <summary>Pure, explicit separation between land and sailing routes.
/// Geometry and runtime player/helm gates are checked by the caller.</summary>
internal static class RouteFollowEligibility
{
    public static bool CanWalk(AtlasRoute? route)
    {
        return IsLiving(route) &&
            route!.TravelMode == RouteTravelMode.Land;
    }

    public static bool CanSail(AtlasRoute? route)
    {
        return IsLiving(route) &&
            route!.TravelMode == RouteTravelMode.Sailing;
    }

    private static bool IsLiving(AtlasRoute? route)
    {
        return route is not null &&
            !route.Deleted &&
            !route.Archived;
    }
}
