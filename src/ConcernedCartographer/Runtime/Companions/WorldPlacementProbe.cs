using System;
using System.Reflection;
using BepInEx.Logging;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Answers "could something stand here?" for one candidate point,
/// using only surfaces the 1.0.12 audit marked Verified.
///
/// Everything it asks is bounded and local. <c>IsZoneLoaded</c> comes first and
/// is the hard stop: a point in unloaded ground is reported as
/// <see cref="PlacementRejection.NotLoaded"/> and the planner defers the whole
/// attempt rather than accepting a worse spot, because the ground there may
/// be nothing like what a height query would invent for it. There is no global
/// scan, no terrain edit and no object is moved.
///
/// Water is the one derived test — the audit found no single call for it — so
/// it compares solid ground against the live water level, and when the water
/// level cannot be read it says so once and stops rejecting for water rather
/// than inventing a shoreline.</summary>
internal sealed class WorldPlacementProbe : IPlacementProbe
{
    /// <summary>How far above and below the anchor's height to look for
    /// ground. Generous enough for a sloped camp, small enough that a
    /// candidate never resolves onto a roof or the floor of a cave below.</summary>
    private const float GroundSearchUp = 6f;

    /// <summary>Slope limit, as the dot product of the ground normal with up.
    /// About 40 degrees — a hillside camp still works, a cliff face does
    /// not.</summary>
    private const float MinimumUpDot = 0.76f;

    private readonly ManualLogSource _log;
    private readonly float _clearanceRadius;

    /// <summary>Reused across every candidate. The planner probes several
    /// dozen points per attempt, and a fresh array each time would be pure
    /// garbage for the collector to sweep.</summary>
    private readonly Collider[] _overlapBuffer = new Collider[16];

    private bool _waterLevelUnavailableLogged;

    public WorldPlacementProbe(ManualLogSource log, float clearanceRadius = 0.5f)
    {
        _log = log;
        _clearanceRadius = clearanceRadius;
    }

    public PlacementProbeSample Probe(WorldPoint position)
    {
        var point = new Vector3(position.X, position.Y, position.Z);

        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(point))
            {
                return PlacementProbeSample.NotLoaded(position);
            }

            PlacementRejection rejections = PlacementRejection.None;

            Vector3 probeFrom = point + (Vector3.up * GroundSearchUp);
            if (!zones.GetSolidHeight(probeFrom, out float height, out Vector3 normal, out GameObject _))
            {
                return new PlacementProbeSample(
                    position, PlacementRejection.Unsupported, -1f, SeatAvailability.None);
            }

            Vector3 grounded = new Vector3(point.x, height, point.z);
            var groundedPoint = new WorldPoint(grounded.x, grounded.y, grounded.z);

            if (Vector3.Dot(normal, Vector3.up) < MinimumUpDot)
            {
                rejections |= PlacementRejection.TooSteep;
            }

            if (IsUnderWater(zones, height))
            {
                rejections |= PlacementRejection.Water;
            }

            if (zones.IsBlocked(grounded + (Vector3.up * 0.1f)))
            {
                rejections |= PlacementRejection.Unsupported;
            }

            if (IsOccupied(grounded))
            {
                rejections |= PlacementRejection.Occupied;
            }

            return new PlacementProbeSample(
                groundedPoint, rejections, DistanceToWarmth(grounded), SeatAvailability.Unverified);
        }
        catch (Exception exception)
        {
            _log.LogInfo(
                "A companion placement probe could not read the ground there; that candidate is " +
                $"skipped: {SafeLogText.Brief(exception)}");
            return new PlacementProbeSample(
                position, PlacementRejection.Unsupported, -1f, SeatAvailability.None);
        }
    }

    /// <summary>Warmth as a distance, per the shared sample's contract:
    /// non-negative means a fire is near. <c>EffectArea.IsPointInsideArea</c>
    /// answers the question without touching any fire's internals, but it
    /// answers yes/no, so a hit reports zero and a miss reports -1.</summary>
    private static float DistanceToWarmth(Vector3 grounded)
    {
        try
        {
            EffectArea? heat = EffectArea.IsPointInsideArea(
                grounded, EffectArea.Type.Heat, 0f);
            return heat != null ? 0f : -1f;
        }
        catch
        {
            // Warmth only improves a candidate's score; not knowing costs a
            // nicer spot and nothing else.
            return -1f;
        }
    }

    /// <summary>Anything solid already standing in the spot. A small sphere,
    /// on the layers that actually block a body — nothing here moves, claims
    /// or inspects what it finds.</summary>
    private bool IsOccupied(Vector3 grounded)
    {
        try
        {
            int count = Physics.OverlapSphereNonAlloc(
                grounded + (Vector3.up * 0.9f),
                _clearanceRadius,
                _overlapBuffer,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < count; index++)
            {
                Collider hit = _overlapBuffer[index];
                if (hit == null || hit.isTrigger)
                {
                    continue;
                }

                // The ground itself is not an obstruction.
                if (hit.GetComponent<Heightmap>() != null)
                {
                    continue;
                }

                if (hit.GetComponentInParent<Character>() != null ||
                    hit.GetComponentInParent<Piece>() != null)
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsUnderWater(ZoneSystem zones, float groundHeight)
    {
        try
        {
            FieldInfo? field = zones.GetType().GetField(
                "m_waterLevel",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(zones) is float level)
            {
                return groundHeight < level;
            }
        }
        catch
        {
            // Fall through to the notice below.
        }

        if (!_waterLevelUnavailableLogged)
        {
            _waterLevelUnavailableLogged = true;
            _log.LogInfo(
                "This build does not expose a water level, so companion placement cannot rule out a " +
                "shoreline spot. Placement still avoids steep, blocked and occupied ground.");
        }

        return false;
    }
}
