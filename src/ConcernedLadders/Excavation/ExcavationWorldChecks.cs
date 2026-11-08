using System;
using System.Collections;
using System.Reflection;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The world questions the commit gate asks of a Ready plan (#487 slice 2): wards, no-build locations and
/// objects in the changed square. Read-only; nothing here moves, removes or claims anything.
///
/// Read from the decompiled installed game:
/// <list type="bullet">
/// <item><b>Wards.</b> The pickaxe's path asks <c>PrivateArea.CheckAccess(point, opRadius)</c>, a circle that misses a
/// square op's floor corners (EXCAVATION.md claim 10), and in its default mode passes when ANY overlapping ward grants
/// access - so merely widening that radius would also pull in more wards that grant. Here it is asked at
/// <see cref="ExcavationGeometry.AccessCheckRadius"/> (the farthest border corner) with <c>wardCheck: true</c>, which
/// refuses when any overlapping ward does not grant access. Whatever the pickaxe's check refuses, this refuses
/// too.</item>
/// <item><b>No-build locations.</b> The game's public test, <c>Location.IsInsideNoBuildLocation(point)</c>, takes a
/// point only. Each no-build location is instead asked <c>IsInside(centre, AccessCheckRadius, buildCheck: true)</c>,
/// which covers the whole square. The location list is private, so it is found once by reflection; when it cannot be
/// found the feature is unavailable rather than half-checked.</item>
/// <item><b>Objects.</b> A terrain op leaves rocks, trees, ore, items, carts and ships where they are, floating if the
/// ground under them goes (claim 11). Any such collider in the changed square, from the floor to just above the
/// highest ground, refuses. Structural building pieces (with WearNTear) do not refuse - digging under or beside your own house is the point - and
/// are counted so the result can say how many stand on changed ground; their support stays native.</item>
/// </list></summary>
internal sealed class ExcavationWorldChecks
{
    /// <summary>How far above the highest sampled ground the object scan reaches.</summary>
    internal const float ScanHeadroom = 0.3f;

    /// <summary>How far below the floor the object scan reaches.</summary>
    internal const float ScanFootroom = 0.1f;

    /// <summary>Inset from the border square, so an object merely touching the untouched ring line does not refuse.</summary>
    internal const float ScanInset = 0.05f;

    private const int ScanCapacity = 128;

    private readonly Collider[] _hits = new Collider[ScanCapacity];
    private readonly FieldInfo? _locations;
    private int _mask;

    internal ExcavationWorldChecks()
    {
        FieldInfo? field = typeof(Location).GetField("s_allLocations", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        _locations = field != null && typeof(IList).IsAssignableFrom(field.FieldType) ? field : null;
        Missing = _locations == null ? "Location.s_allLocations" : null;
    }

    internal bool Available => Missing == null;

    internal string? Missing { get; }

    /// <summary>The ward check at the geometry's wider radius, refusing if any overlapping ward lacks access. Flashes
    /// the refusing ward, as the pickaxe does.</summary>
    internal static bool WardAllows(in ExcavationPlan plan) =>
        PrivateArea.CheckAccess(Centre(plan, plan.Datum), plan.Geometry.AccessCheckRadius, flash: true, wardCheck: true);

    /// <summary>True when any no-build location reaches into the square. Fails closed: an unreadable list is a hit.</summary>
    internal bool InNoBuildArea(in ExcavationPlan plan)
    {
        if (_locations == null || !(_locations.GetValue(null) is IList locations)) return true;
        Vector3 centre = Centre(plan, plan.Datum);
        float radius = plan.Geometry.AccessCheckRadius;
        for (int i = 0; i < locations.Count; i++)
        {
            if (locations[i] is Location location && location != null && location.m_noBuild &&
                location.IsInside(centre, radius, buildCheck: true))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Scans the changed square. Returns how many colliders belong to something other than a building piece
    /// (the first one's prefab name in <paramref name="firstObstruction"/>), counts building-piece colliders into
    /// <paramref name="pieces"/>, and sets <paramref name="overflow"/> when the buffer filled, so the scan could not
    /// see everything (which refuses).</summary>
    internal int Obstructions(in ExcavationPlan plan, float highestGround, out int pieces, out bool overflow, out string? firstObstruction)
    {
        if (_mask == 0)
        {
            // The placement ray's own layers (Player.m_placeRayMask) without terrain, plus loose items.
            _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle", "item");
        }
        float bottom = plan.Bottom - ScanFootroom;
        float top = Math.Max(highestGround, plan.Datum) + ScanHeadroom;
        float half = plan.Geometry.RingDistance * ExcavationGeometry.VertexSpacing - ScanInset;
        Vector3 centre = Centre(plan, (bottom + top) * 0.5f);
        var extents = new Vector3(half, (top - bottom) * 0.5f, half);
        int count = Physics.OverlapBoxNonAlloc(centre, extents, _hits, Quaternion.identity, _mask, QueryTriggerInteraction.Ignore);
        overflow = count >= _hits.Length;
        pieces = 0;
        firstObstruction = null;
        int obstructions = 0;
        for (int i = 0; i < count; i++)
        {
            Collider hit = _hits[i];
            _hits[i] = null!;
            if (hit == null) continue;
            // A structural building piece (it has WearNTear, so the game's own support and collapse govern it). Carts and
            // ships carry WearNTear too but are vehicles, and anything on a moving rigidbody could be left hanging; they
            // refuse first. A planted crop or sapling is a Piece without WearNTear and would float, so it refuses too.
            Rigidbody body = hit.attachedRigidbody;
            bool vehicle = hit.GetComponentInParent<Vagon>() != null || hit.GetComponentInParent<Ship>() != null ||
                           (body != null && !body.isKinematic);
            if (!vehicle && hit.GetComponentInParent<WearNTear>() != null) { pieces++; continue; }
            obstructions++;
            if (firstObstruction == null)
            {
                Transform root = hit.transform.root;
                string name = (root != null ? root : hit.transform).name;
                int clone = name.IndexOf('(');
                firstObstruction = clone > 0 ? name.Substring(0, clone).Trim() : name;
            }
        }
        return obstructions;
    }

    private static Vector3 Centre(in ExcavationPlan plan, float y) =>
        new Vector3(plan.CentreX * ExcavationGeometry.VertexSpacing, y, plan.CentreZ * ExcavationGeometry.VertexSpacing);
}
