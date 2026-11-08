using System;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Presentation;

/// <summary>Answers "could Thorstein sit here?" for one candidate point near
/// home (#295 S5), for the shared <see cref="PlacementPlanner"/>.
///
/// <b>It reads and changes nothing.</b> Every question is a bounded physics or
/// effect-area query about what is already loaded: no terrain edit, no world
/// write, nothing claimed, moved or opened. A point in a zone that is not
/// loaded is <see cref="PlacementProbeSample.NotLoaded"/>, so the planner
/// defers instead of seating him on ground nobody has seen.
///
/// The questions are the ones Hulgi's probe in Concerned Cartographer asks,
/// written against the same game calls, because Foreman may not depend on
/// Cartographer. What Thorstein does not need is left out: seats (a slice of
/// its own - a chair has to be given up the moment a player sits down) and the
/// door-leaf allowance of a body that walks.
///
/// <b>Footing</b> is the nearest solid surface to the candidate's own height,
/// within a small window, that has room to stand above it - not
/// <c>ZoneSystem.GetSolidHeight</c>, which returns the first solid thing below
/// a point a kilometre up, which under a roof is the roof.</summary>
internal sealed class ThorsteinRestProbe : IPlacementProbe
{
    private const float SearchUp = 2f;
    private const float SearchDown = 3f;
    private const float Headroom = 1.8f;

    /// <summary>About forty degrees: a sloped camp, not a cliff.</summary>
    private const float MinimumUpDot = 0.76f;

    /// <summary>His standing body, for the clearance test: above a normal
    /// step, to head height.</summary>
    private const float BodyBottom = 0.55f;
    private const float BodyTop = 1.6f;
    private const float BodyRadius = 0.3f;
    private const float BodyCentre = 0.9f;

    /// <summary>How far around him beds, fires, doors and people are looked
    /// for.</summary>
    private const float NeighbourhoodRadius = 1.5f;

    /// <summary>On or against a bed, or the fire piece itself, is refused;
    /// beside either is where a camp is.</summary>
    private const float BedMargin = 1f;
    private const float FireMargin = 1f;

    /// <summary>Another body this close is in his seat.</summary>
    private const float CharacterMargin = 0.6f;

    private const float LocationProbeRadius = 3f;

    private readonly Action<string> _log;
    private readonly float _warmthRadius;
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Collider[] _overlap = new Collider[128];
    private int _solidMask = -1;
    private int _bodyMask = -1;
    private bool _faultSaid;

