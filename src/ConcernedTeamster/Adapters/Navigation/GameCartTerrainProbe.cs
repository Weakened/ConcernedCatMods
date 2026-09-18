using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Navigation;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Navigation;

/// <summary>The running game as a <see cref="ICartTerrainProbe"/> (#314).
/// Read-only physics and zone queries on the main thread; nothing here writes to
/// the world, moves a body or sends anything.
///
/// Layers are the game's own (TagManager, Valheim 1.0.12 builds 25253764 and
/// 25364265):
/// - ground is what <c>ZoneSystem</c> itself treats as solid - Default,
///   static_solid, Default_small, piece, terrain - ignoring anything on a
///   moving body, as the game's own solid-height query does;
/// - obstacles are the same set without terrain (slopes are the ground checks'
///   business) plus <c>vehicle</c>, where other carts and ships live. Characters,
///   items, non-solid pieces and triggers never block, and neither do the leased
///   cart and Gunnar himself (<see cref="UseBodies"/>).
///
/// The cart's box runs from <see cref="BoxBottomMetres"/> - the navmesh
/// agent's step, below which the ground checks judge bumps - to
/// <see cref="BoxTopMetres"/>, the measured top of the loaded cart.</summary>
internal sealed class GameCartTerrainProbe : ICartTerrainProbe
{
    /// <summary>How far above the expected height a surface may be and still be
    /// ground rather than a roof or overhang. The navmesh agent needs 2.5 m of
    /// headroom, so nothing on a planned line is lower than that.</summary>
    public const float GroundSearchUpMetres = 1f;

    /// <summary>How far below the expected height the ground is looked for;
    /// deeper than this is a gap.</summary>
    public const float GroundSearchDownMetres = 3f;

    /// <summary>Vanilla doors are centred on a 2 m opening, so the floor is a
    /// metre below the door's origin.</summary>
    public const float DoorOriginAboveFloorMetres = 1f;

    /// <summary>Half a doorway's width, generously: a wood door's leaf is
    /// 1.39 m wide and a wood gate's 1.68 m, both centred on the piece.</summary>
    public const float DoorHalfWidthMetres = 1f;

    private readonly RaycastHit[] _rays = new RaycastHit[32];
    private readonly RaycastHit[] _hits = new RaycastHit[32];
    private readonly Collider[] _overlaps = new Collider[32];
    private readonly List<Piece> _pieces = new List<Piece>();
    private int _groundMask = -1;
    private int _obstacleMask = -1;
    private Transform? _cart;
    private Transform? _puller;

    public float BoxBottomMetres { get; private set; } = CartRouteGeometry.StepMetres;

    public float BoxTopMetres { get; private set; } = CartFootprintReader.VanillaCartTopMetres;

    /// <summary>The leased cart and Gunnar's body: never obstacles to
    /// themselves. <paramref name="cartTopMetres"/> is the cart's measured
    /// height above its lowest point (<see cref="CartFootprintReader"/>).
    /// </summary>
    public void UseBodies(Transform? cart, Transform? puller, float cartTopMetres)
    {
        _cart = cart;
        _puller = puller;
        if (cartTopMetres > BoxBottomMetres + 0.1f && cartTopMetres < 10f)
        {
            BoxTopMetres = cartTopMetres;
        }
    }

