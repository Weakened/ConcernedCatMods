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

    /// <summary>How far around a candidate to look for beds, doors,
    /// seats and bodies. Wider than the clearance radius, because a
    /// doorway two metres away still makes a spot a bad place to sit.</summary>
    private const float NeighbourhoodRadius = 2f;

    /// <summary>Radius for the authored-location sweep. Locations are
    /// large, so this only has to find a piece of one; its own radius
    /// fields decide the rest.</summary>
    private const float LocationProbeRadius = 3f;

    private readonly ManualLogSource _log;
    private readonly float _clearanceRadius;
    private readonly float _warmthRadius;

    /// <summary>Reused across every candidate. The planner probes several
    /// dozen points per attempt, and a fresh array each time would be pure
    /// garbage for the collector to sweep.</summary>
    private readonly Collider[] _overlapBuffer = new Collider[32];

    private bool _waterLevelUnavailableLogged;

    /// <param name="warmthRadius">How far from a fire still counts as warm.
    /// This has to be passed IN rather than compared afterwards: the vanilla
    /// warmth call answers inside-or-outside for a radius you hand it, so a
    /// probe that asked with a fixed radius and let the planner compare
    /// afterwards would leave <c>PlacementRules.FireComfortRadius</c> looking
    /// like a tuning knob while changing nothing. Give it the rules' own value
    /// and the knob is real.</param>
    public WorldPlacementProbe(
        ManualLogSource log,
        float warmthRadius = PlacementRules.DefaultFireComfortRadius,
        float clearanceRadius = 0.5f)
    {
        _log = log;
        _warmthRadius = warmthRadius > 0f ? warmthRadius : PlacementRules.DefaultFireComfortRadius;
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

            rejections |= SurveyNeighbourhood(grounded, out SeatAvailability seat);

            if (IsHazardousFire(grounded))
            {
                rejections |= PlacementRejection.Fire;
            }

            if (IsInsideClearedLocation(grounded))
            {
                rejections |= PlacementRejection.Shrine;
            }

            return new PlacementProbeSample(
                groundedPoint, rejections, DistanceToWarmth(grounded), seat);
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
    /// non-negative means a fire is near, and the planner then compares it
    /// against <c>FireComfortRadius</c>.
    ///
    /// <c>EffectArea.IsPointInsideArea</c> answers inside-or-outside for a
    /// radius passed in, not a distance, so the comfort radius is what this
    /// asks WITH. A hit therefore reports 0 (inside the radius the planner
    /// will compare against, which is exactly what "warm" means) and a miss
    /// reports -1. Widening the rule widens the question, which is the
    /// behaviour a tuning knob is supposed to have.</summary>
    private float DistanceToWarmth(Vector3 grounded)
    {
        try
        {
            EffectArea? heat = EffectArea.IsPointInsideArea(
                grounded, EffectArea.Type.Heat, _warmthRadius);
            return heat != null ? 0f : -1f;
        }
        catch
        {
            // Warmth only improves a candidate's score; not knowing costs a
            // nicer spot and nothing else.
            return -1f;
        }
    }

    /// <summary>One sweep of what is standing in and around the spot: whether
    /// it is occupied, whether it is somebody's bed or a doorway, and whether
    /// there is a seat.
    ///
    /// Done as a single overlap because the planner asks this for several dozen
    /// candidates and four separate sweeps would cost four times as much for
    /// the same answers. Nothing found here is moved, claimed, opened or
    /// written to — a companion yields to the world, never the other way
    /// round.</summary>
    private PlacementRejection SurveyNeighbourhood(Vector3 grounded, out SeatAvailability seat)
    {
        seat = SeatAvailability.None;

        try
        {
            int count = Physics.OverlapSphereNonAlloc(
                grounded + (Vector3.up * 0.9f),
                NeighbourhoodRadius,
                _overlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            PlacementRejection rejections = PlacementRejection.None;
            bool freeSeatNearby = false;

            for (int index = 0; index < count; index++)
            {
                Collider hit = _overlapBuffer[index];
                if (hit == null)
                {
                    continue;
                }

                // The ground itself is not an obstruction.
                if (hit.GetComponent<Heightmap>() != null)
                {
                    continue;
                }

                // A bed is somebody's. Standing on one is rude and, for a
                // respawn point, actively unhelpful.
                if (hit.GetComponentInParent<Bed>() != null)
                {
                    rejections |= PlacementRejection.Bed;
                    continue;
                }

                // A doorway is a route, not a room.
                if (hit.GetComponentInParent<Door>() != null)
                {
                    rejections |= PlacementRejection.Doorway;
                    continue;
                }

                var chair = hit.GetComponentInParent<Chair>();
                if (chair != null)
                {
                    // IsInUse is the vanilla way to yield to a real occupant.
                    // No seat is ever claimed and no attachment message is ever
                    // sent.
                    if (chair.IsInUse())
                    {
                        seat = SeatAvailability.Occupied;
                    }
                    else
                    {
                        freeSeatNearby = true;
                    }

                    continue;
                }

                bool solid = !hit.isTrigger &&
                    (hit.GetComponentInParent<Character>() != null ||
                     hit.GetComponentInParent<Piece>() != null);
                if (solid && IsWithin(hit, grounded, _clearanceRadius))
                {
                    rejections |= PlacementRejection.Occupied;
                }
            }

            if (freeSeatNearby && seat != SeatAvailability.Occupied)
            {
                // Deliberately NOT reported as Free. A free chair was found,
                // and whether posing a companion on one actually looks right is
                // an open evidence row — the audit could only confirm that
                // Chair.m_attachAnimation is a per-prefab string, not what it
                // contains or how it lands. Unverified is what the shared
                // planner treats as no seat, so the companion sits on the
                // ground beside the chair rather than standing inside it, and
                // the console tool reports the seat as detected-but-unused
                // instead of pretending furniture support works.
                seat = SeatAvailability.Unverified;
                SeatSeen = true;
            }

            return rejections;
        }
        catch
        {
            return PlacementRejection.None;
        }
    }

    /// <summary>True once a free seat has been detected near any candidate this
    /// session. Reported by the console tool as pending evidence.</summary>
    public bool SeatSeen { get; private set; }

    private static bool IsWithin(Collider hit, Vector3 point, float radius)
    {
        try
        {
            return (hit.ClosestPoint(point) - point).sqrMagnitude <= radius * radius;
        }
        catch
        {
            // A collider shape that cannot answer counts as in the way.
            return true;
        }
    }

    /// <summary>Open flame, as opposed to warmth: the same effect-area
    /// mechanism, asked a different question.</summary>
    private static bool IsHazardousFire(Vector3 grounded)
    {
        try
        {
            return EffectArea.IsPointInsideArea(grounded, EffectArea.Type.Burning, 0.5f) != null
                || EffectArea.IsPointInsideArea(grounded, EffectArea.Type.Fire, 0.5f) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Inside a location that clears its own ground — a shrine, a
    /// boss altar, the starting temple. Their geometry is authored, and a
    /// companion standing in the middle of it looks like a bug.</summary>
    private bool IsInsideClearedLocation(Vector3 grounded)
    {
        try
        {
            int count = Physics.OverlapSphereNonAlloc(
                grounded, LocationProbeRadius, _overlapBuffer, ~0, QueryTriggerInteraction.Collide);

            for (int index = 0; index < count; index++)
            {
                Collider hit = _overlapBuffer[index];
                var location = hit == null ? null : hit.GetComponentInParent<Location>();
                if (location == null || !location.m_clearArea)
                {
                    continue;
                }

                float distance = Vector3.Distance(grounded, location.transform.position);
                if (distance <= Mathf.Max(location.m_exteriorRadius, location.m_interiorRadius))
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
