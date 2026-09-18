using System;
using TheConcernedCat.Settlement.Collection.Planning;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Collection;

/// <summary>Whether a point belongs to one of the world's locations (D10: they
/// are excluded, the start temple included).
///
/// <b>Why not `Location.IsInsideLocation` alone.</b> That reads
/// <c>Location.s_allLocations</c>, which a location joins in its
/// <c>Location.Awake</c> — and the component only exists once
/// <c>LocationProxy</c> has spawned it, which the game may defer a frame or
/// more while a zone's objects are created nearest-first. In that window a
/// temple stone would answer "not in a location" and become collectable, which
/// is precisely what D10 forbids. The loaded-component answer is therefore kept
/// only as a fast "yes".
///
/// <b>What is read instead.</b> <c>ZoneSystem.m_locationInstances</c> is the
/// world's own registry of placed locations, keyed by zone and filled at world
/// generation, so it answers whether a location stands here whether or not its
/// objects exist yet. The source's zone and its eight neighbours are consulted,
/// because a location's exterior reaches past its own zone.
///
/// <b>Unknown refuses.</b> No zone system, a registration whose location data
/// cannot be read, or any exception answers
/// <see cref="LocationStanding.Unknown"/>, which the predicate treats as a
/// refusal — the same shape the ward clause has.</summary>
internal static class WorldLocationSense
{
    internal static LocationStanding At(Vector3 position)
    {
        try
        {
            // The loaded component, when it is there, is the cheapest yes.
            if (Location.IsInsideLocation(position, 0f))
            {
                return LocationStanding.Inside;
            }

            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || zones.m_locationInstances == null)
            {
                return LocationStanding.Unknown;
            }

            Vector2s centre = ZoneSystem.GetZone(position);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var zone = new Vector2s(centre.x + dx, centre.y + dz);
                    if (!zones.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance instance))
                    {
                        continue;
                    }

                    if (instance.m_location == null)
                    {
                        // A location stands in reach and we cannot tell how far
                        // it reaches.
                        return LocationStanding.Unknown;
                    }

                    if (Utils.DistanceXZ(instance.m_position, position) <= Math.Max(0f, instance.m_location.m_exteriorRadius))
                    {
                        return LocationStanding.Inside;
                    }
                }
            }

            return LocationStanding.Outside;
        }
        catch (Exception)
        {
            return LocationStanding.Unknown;
        }
    }
}
