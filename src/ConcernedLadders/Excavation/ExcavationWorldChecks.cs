using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The world questions the commit gate asks before a dig or an undo changes ground (#487 slice 2; the rectangle
/// since #551): wards, no-build locations and objects in the changed rectangle. Read-only; nothing here moves, removes or
/// claims anything.
///
/// Read from the decompiled installed game:
/// <list type="bullet">
/// <item><b>Wards.</b> The pickaxe's path asks <c>PrivateArea.CheckAccess(point, opRadius)</c> at each operation's point,
/// and in its default mode passes when ANY overlapping ward grants access. Here it is asked once for the whole changed
/// rectangle, at <see cref="AccessCheckRadius"/> (the circle through its corners) with <c>wardCheck: true</c>, which
/// refuses when any overlapping ward does not grant access. Whatever the pickaxe's per-vertex check refuses, this refuses
/// too.</item>
/// <item><b>No-build locations.</b> Each no-build location is asked <c>IsInside(centre, AccessCheckRadius, buildCheck:
/// true)</c>, which covers the whole rectangle. The location list is private, so it is found once by reflection; when it
/// cannot be found the feature is unavailable rather than half-checked.</item>
/// <item><b>Objects</b> (#567, owner playtest 2026-11-11: "digging should ignore pickable objects and others and just destroy
/// them ... unless there's a more graceful way"). A terrain op leaves whatever stands on the ground where it is, floating
/// over a dig. <see cref="Survey"/> sorts what is in the changed rectangle: building pieces (counted, never touched - digging
/// beside your own house is the point); pickables (picked, so their items drop for the player, then removed: the graceful
/// way); loose items (left to fall onto the new floor); other networked world objects - rocks, stumps, bushes, logs, trees
/// (removed through <see cref="ExcavationClearing"/>, the one place that does it); and what may never be removed or cannot
/// be removed persistently - carts, ships, anything on a moving body, static props with no network object - which still
/// refuse, by name. An undo refuses whatever stands in the hole, a piece included, since raising the ground would bury it.</item>
/// </list></summary>
internal sealed class ExcavationWorldChecks
{
    /// <summary>How far above the highest sampled ground the object scan reaches.</summary>
    internal const float ScanHeadroom = 0.3f;

    /// <summary>How far below the floor the object scan reaches.</summary>
    internal const float ScanFootroom = 0.1f;

    /// <summary>Inset from the changed rectangle, so an object merely touching the untouched ring line does not refuse.</summary>
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

    /// <summary>The circle through the corners of everything a dig of <paramref name="footprint"/> can change: the floor
    /// and the one sloping cell around it.</summary>
    internal static float AccessCheckRadius(DigFootprint footprint)
    {
        float halfX = (footprint.Width + 2) * ExcavationGeometry.VertexSpacing * 0.5f;
        float halfZ = (footprint.Length + 2) * ExcavationGeometry.VertexSpacing * 0.5f;
        return (float)Math.Sqrt(halfX * halfX + halfZ * halfZ);
    }

    /// <summary>The ward check at the rectangle's corner radius, refusing if any overlapping ward lacks access. Flashes the
    /// refusing ward, as the pickaxe does.</summary>
    internal static bool WardAllows(DigFootprint footprint, float y) =>
        PrivateArea.CheckAccess(Centre(footprint, y), AccessCheckRadius(footprint), flash: true, wardCheck: true);

    /// <summary>The same ward question for the preview, without flashing anything: with <c>flash: false</c> the game's
    /// CheckAccess only reads the wards (read from the installed game).</summary>
    internal static bool WardAllowsQuietly(DigFootprint footprint, float y) =>
        PrivateArea.CheckAccess(Centre(footprint, y), AccessCheckRadius(footprint), flash: false, wardCheck: true);

    /// <summary>True when any no-build location reaches into the rectangle. Fails closed: an unreadable list is a hit.</summary>
    internal bool InNoBuildArea(DigFootprint footprint, float y)
    {
        if (_locations == null || !(_locations.GetValue(null) is IList locations)) return true;
        Vector3 centre = Centre(footprint, y);
        float radius = AccessCheckRadius(footprint);
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

    private readonly HashSet<GameObject> _seen = new HashSet<GameObject>();

    /// <summary>Sorts everything in the changed rectangle from <paramref name="bottom"/> to <paramref name="top"/> (#567).
    /// Fills <paramref name="toPick"/> with pickables and <paramref name="toRemove"/> with other networked world objects
    /// (each object once, by its network root), <paramref name="fills"/> with earth blocks (#577; which of them a dig takes
    /// out is the runtime's to decide), counts building pieces into <paramref name="pieces"/>, and returns how many
    /// objects block the dig (carts, ships, moving bodies, static props), the first one's name in
    /// <paramref name="blocker"/>. <paramref name="overflow"/> is set when the buffer filled, so the scan could not see
    /// everything (which refuses). Read-only: it never touches what it finds.</summary>
    internal int Survey(DigFootprint footprint, float bottom, float top, List<GameObject> toPick, List<GameObject> toRemove,
        List<GameObject> fills, out int pieces, out bool overflow, out string? blocker)
    {
        toPick.Clear();
        toRemove.Clear();
        fills.Clear();
        _seen.Clear();
        int count = Overlap(footprint, bottom, top, out overflow);
        pieces = 0;
        blocker = null;
        int blocking = 0;
        for (int i = 0; i < count; i++)
        {
            Collider hit = _hits[i];
            _hits[i] = null!;
            if (hit == null) continue;
            Rigidbody body = hit.attachedRigidbody;
            bool vehicle = hit.GetComponentInParent<Vagon>() != null || hit.GetComponentInParent<Ship>() != null;
            // A structural building piece (it has WearNTear, so the game's own support and collapse govern it): never touched.
            if (!vehicle && hit.GetComponentInParent<WearNTear>() != null) { pieces++; continue; }
            // A loose item falls onto the new floor by itself.
            if (!vehicle && hit.GetComponentInParent<ItemDrop>() != null) continue;
            ZNetView? view = hit.GetComponentInParent<ZNetView>();
            // An earth block (#577): never cleared as a world object, never counted as a building piece.
            if (!vehicle && view != null && view.IsValid() && EarthFillPlacement.IsFill(view.gameObject))
            {
                if (_seen.Add(view.gameObject)) fills.Add(view.gameObject);
                continue;
            }

            bool moving = body != null && !body.isKinematic;
            if (vehicle || moving || view == null || !view.IsValid())
            {
                blocking++;
                blocker ??= NameOf(hit);
                continue;
            }

            if (!_seen.Add(view.gameObject)) continue;
            if (view.GetComponent<Pickable>() != null) toPick.Add(view.gameObject);
            else toRemove.Add(view.gameObject);
        }

        return blocking;
    }

    private int Overlap(DigFootprint footprint, float bottom, float top, out bool overflow)
    {
        if (_mask == 0)
        {
            // The placement ray's own layers (Player.m_placeRayMask) without terrain, plus loose items.
            _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle", "item");
        }

        float low = bottom - ScanFootroom;
        float high = top + ScanHeadroom;
        float halfX = (footprint.Width + 2) * ExcavationGeometry.VertexSpacing * 0.5f - ScanInset;
        float halfZ = (footprint.Length + 2) * ExcavationGeometry.VertexSpacing * 0.5f - ScanInset;
        Vector3 centre = Centre(footprint, (low + high) * 0.5f);
        var extents = new Vector3(halfX, (high - low) * 0.5f, halfZ);
        int count = Physics.OverlapBoxNonAlloc(centre, extents, _hits, Quaternion.identity, _mask, QueryTriggerInteraction.Ignore);
        overflow = count >= _hits.Length;
        return count;
    }

    private static string NameOf(Collider hit)
    {
        Transform root = hit.transform.root;
        string name = (root != null ? root : hit.transform).name;
        int clone = name.IndexOf('(');
        return clone > 0 ? name.Substring(0, clone).Trim() : name;
    }

    private static Vector3 Centre(DigFootprint footprint, float y) =>
        new Vector3((footprint.MinX + footprint.MaxX) * 0.5f * ExcavationGeometry.VertexSpacing, y,
            (footprint.MinZ + footprint.MaxZ) * 0.5f * ExcavationGeometry.VertexSpacing);
}