    internal ThorsteinRestProbe(Action<string> log, float warmthRadius)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _warmthRadius = warmthRadius > 0f ? warmthRadius : PlacementRules.DefaultFireComfortRadius;
    }

    public PlacementProbeSample Probe(WorldPoint position) => Sample(position, countBodies: true);

    /// <summary>The spot he already sits on, asked again. Another body near it
    /// does not count: the player who walked up to talk to him, or a tame
    /// wandering past, is no reason for him to get up and move away. Everything
    /// built or burning still counts.</summary>
    internal PlacementProbeSample Recheck(WorldPoint position) => Sample(position, countBodies: false);

    private PlacementProbeSample Sample(WorldPoint position, bool countBodies)
    {
        var point = new Vector3(position.X, position.Y, position.Z);
        try
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || !zones.IsZoneLoaded(point))
            {
                return PlacementProbeSample.NotLoaded(position);
            }

            if (!TryFindFooting(point, out Vector3 grounded, out Vector3 normal))
            {
                return new PlacementProbeSample(position, PlacementRejection.Unsupported, -1f, SeatOffer.None);
            }

            PlacementRejection rejections = PlacementRejection.None;
            if (Vector3.Dot(normal, Vector3.up) < MinimumUpDot)
            {
                rejections |= PlacementRejection.TooSteep;
            }

            if (Floating.GetLiquidLevel(grounded) > grounded.y + 0.05f)
            {
                rejections |= PlacementRejection.Water;
            }

            if (IsBodyObstructed(grounded))
            {
                rejections |= PlacementRejection.Occupied;
            }

            rejections |= SurveyNeighbourhood(grounded, countBodies);

            if (EffectArea.IsPointInsideArea(grounded, EffectArea.Type.Burning, 0.5f) != null)
            {
                rejections |= PlacementRejection.Fire;
            }

            if (IsInsideClearedLocation(grounded))
            {
                rejections |= PlacementRejection.Shrine;
            }

            float warmth = TryFindWarmth(grounded, out _) ? 0f : -1f;
            return new PlacementProbeSample(
                new WorldPoint(grounded.x, grounded.y, grounded.z), rejections, warmth, SeatOffer.None);
        }
        catch (Exception exception)
        {
            if (!_faultSaid)
            {
                _faultSaid = true;
                _log("A spot near home could not be read for Thorstein to sit on; such spots are skipped: " +
                    SafeFailure.Brief(exception));
            }

            return new PlacementProbeSample(position, PlacementRejection.Unsupported, -1f, SeatOffer.None);
        }
    }

    /// <summary>Whether <paramref name="spot"/> is in a fire's warmth, and where
    /// that fire is, so he can face it. The comfort radius is asked WITH,
    /// because the game's call answers inside-or-outside for the radius it is
    /// given.</summary>
    internal bool TryFindWarmth(Vector3 spot, out Vector3 fire)
    {
        fire = spot;
        try
        {
            EffectArea? area = EffectArea.IsPointInsideArea(
                spot, EffectArea.Type.Heat | EffectArea.Type.Fire, _warmthRadius);
            if (area == null)
            {
                return false;
            }

            fire = area.transform.position;
            return true;
        }
        catch (Exception)
        {
            // Warmth only makes a spot nicer; not knowing costs that and
            // nothing else.
            return false;
        }
    }

    private bool TryFindFooting(Vector3 point, out Vector3 grounded, out Vector3 normal)
    {
        grounded = point;
        normal = Vector3.up;
        if (_solidMask == -1)
        {
            _solidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        }

        int count = Physics.RaycastNonAlloc(
            point + (Vector3.up * SearchUp), Vector3.down, _hits, SearchUp + SearchDown, _solidMask,
            QueryTriggerInteraction.Ignore);

        int best = -1;
        float bestGap = float.MaxValue;
        for (int index = 0; index < count; index++)
        {
            RaycastHit candidate = _hits[index];
            if (candidate.collider == null || candidate.collider.attachedRigidbody != null)
            {
                continue;
            }

            float surface = candidate.point.y;
            bool room = true;
            for (int other = 0; other < count; other++)
            {
                RaycastHit above = _hits[other];
                if (other == index || above.collider == null || above.collider.attachedRigidbody != null)
                {
                    continue;
                }

                float rise = above.point.y - surface;
                if (rise > 0.05f && rise < Headroom)
                {
                    room = false;
                    break;
                }
            }

            float gap = Mathf.Abs(surface - point.y);
            if (room && gap < bestGap)
            {
                bestGap = gap;
                best = index;
            }
        }

        if (best < 0)
        {
            return false;
        }

        grounded = new Vector3(point.x, _hits[best].point.y, point.z);
        normal = _hits[best].normal;
        return true;
    }

    private bool IsBodyObstructed(Vector3 grounded)
    {
        if (_bodyMask == -1)
        {
            _bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
        }

        return Physics.CheckCapsule(
            grounded + (Vector3.up * BodyBottom), grounded + (Vector3.up * BodyTop), BodyRadius, _bodyMask,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>One overlap for a bed, the fire piece, a door and another body.
    /// Only solid colliders count: a bed's and a fire's large trigger volumes
    /// are the camp doing its job, not an obstruction.</summary>
    private PlacementRejection SurveyNeighbourhood(Vector3 grounded, bool countBodies)
    {
        Vector3 centre = grounded + (Vector3.up * BodyCentre);
        int count = Physics.OverlapSphereNonAlloc(
            centre, NeighbourhoodRadius, _overlap, ~0, QueryTriggerInteraction.Ignore);

        PlacementRejection rejections = PlacementRejection.None;
        for (int index = 0; index < count; index++)
        {
            Collider hit = _overlap[index];
            if (hit == null || hit.isTrigger || hit.GetComponent<Heightmap>() != null)
            {
                continue;
            }

            if (hit.GetComponentInParent<Bed>() != null)
            {
                if (IsWithin(hit, centre, BedMargin))
                {
                    rejections |= PlacementRejection.Bed;
                }

                continue;
            }

            if (hit.GetComponentInParent<Fireplace>() != null)
            {
                if (IsWithin(hit, grounded + (Vector3.up * 0.3f), FireMargin))
                {
                    rejections |= PlacementRejection.Fire;
                }

                continue;
            }

            if (hit.GetComponentInParent<Door>() != null)
            {
                rejections |= PlacementRejection.Doorway;
                continue;
            }

            if (countBodies && hit.GetComponentInParent<Character>() != null && IsWithin(hit, centre, CharacterMargin))
            {
                rejections |= PlacementRejection.Occupied;
            }
        }

        return rejections;
    }

    /// <summary>Inside a location that clears its own ground - the starting
    /// temple, a shrine, an altar - whose authored layout he does not sit in.
    /// </summary>
    private bool IsInsideClearedLocation(Vector3 grounded)
    {
        int count = Physics.OverlapSphereNonAlloc(
            grounded, LocationProbeRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
        for (int index = 0; index < count; index++)
        {
            Collider hit = _overlap[index];
            Location? location = hit == null ? null : hit.GetComponentInParent<Location>();
            if (location == null || !location.m_clearArea)
            {
                continue;
            }

            float reach = Mathf.Max(location.m_exteriorRadius, location.m_interiorRadius);
            if (Vector3.Distance(grounded, location.transform.position) <= reach)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithin(Collider hit, Vector3 point, float radius)
    {
        try
        {
            return (hit.ClosestPoint(point) - point).sqrMagnitude <= radius * radius;
        }
        catch (Exception)
        {
            // A shape that cannot answer counts as in the way.
            return true;
        }
    }
}