    public bool IsLoaded(WorkPoint point)
    {
        if (!NavigationCapability.Enabled || !point.IsFinite)
        {
            return false;
        }

        try
        {
            return IsLoadedCore(point);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public CartGroundSample SampleGround(float x, float z, float nearHeight)
    {
        if (!NavigationCapability.Enabled || float.IsNaN(x) || float.IsNaN(z) || float.IsNaN(nearHeight) ||
            float.IsInfinity(x) || float.IsInfinity(z) || float.IsInfinity(nearHeight))
        {
            return CartGroundSample.Missing(CartGroundStatus.Unreadable);
        }

        try
        {
            return SampleGroundCore(x, z, nearHeight);
        }
        catch (Exception)
        {
            return CartGroundSample.Missing(CartGroundStatus.Unreadable);
        }
    }

    public CartClearanceSample SweepBox(
        WorkPoint from, WorkPoint to, float headingX, float headingZ, float halfWidth, float halfLength,
        bool ignoreStartOverlaps)
    {
        if (!NavigationCapability.Enabled || !from.IsFinite || !to.IsFinite || !(halfWidth > 0f) || !(halfLength > 0f))
        {
            return CartClearanceSample.Unreadable;
        }

        try
        {
            return SweepBoxCore(from, to, headingX, headingZ, halfWidth, halfLength, ignoreStartOverlaps);
        }
        catch (Exception)
        {
            return CartClearanceSample.Unreadable;
        }
    }

    public CartClearanceSample CheckBox(WorkPoint centre, float headingX, float headingZ, float halfWidth, float halfLength)
    {
        if (!NavigationCapability.Enabled || !centre.IsFinite || !(halfWidth > 0f) || !(halfLength > 0f))
        {
            return CartClearanceSample.Unreadable;
        }

        try
        {
            return CheckBoxCore(centre, headingX, headingZ, halfWidth, halfLength);
        }
        catch (Exception)
        {
            return CartClearanceSample.Unreadable;
        }
    }

    public bool TryFindDoorways(WorkPoint centre, float radius, List<CartDoorway> doorways)
    {
        doorways.Clear();
        if (!NavigationCapability.Enabled || !centre.IsFinite || !(radius > 0f))
        {
            return false;
        }

        try
        {
            return FindDoorwaysCore(centre, radius, doorways);
        }
        catch (Exception)
        {
            doorways.Clear();
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsLoadedCore(WorkPoint point)
    {
        ZoneSystem zones = ZoneSystem.instance;
        var position = new Vector3(point.X, point.Y, point.Z);
        return zones != null && zones.IsZoneLoaded(position) && Heightmap.FindHeightmap(position) != null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CartGroundSample SampleGroundCore(float x, float z, float nearHeight)
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null)
        {
            return CartGroundSample.Missing(CartGroundStatus.Unreadable);
        }

        var at = new Vector3(x, nearHeight, z);
        Heightmap heightmap = zones.IsZoneLoaded(at) ? Heightmap.FindHeightmap(at) : null!;
        if (heightmap == null)
        {
            return CartGroundSample.Missing(CartGroundStatus.Unloaded);
        }

        EnsureMasks();
        int count = Physics.RaycastNonAlloc(
            new Vector3(x, nearHeight + GroundSearchUpMetres, z),
            Vector3.down,
            _rays,
            GroundSearchUpMetres + GroundSearchDownMetres,
            _groundMask,
            QueryTriggerInteraction.Ignore);

        int best = -1;
        for (int index = 0; index < count; index++)
        {
            Collider collider = _rays[index].collider;
            if (collider == null || collider.attachedRigidbody != null || IsOwnBody(collider.transform))
            {
                continue;
            }

            if (best < 0 || _rays[index].point.y > _rays[best].point.y)
            {
                best = index;
            }
        }

        float surface = best >= 0 ? _rays[best].point.y : nearHeight;
        float liquid = Floating.GetLiquidLevel(new Vector3(x, surface + 0.05f, z), 1f, LiquidType.All);
        float depth = Math.Max(liquid - surface, zones.m_waterLevel - surface);
        if (best < 0)
        {
            return new CartGroundSample(CartGroundStatus.NoSurface, 0f, 0f, depth, false);
        }

        return new CartGroundSample(
            CartGroundStatus.Surface, surface, _rays[best].normal.y, depth, heightmap.IsLava(at, 0.6f));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CartClearanceSample SweepBoxCore(
        WorkPoint from, WorkPoint to, float headingX, float headingZ, float halfWidth, float halfLength,
        bool ignoreStartOverlaps)
    {
        EnsureMasks();
        float halfHeight = Math.Max(0.05f, (BoxTopMetres - BoxBottomMetres) * 0.5f);
        var start = new Vector3(from.X, from.Y + BoxBottomMetres + halfHeight, from.Z);
        var end = new Vector3(to.X, to.Y + BoxBottomMetres + halfHeight, to.Z);
        Vector3 travel = end - start;
        float distance = travel.magnitude;
        Quaternion orientation = Orientation(headingX, headingZ);
        var extents = new Vector3(halfWidth, halfHeight, halfLength);
        if (distance < 0.01f)
        {
            return ignoreStartOverlaps ? CartClearanceSample.Clear : Overlap(start, extents, orientation);
        }

        int count = Physics.BoxCastNonAlloc(
            start, extents, travel / distance, _hits, orientation, distance, _obstacleMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Collider? blocker = null;
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = _hits[index];
            Collider collider = hit.collider;
            if (collider == null || IsOwnBody(collider.transform))
            {
                continue;
            }

            // A box already overlapping something where the sweep starts reports
            // it at distance zero with no contact point.
            bool atStart = hit.distance <= 0f && hit.point == Vector3.zero;
            if (atStart && ignoreStartOverlaps)
            {
                continue;
            }

            float at = atStart ? 0f : hit.distance;
            if (at < nearest)
            {
                nearest = at;
                blocker = collider;
            }
        }

        return blocker == null ? CartClearanceSample.Clear : CartClearanceSample.BlockedAt(nearest, Describe(blocker));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private CartClearanceSample CheckBoxCore(WorkPoint centre, float headingX, float headingZ, float halfWidth, float halfLength)
    {
        EnsureMasks();
        float halfHeight = Math.Max(0.05f, (BoxTopMetres - BoxBottomMetres) * 0.5f);
        return Overlap(
            new Vector3(centre.X, centre.Y + BoxBottomMetres + halfHeight, centre.Z),
            new Vector3(halfWidth, halfHeight, halfLength),
            Orientation(headingX, headingZ));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool FindDoorwaysCore(WorkPoint centre, float radius, List<CartDoorway> doorways)
    {
        _pieces.Clear();
        Piece.GetAllPiecesInRadius(new Vector3(centre.X, centre.Y, centre.Z), radius, _pieces);
        foreach (Piece piece in _pieces)
        {
            Door? door = piece == null ? null : piece.GetComponentInChildren<Door>();
            if (door == null)
            {
                continue;
            }

            Transform transform = door.transform;
            Vector3 position = transform.position;
            Vector3 facing = transform.forward;
            doorways.Add(new CartDoorway(
                new WorkPoint(position.x, position.y - DoorOriginAboveFloorMetres, position.z),
                facing.x, facing.z, DoorHalfWidthMetres));
        }

        _pieces.Clear();
        return true;
    }

    private CartClearanceSample Overlap(Vector3 centre, Vector3 extents, Quaternion orientation)
    {
        int count = Physics.OverlapBoxNonAlloc(
            centre, extents, _overlaps, orientation, _obstacleMask, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            Collider collider = _overlaps[index];
            if (collider != null && !IsOwnBody(collider.transform))
            {
                return CartClearanceSample.BlockedAt(0f, Describe(collider));
            }
        }

        return CartClearanceSample.Clear;
    }

    private bool IsOwnBody(Transform transform)
    {
        return (_cart != null && transform.IsChildOf(_cart)) || (_puller != null && transform.IsChildOf(_puller));
    }

    private void EnsureMasks()
    {
        if (_groundMask == -1)
        {
            _groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        }

        if (_obstacleMask == -1)
        {
            _obstacleMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
        }
    }

    private static Quaternion Orientation(float headingX, float headingZ)
    {
        var forward = new Vector3(headingX, 0f, headingZ);
        return forward.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
    }

    private static string Describe(Collider collider)
    {
        Piece? piece = collider.GetComponentInParent<Piece>();
        string name = (piece != null ? piece.gameObject.name : collider.gameObject.name).Replace("(Clone)", string.Empty).Trim();
        Vector3 centre = collider.bounds.center;
        return $"{name} ({LayerMask.LayerToName(collider.gameObject.layer)}) at ({centre.x:0.0}, {centre.y:0.0}, {centre.z:0.0})";
    }
}
