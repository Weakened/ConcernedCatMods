using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedCartographer.Atlas;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

/// <summary>The loaded-world surface issue #258 was missing: Valheim
/// world <c>Location</c> instances.</summary>
/// <remarks>Dungeon entrances are LOCATIONS, not networked prefabs. When a
/// zone loads, the networked object is a <c>LocationProxy</c> (named
/// "LocationProxy(Clone)"), and <c>ZoneSystem.SpawnLocation</c> in client
/// mode instantiates the real location prefab with every enabled
/// <c>ZNetView</c> child deactivated. So no object called "Crypt3(Clone)",
/// "TrollCave02(Clone)" or "BearCave(Clone)" is ever registered in
/// <c>ZNetScene.m_instances</c>, and the <c>crypt*</c> / <c>trollcave*</c>
/// survey rules could never fire no matter how close the player stood.
///
/// The loaded locations live in the private static
/// <c>Location.s_allLocations</c>, appended in <c>Location.Awake</c> and
/// removed in <c>OnDestroy</c> — already-instantiated objects only, so
/// reading it is bounded by the loaded zones exactly like the ZNetScene
/// surface. It is NEVER the world-wide location database
/// (<c>ZoneSystem.m_locationInstances</c>), which would leak unexplored
/// map data.
///
/// Verified against the installed Valheim 1.0.12 <c>assembly_valheim.dll</c>.
/// If the field ever moves, <see cref="Available"/> goes false and the
/// survey keeps working on its other surfaces.</remarks>
internal sealed class LoadedLocationSightingSource : ISurveySightingSource
{
    /// <summary>Bound on locations examined per sweep. The loaded set is
    /// naturally small (a handful of zones); this only guarantees it.</summary>
    public const int MaxLocationsPerSweep = 96;

    private static readonly AccessTools.FieldRef<List<Location>>? AllLocationsField =
        BuildAllLocationsRef();

    private readonly List<Location> _snapshot = new();

    /// <summary>True when the loaded-location surface resolved against the
    /// running game. The Survey panel reports this honestly.</summary>
    public static bool Available => AllLocationsField is not null;

    public int Count => _snapshot.Count;

    public void Clear()
    {
        _snapshot.Clear();
    }

    /// <summary>Snapshots the currently loaded locations, bounded.</summary>
    public void Refresh()
    {
        _snapshot.Clear();
        if (AllLocationsField is null)
        {
            return;
        }

        List<Location> all = AllLocationsField();
        if (all is null)
        {
            return;
        }

        for (int index = 0; index < all.Count && _snapshot.Count < MaxLocationsPerSweep; index++)
        {
            Location location = all[index];
            if (location != null)
            {
                _snapshot.Add(location);
            }
        }
    }

    public bool TryRead(int index, out SurveySighting sighting)
    {
        sighting = default;
        Location location = _snapshot[index];
        if (location == null || location.gameObject == null)
        {
            return false;
        }

        UnityEngine.Vector3 position = location.transform.position;
        sighting = new SurveySighting(
            location.gameObject.name,
            new RoadPoint(position.x, position.y, position.z),
            location.m_exteriorRadius);
        return true;
    }

    private static AccessTools.FieldRef<List<Location>>? BuildAllLocationsRef()
    {
        try
        {
            System.Reflection.FieldInfo? field =
                AccessTools.Field(typeof(Location), "s_allLocations");
            if (field is null || !field.IsStatic ||
                !typeof(List<Location>).IsAssignableFrom(field.FieldType))
            {
                return null;
            }

            return AccessTools.StaticFieldRefAccess<List<Location>>(field);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
