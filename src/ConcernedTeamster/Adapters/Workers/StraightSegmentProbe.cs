using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary><b>PLACEHOLDER</b> ground port for
/// <see cref="StraightLinePlaceholderPlanner"/>, replaced with agent B's
/// navigation adapters (#314). Flat open ground only.
///
/// Each sample reads: whether the zone is loaded, the terrain height
/// (<c>Heightmap.GetHeight</c>, terrain only), whether the water surface is above
/// it, and whether any static collider intrudes into a box the cart's width plus
/// side clearance wide, from 0.3 m above the ground (a step Gunnar's cart rolls
/// over) to 1.5 m. Moving bodies (the cart, Gunnar, the player, creatures) are
/// ignored. Read-only: it casts and measures, nothing more.</summary>
internal sealed class StraightSegmentProbe : IStraightSegmentProbe
{
    private const float StepHeightMetres = 0.3f;
    private const float CartHeightMetres = 1.5f;

    private readonly Collider[] _overlaps = new Collider[32];
    private readonly int _obstacleMask;
    private readonly Func<CartObservation?> _leasedCart;

    public StraightSegmentProbe(Func<CartObservation?> leasedCart)
    {
        _leasedCart = leasedCart ?? throw new ArgumentNullException(nameof(leasedCart));
        _obstacleMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
    }

    public StraightSegmentSample Sample(WorkPoint at, float directionX, float directionZ, float halfWidthMetres, float sampleLengthMetres)
    {
        var sample = new StraightSegmentSample();
        try
        {
            var point = new Vector3(at.X, at.Y, at.Z);
            ZoneSystem zones = ZoneSystem.instance;
            sample.Loaded = zones != null && zones.IsZoneLoaded(point);
            if (!sample.Loaded)
            {
                return sample;
            }

            sample.GroundKnown = Heightmap.GetHeight(point, out float ground);
            if (!sample.GroundKnown)
            {
                return sample;
            }

            sample.GroundHeight = ground;
            sample.InWater = Floating.GetLiquidLevel(new Vector3(at.X, ground, at.Z), 1f, LiquidType.All) > ground + 0.1f;

            var direction = new Vector3(directionX, 0f, directionZ);
            Quaternion orientation = direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
            float height = CartHeightMetres - StepHeightMetres;
            var centre = new Vector3(at.X, ground + StepHeightMetres + (height / 2f), at.Z);
            var halfExtents = new Vector3(halfWidthMetres, height / 2f, Math.Max(0.1f, sampleLengthMetres / 2f));
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, _overlaps, orientation, _obstacleMask, QueryTriggerInteraction.Ignore);
            sample.Clear = true;
            for (int index = 0; index < count; index++)
            {
                Collider collider = _overlaps[index];
                if (collider != null && collider.attachedRigidbody == null)
                {
                    sample.Clear = false;
                    break;
                }
            }
        }
        catch
        {
            // An unreadable sample refuses the route.
            sample = new StraightSegmentSample();
        }

        return sample;
    }

    public bool TryReadCartHeading(out float headingX, out float headingZ)
    {
        headingX = 0f;
        headingZ = 0f;
        CartObservation? cart = _leasedCart();
        if (cart == null || !cart.Value.Resolved)
        {
            return false;
        }

        headingX = cart.Value.HeadingX;
        headingZ = cart.Value.HeadingZ;
        return (headingX * headingX) + (headingZ * headingZ) > 0.0001f;
    }
}
