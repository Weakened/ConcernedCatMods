using System;
using System.Collections.Generic;
using System.Linq;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;
using Xunit;

namespace TheConcernedCat.ConcernedCartographer.Tests;

public class RouteTravelModeTests
{
    private static RoadPoint P(float x, float z) => new(x, 0f, z);

    [Fact]
    public void NewSailingExtensionRoundtripsWithoutChangingV2Meta()
    {
        AtlasRoute route = Route(RouteTravelMode.Sailing);
        List<string> rows = RouteCodec.SerializeRoute(route).ToList();

        Assert.EndsWith("	2", rows[0]);
        Assert.Contains(rows, row => row.EndsWith("	T	3"));
        RouteCodec.ParseResult parsed = RouteCodec.Parse(rows);

        AtlasRoute restored = Assert.Single(parsed.Routes);
        Assert.Equal(RouteTravelMode.Sailing, restored.TravelMode);
        Assert.Equal(route.Points, restored.Points);
        Assert.Equal(0, parsed.MalformedRows);
    }

    [Fact]
    public void V1AndV2RowsWithoutExtensionDefaultToLand()
    {
        AtlasRoute route = Route(RouteTravelMode.Land);
        List<string> v2 = RouteCodec.SerializeRoute(route).ToList();
        Assert.DoesNotContain(v2, row => row.EndsWith("	T	3"));

        AtlasRoute parsedV2 = Assert.Single(RouteCodec.Parse(v2).Routes);
        Assert.Equal(RouteTravelMode.Land, parsedV2.TravelMode);

        string[] v1Meta = v2[0].Split('	');
        List<string> v1 = new()
        {
            string.Join("	", v1Meta.Take(16).Concat(new[] { "1" })),
        };
        v1.AddRange(v2.Skip(1));
        AtlasRoute parsedV1 = Assert.Single(RouteCodec.Parse(v1).Routes);
        Assert.Equal(RouteTravelMode.Land, parsedV1.TravelMode);
    }

    [Fact]
    public void OlderReaderIgnoringExtensionStillRetainsRouteAndPoints()
    {
        AtlasRoute route = Route(RouteTravelMode.Sailing);
        List<string> rows = RouteCodec.SerializeRoute(route).ToList();
        List<string> oldVisibleRows = rows
            .Where(row => !row.EndsWith("	T	3"))
            .ToList();

        RouteCodec.ParseResult downgraded =
            RouteCodec.Parse(oldVisibleRows);
        AtlasRoute retained = Assert.Single(downgraded.Routes);

        Assert.Equal(RouteTravelMode.Land, retained.TravelMode);
        Assert.Equal(route.Id, retained.Id);
        Assert.Equal(route.Points, retained.Points);
    }

    [Fact]
    public void MalformedTravelExtensionCannotDiscardV2Route()
    {
        AtlasRoute route = Route(RouteTravelMode.Sailing);
        List<string> rows = RouteCodec.SerializeRoute(route).ToList();
        int extension = rows.FindIndex(row => row.EndsWith("	T	3"));
        string[] parts = rows[extension].Split('	');
        parts[2] = "99";
        rows[extension] = string.Join("	", parts);

        RouteCodec.ParseResult result = RouteCodec.Parse(rows);
        AtlasRoute retained = Assert.Single(result.Routes);

        Assert.Equal(RouteTravelMode.Land, retained.TravelMode);
        Assert.Equal(route.Points, retained.Points);
        Assert.Equal(1, result.MalformedRows);
    }

    [Fact]
    public void LandAndSailingRoutesCannotMerge()
    {
        var store = new RouteStore();
        var operations = new RouteOperations(store);
        AtlasRoute land = operations.StartRoute(RouteKind.Waypoint, "Land");
        AtlasRoute sailing = operations.StartRoute(RouteKind.Waypoint, "Sea");
        store.Mutate(land.Id, route =>
        {
            route.Points.Add(P(0f, 0f));
            route.Points.Add(P(10f, 0f));
        });
        store.Mutate(sailing.Id, route =>
        {
            route.TravelMode = RouteTravelMode.Sailing;
            route.Points.Add(P(10f, 0f));
            route.Points.Add(P(20f, 0f));
        });

        Assert.False(operations.Merge(land.Id, sailing.Id));
        Assert.False(land.Deleted);
        Assert.False(sailing.Deleted);
        Assert.Equal(2, land.Points.Count);
        Assert.Equal(2, sailing.Points.Count);
    }

    [Fact]
    public void MatchingSailingRoutesMergeAndPreserveMode()
    {
        var store = new RouteStore();
        var operations = new RouteOperations(store);
        AtlasRoute first = operations.StartRoute(RouteKind.Waypoint, "One");
        AtlasRoute second = operations.StartRoute(RouteKind.Waypoint, "Two");
        store.Mutate(first.Id, route =>
        {
            route.TravelMode = RouteTravelMode.Sailing;
            route.Points.Add(P(0f, 0f));
            route.Points.Add(P(10f, 0f));
        });
        store.Mutate(second.Id, route =>
        {
            route.TravelMode = RouteTravelMode.Sailing;
            route.Points.Add(P(10f, 0f));
            route.Points.Add(P(20f, 0f));
        });

        Assert.True(operations.Merge(first.Id, second.Id));
        Assert.Equal(RouteTravelMode.Sailing, first.TravelMode);
        Assert.True(second.Deleted);
    }

    [Fact]
    public void WalkingAndSailingEligibilityAreMutuallyExclusive()
    {
        AtlasRoute route = Route(RouteTravelMode.Land);
        Assert.True(RouteFollowEligibility.CanWalk(route));
        Assert.False(RouteFollowEligibility.CanSail(route));

        route.TravelMode = RouteTravelMode.Sailing;
        Assert.False(RouteFollowEligibility.CanWalk(route));
        Assert.True(RouteFollowEligibility.CanSail(route));

        route.Archived = true;
        Assert.False(RouteFollowEligibility.CanWalk(route));
        Assert.False(RouteFollowEligibility.CanSail(route));
    }

    private static AtlasRoute Route(RouteTravelMode mode)
    {
        var route = new AtlasRoute(
            new AtlasId(AtlasId.RouteKind, Guid.NewGuid()))
        {
            Revision = 4,
            CreatedUtc = new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 9, 11, 1, 0, 0, DateTimeKind.Utc),
            Name = "Island passage",
            Kind = RouteKind.Waypoint,
            TravelMode = mode,
            OwnerAuthor = "owner",
            LastAuthor = "owner",
        };
        route.Points.Add(P(0f, 0f));
        route.Points.Add(P(100f, 0f));
        return route;
    }
}
