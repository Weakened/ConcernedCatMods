using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>Reads the live Cartographer route table into immutable snapshots
/// (CT-021). Walks the contract chain by member name from the plugin
/// instance on EVERY call — Cartographer replaces its route store on world
/// enter, so nothing past the plugin instance may ever be cached. Fail
/// closed: any structural surprise returns false with an empty list (a
/// partially read table is never published), while a single malformed route
/// row is skipped so the rest stay usable. Never throws, never writes —
/// every reflective access here is a read.</summary>
public static class CartographerRouteReader
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Copies every living (non-deleted) route out of the store.
    /// False when the chain cannot be walked right now — plugin missing, the
    /// runtime torn down between worlds, a member gone — in which case the
    /// integration simply has nothing to show.</summary>
    public static bool TryReadRoutes(
        object? pluginInstance, out IReadOnlyList<CartographerRouteSnapshot> routes)
    {
        routes = Array.Empty<CartographerRouteSnapshot>();
        try
        {
            if (!TryResolveStore(pluginInstance, out object store))
            {
                return false;
            }

            if (GetPropertyValue(store, CartographerContract.LivingProperty) is not IEnumerable living)
            {
                return false;
            }

            var collected = new List<CartographerRouteSnapshot>();
            foreach (object? routeObject in living)
            {
                if (routeObject is null)
                {
                    continue;
                }

                if (TryReadRoute(routeObject, out CartographerRouteSnapshot? snapshot))
                {
                    collected.Add(snapshot!);
                }
            }

            routes = collected;
            return true;
        }
        catch
        {
            routes = Array.Empty<CartographerRouteSnapshot>();
            return false;
        }
    }

    /// <summary>Reads the store's monotonic change stamp, letting later
    /// leaves (CT-022+) refresh their route lists only when something
    /// actually changed instead of re-copying geometry every tick.</summary>
    public static bool TryReadChangeStamp(object? pluginInstance, out long changeStamp)
    {
        changeStamp = 0;
        try
        {
            if (!TryResolveStore(pluginInstance, out object store))
            {
                return false;
            }

            if (GetPropertyValue(store, CartographerContract.ChangeStampProperty) is not long stamp)
            {
                return false;
            }

            changeStamp = stamp;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Plugin → runtime → store, all by contract member name. A
    /// null anywhere is a normal lifecycle state (runtime not built yet or
    /// already disposed), reported as "not readable now" rather than error.</summary>
    private static bool TryResolveStore(object? pluginInstance, out object store)
    {
        store = null!;
        if (pluginInstance is null)
        {
            return false;
        }

        object? runtime = GetFieldValue(pluginInstance, CartographerContract.RuntimeField);
        if (runtime is null)
        {
            return false;
        }

        object? storeObject = GetFieldValue(runtime, CartographerContract.RouteStoreField);
        if (storeObject is null)
        {
            return false;
        }

        store = storeObject;
        return true;
    }

    private static bool TryReadRoute(object routeObject, out CartographerRouteSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            object? idObject = GetPropertyValue(routeObject, CartographerContract.RouteIdProperty);
            if (idObject is null ||
                GetPropertyValue(idObject, CartographerContract.IdValueProperty) is not Guid id)
            {
                return false;
            }

            string name =
                GetPropertyValue(routeObject, CartographerContract.RouteNameProperty) as string ?? "";

            if (GetPropertyValue(routeObject, CartographerContract.RouteArchivedProperty) is not bool archived)
            {
                return false;
            }

            if (GetPropertyValue(routeObject, CartographerContract.RoutePointsProperty)
                is not IEnumerable pointsEnumerable)
            {
                return false;
            }

            var points = new List<CartographerRoutePoint>();
            foreach (object? pointObject in pointsEnumerable)
            {
                // Torn geometry is worse than a missing route: drop the whole
                // row rather than show a truncated polyline as complete.
                if (pointObject is null ||
                    GetPropertyValue(pointObject, CartographerContract.PointXProperty) is not float x ||
                    GetPropertyValue(pointObject, CartographerContract.PointYProperty) is not float y ||
                    GetPropertyValue(pointObject, CartographerContract.PointZProperty) is not float z)
                {
                    return false;
                }

                points.Add(new CartographerRoutePoint(x, y, z));
            }

            snapshot = new CartographerRouteSnapshot(id, name, archived, points);
            return true;
        }
        catch
        {
            snapshot = null;
            return false;
        }
    }

    private static object? GetFieldValue(object owner, string fieldName)
    {
        return owner.GetType().GetField(fieldName, InstanceMembers)?.GetValue(owner);
    }

    private static object? GetPropertyValue(object owner, string propertyName)
    {
        return owner.GetType().GetProperty(propertyName, InstanceMembers)?.GetValue(owner);
    }
}
