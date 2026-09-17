using System;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Navigation;

/// <summary>Measures a live cart for route planning (#314, CART-05): its width,
/// its length from handle tip to tail, the hitch length from the handle to the
/// axle, and its height - from the cart's own enabled, solid colliders at the
/// moment of reading, so a load that raises the cart's load visuals is measured
/// as loaded. Read-only.
///
/// Each collider's own local box is carried into the cart's frame (the handle
/// direction forward), so a turned cart measures the same as a straight one; a
/// collider of an unknown shape falls back to its world bounds, which can only
/// make the cart look bigger. Anything implausible is refused, never guessed.
/// </summary>
internal static class CartFootprintReader
{
    /// <summary>The vanilla cart as its prefab defines it (Valheim 1.0.12,
    /// builds 25253764 and 25364265, <c>Assets/GameElements/Cart/Cart.prefab</c>):
    /// wheel faces 1.72 m apart, 3.25 m from handle tip to tail board, the
    /// attach point 2.21 m ahead of the axle. For planning before a cart is
    /// read; a live reading always replaces it.</summary>
    public static CartFootprint VanillaCart => new CartFootprint(1.72f, 3.25f, 2.21f);

    /// <summary>The vanilla cart's height above its wheels' lowest point, up to
    /// the top of its container collider.</summary>
    public const float VanillaCartTopMetres = 1.4f;

    /// <summary>Reads <paramref name="cartComponent"/> (a live cart). False when
    /// it is not one, the capability is off, it has no solid colliders, or the
    /// measure is implausible. Never throws.</summary>
    public static bool TryRead(object? cartComponent, out CartFootprint footprint, out float topMetres)
    {
        footprint = default;
        topMetres = 0f;
        if (cartComponent is null || !NavigationCapability.Enabled)
        {
            return false;
        }

        try
        {
            return ReadCore(cartComponent, out footprint, out topMetres);
        }
        catch (Exception)
        {
            footprint = default;
            topMetres = 0f;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadCore(object cartComponent, out CartFootprint footprint, out float topMetres)
    {
        footprint = default;
        topMetres = 0f;
        Vagon? cart = cartComponent as Vagon;
        if (cart == null)
        {
            return false;
        }

        Transform root = cart.transform;
        Vector3 hitch = cart.m_attachPoint != null ? cart.m_attachPoint.position : root.position;
        Vector3 forward = hitch - root.position;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = root.forward;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        forward.Normalize();
        var right = new Vector3(forward.z, 0f, -forward.x);
        var extent = new Extent(hitch, forward, right);

        foreach (Collider collider in cart.GetComponentsInChildren<Collider>())
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            AddCollider(collider, ref extent);
        }

        if (!extent.Any)
        {
            return false;
        }

        float width = extent.MaxRight - extent.MinRight;
        float length = extent.MaxForward - extent.MinForward;
        float axle = (extent.MinForward + extent.MaxForward) * 0.5f;
        Rigidbody[]? wheels = cart.m_wheels;
        if (wheels != null && wheels.Length > 0)
        {
            float sum = 0f;
            int counted = 0;
            foreach (Rigidbody wheel in wheels)
            {
                if (wheel != null)
                {
                    // The vanilla wheels pivot on the axle.
                    sum += Vector3.Dot(wheel.transform.position - hitch, forward);
                    counted++;
                }
            }

            if (counted > 0)
            {
                axle = sum / counted;
            }
        }

        float hitchLength = Math.Max(0f, -axle);
        float top = extent.MaxY - extent.MinY;
        if (!(width > 0.2f && width < 6f) || !(length > 0.5f && length < 12f) || hitchLength > length ||
            !(top > 0.2f && top < 10f))
        {
            return false;
        }

        footprint = new CartFootprint(width, length, hitchLength);
        topMetres = top;
        return true;
    }

    private static void AddCollider(Collider collider, ref Extent extent)
    {
        Transform transform = collider.transform;
        switch (collider)
        {
            case BoxCollider box:
                AddLocalBox(transform, box.center, box.size * 0.5f, ref extent);
                return;
            case SphereCollider sphere:
                AddLocalBox(transform, sphere.center, Vector3.one * sphere.radius, ref extent);
                return;
            case CapsuleCollider capsule:
            {
                float along = Math.Max(capsule.height * 0.5f, capsule.radius);
                var half = new Vector3(capsule.radius, capsule.radius, capsule.radius);
                half[Math.Max(0, Math.Min(2, capsule.direction))] = along;
                AddLocalBox(transform, capsule.center, half, ref extent);
                return;
            }

            case MeshCollider mesh when mesh.sharedMesh != null:
            {
                Bounds bounds = mesh.sharedMesh.bounds;
                AddLocalBox(transform, bounds.center, bounds.extents, ref extent);
                return;
            }

            default:
            {
                Bounds world = collider.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    extent.Add(world.center + Corner(world.extents, corner));
                }

                return;
            }
        }
    }

    private static void AddLocalBox(Transform transform, Vector3 centre, Vector3 half, ref Extent extent)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            extent.Add(transform.TransformPoint(centre + Corner(half, corner)));
        }
    }

    private static Vector3 Corner(Vector3 half, int corner)
    {
        return new Vector3(
            (corner & 1) == 0 ? -half.x : half.x,
            (corner & 2) == 0 ? -half.y : half.y,
            (corner & 4) == 0 ? -half.z : half.z);
    }

    private struct Extent
    {
        private readonly Vector3 _origin;
        private readonly Vector3 _forward;
        private readonly Vector3 _right;

        public Extent(Vector3 origin, Vector3 forward, Vector3 right)
        {
            _origin = origin;
            _forward = forward;
            _right = right;
            Any = false;
            MinRight = float.MaxValue;
            MaxRight = float.MinValue;
            MinForward = float.MaxValue;
            MaxForward = float.MinValue;
            MinY = float.MaxValue;
            MaxY = float.MinValue;
        }

        public bool Any { get; private set; }

        public float MinRight { get; private set; }

        public float MaxRight { get; private set; }

        public float MinForward { get; private set; }

        public float MaxForward { get; private set; }

        public float MinY { get; private set; }

        public float MaxY { get; private set; }

        public void Add(Vector3 point)
        {
            Vector3 offset = point - _origin;
            float across = Vector3.Dot(offset, _right);
            float along = Vector3.Dot(offset, _forward);
            MinRight = Math.Min(MinRight, across);
            MaxRight = Math.Max(MaxRight, across);
            MinForward = Math.Min(MinForward, along);
            MaxForward = Math.Max(MaxForward, along);
            MinY = Math.Min(MinY, point.y);
            MaxY = Math.Max(MaxY, point.y);
            Any = true;
        }
    }
}
