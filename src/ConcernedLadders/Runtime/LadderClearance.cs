using System;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>One reusable query buffer. Every move checks the full capsule and the whole proposed route.</summary>
internal sealed class LadderClearance
{
    private const float Skin = 0.02f;
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private readonly Collider[] _overlaps = new Collider[64];
    private readonly Player _player;
    private readonly CapsuleCollider _capsule;
    private readonly int _mask;
    private readonly Vector3 _centreOffset;
    private readonly float _halfSegment;
    internal float Radius { get; }

    internal LadderClearance(Player player, CapsuleCollider capsule, Vector3 feet)
    {
        _player = player;
        _capsule = capsule;
        Vector3 scale = capsule.transform.lossyScale;
        if (capsule.direction != 1 || !capsule.enabled || !LadderSegment.Finite(scale))
        {
            throw new InvalidOperationException("The player's capsule is not a supported upright capsule.");
        }

        Radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(2f * Radius, capsule.height * Mathf.Abs(scale.y));
        _halfSegment = Mathf.Max(0f, height * 0.5f - Radius);
        _centreOffset = capsule.transform.TransformPoint(capsule.center) - feet;
        if (!LadderSegment.Finite(_centreOffset) || Mathf.Abs(_centreOffset.x) > 0.05f || Mathf.Abs(_centreOffset.z) > 0.05f ||
            Radius < 0.05f || Radius > 1f || height > 4f)
        {
            throw new InvalidOperationException("The player's capsule has unsupported dimensions.");
        }

        _mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain", "blocker",
            "vehicle", "character", "character_net", "character_ghost");
        if (_mask == 0) { throw new InvalidOperationException("Collision layers were unavailable."); }
    }

    internal bool IsUnchanged()
    {
        if (_capsule == null || !_capsule.enabled || _capsule.direction != 1) { return false; }
        Vector3 scale = _capsule.transform.lossyScale;
        float radius = _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(2f * radius, _capsule.height * Mathf.Abs(scale.y));
        return Mathf.Abs(radius - Radius) < 0.001f && Mathf.Abs(height * 0.5f - radius - _halfSegment) < 0.001f;
    }

    internal bool HasRoom(Vector3 feet)
    {
        if (!LadderSegment.Finite(feet)) { return false; }
        CapsuleAt(feet, out Vector3 low, out Vector3 high);
        int found = Physics.OverlapCapsuleNonAlloc(low, high, Mathf.Max(0.02f, Radius - Skin), _overlaps, _mask,
            QueryTriggerInteraction.Ignore);
        if (found >= _overlaps.Length) { return false; }
        for (int index = 0; index < found; index++)
        {
            if (Solid(_overlaps[index])) { return false; }
        }

        return true;
    }

    internal bool CanMove(Vector3 from, Vector3 to)
    {
        if (!LadderSegment.Finite(from) || !LadderSegment.Finite(to) || !HasRoom(to)) { return false; }
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.0001f) { return true; }
        CapsuleAt(from, out Vector3 low, out Vector3 high);
        Vector3 direction = delta / distance;
        int found = Physics.CapsuleCastNonAlloc(low, high, Mathf.Max(0.02f, Radius - Skin), direction, _hits,
            distance, _mask, QueryTriggerInteraction.Ignore);
        if (found >= _hits.Length) { return false; }
        for (int index = 0; index < found; index++)
        {
            RaycastHit hit = _hits[index];
            if (!Solid(hit.collider)) { continue; }
            // Existing surface contact may be left or followed; moving into that surface is still refused.
            if (hit.distance <= Skin && Vector3.Dot(hit.normal, direction) >= -0.01f) { continue; }
            return false;
        }

        return true;
    }

    internal bool CanFollow(Vector3 from, Vector3[] points)
    {
        foreach (Vector3 point in points)
        {
            if (!CanMove(from, point)) { return false; }
            from = point;
        }

        return true;
    }

    internal bool Supported(Vector3 feet) => TrySurface(feet, 0.15f, 0.2f, out _);

    internal bool TryTopSurface(Vector3 desired, out Vector3 floor) => TrySurface(desired, 0.5f, 0.5f, out floor);
    internal bool TryNearbySurface(Vector3 desired, out Vector3 floor) => TrySurface(desired, 0.15f, 0.25f, out floor);

    private bool TrySurface(Vector3 desired, float above, float below, out Vector3 floor)
    {
        floor = default;
        if (!LadderSegment.Finite(desired)) { return false; }
        int found = Physics.RaycastNonAlloc(desired + Vector3.up * above, Vector3.down, _hits, above + below,
            _mask, QueryTriggerInteraction.Ignore);
        if (found >= _hits.Length) { return false; }
        float nearest = float.MaxValue;
        bool available = false;
        for (int index = 0; index < found; index++)
        {
            RaycastHit hit = _hits[index];
            if (!Solid(hit.collider) || hit.normal.y < 0.65f || hit.collider.GetComponentInParent<LadderSegment>() != null ||
                hit.collider.GetComponentInParent<Character>() != null)
            {
                continue;
            }

            float difference = Mathf.Abs(hit.point.y - desired.y);
            if (difference >= nearest) { continue; }
            nearest = difference;
            floor = new Vector3(desired.x, hit.point.y + Skin, desired.z);
            available = true;
        }

        return available;
    }

    private bool Solid(Collider collider) => collider != null && !collider.isTrigger &&
        !collider.transform.IsChildOf(_player.transform);

    private void CapsuleAt(Vector3 feet, out Vector3 low, out Vector3 high)
    {
        Vector3 centre = feet + _centreOffset;
        low = centre - Vector3.up * _halfSegment;
        high = centre + Vector3.up * _halfSegment;
    }
}
