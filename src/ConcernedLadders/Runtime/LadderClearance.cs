using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain;
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
    private readonly float _groundLift;
    private readonly HashSet<LadderSegment> _run = new HashSet<LadderSegment>();
    internal float Radius { get; }

    /// <summary>Capsule height and the centre's height above the feet, both scaled.</summary>
    internal float Height { get; }

    /// <summary>What the last refused query hit, and which collider. Diagnostic and message selection only;
    /// the refusal itself never depends on the classification.</summary>
    internal BlockerKind LastBlocker { get; private set; }
    internal string LastBlockerName { get; private set; } = string.Empty;

    /// <summary>Capsule bottom relative to the feet, before the query lift. Diagnostic only.</summary>
    internal float BottomGap { get; }

    /// <summary>The query capsule floats this far above the feet. A standing player's real capsule
    /// rests on the floor, and terrain contact is support, not an obstruction; a step or lip
    /// taller than this still blocks.</summary>
    private const float GroundClearance = 0.05f;

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
        Height = height;
        _halfSegment = Mathf.Max(0f, height * 0.5f - Radius);
        // Use the root-relative shape, not an interpolated Transform versus the physics position.
        _centreOffset = capsule.transform.TransformPoint(capsule.center) - player.transform.position;
        if (!LadderRulesFinite(Radius) || !LadderRulesFinite(height) || !LadderSegment.Finite(_centreOffset) ||
            Mathf.Abs(_centreOffset.x) > 0.05f || Mathf.Abs(_centreOffset.z) > 0.05f ||
            Radius < 0.05f || Radius > 1f || height > 4f)
        {
            throw new InvalidOperationException("The player's capsule has unsupported dimensions.");
        }

        BottomGap = _centreOffset.y - height * 0.5f;
        _groundLift = Mathf.Max(0f, GroundClearance - BottomGap);

        _mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain", "blocker",
            "vehicle", "character", "character_net", "character_ghost");
        if (_mask == 0) { throw new InvalidOperationException("Collision layers were unavailable."); }
    }

    /// <summary>The selected run's own sections, so a hit on them is reported as the ladder itself and
    /// never as "another building piece". They still collide.</summary>
    internal void SetRun(IEnumerable<LadderSegment> segments)
    {
        _run.Clear();
        foreach (LadderSegment segment in segments) { if (segment != null) { _run.Add(segment); } }
    }

    internal void ClearBlocker() { LastBlocker = BlockerKind.None; LastBlockerName = string.Empty; }

    internal bool IsUnchanged()
    {
        if (_capsule == null || !_capsule.enabled || _capsule.direction != 1) { return false; }
        Vector3 scale = _capsule.transform.lossyScale;
        float radius = _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(2f * radius, _capsule.height * Mathf.Abs(scale.y));
        Vector3 centreOffset = _capsule.transform.TransformPoint(_capsule.center) - _player.transform.position;
        return LadderSegment.Finite(scale) && LadderRulesFinite(radius) && LadderRulesFinite(height) &&
               LadderSegment.Finite(centreOffset) && Vector3.Distance(centreOffset, _centreOffset) < 0.001f &&
               Mathf.Abs(radius - Radius) < 0.001f && Mathf.Abs(height * 0.5f - radius - _halfSegment) < 0.001f;
    }

    internal bool HasRoom(Vector3 feet)
    {
        if (!LadderSegment.Finite(feet)) { return false; }
        CapsuleAt(feet, out Vector3 low, out Vector3 high);
        int found = Physics.OverlapCapsuleNonAlloc(low, high, Mathf.Max(0.02f, Radius - Skin), _overlaps, _mask,
            QueryTriggerInteraction.Ignore);
        if (found >= _overlaps.Length) { Blocked(BlockerKind.Overflow, "query overflow"); return false; }
        for (int index = 0; index < found; index++)
        {
            if (Solid(_overlaps[index])) { Classify(_overlaps[index]); return false; }
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
        if (found >= _hits.Length) { Blocked(BlockerKind.Overflow, "query overflow"); return false; }
        for (int index = 0; index < found; index++)
        {
            RaycastHit hit = _hits[index];
            if (!Solid(hit.collider)) { continue; }
            // Existing surface contact may be left or followed; moving into that surface is still refused.
            if (hit.distance <= Skin && Vector3.Dot(hit.normal, direction) >= -0.01f) { continue; }
            Classify(hit.collider);
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

    /// <summary>Whether a straight line reaches <paramref name="to"/> (a rung) without crossing anything but
    /// the selected run itself: a grab through a wall or floor is refused and the blocker recorded.</summary>
    internal bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (!LadderSegment.Finite(from) || !LadderSegment.Finite(to)) { Blocked(BlockerKind.Overflow, "not finite"); return false; }
        if (distance < 0.001f) { return true; }
        int found = Physics.RaycastNonAlloc(from, delta / distance, _hits, distance, _mask, QueryTriggerInteraction.Ignore);
        if (found >= _hits.Length) { Blocked(BlockerKind.Overflow, "query overflow"); return false; }
        for (int index = 0; index < found; index++)
        {
            Collider collider = _hits[index].collider;
            if (!Solid(collider) || OwnRun(collider)) { continue; }
            Classify(collider);
            return false;
        }

        return true;
    }

    internal bool Supported(Vector3 feet) => TrySurface(feet, 0.15f, 0.2f, out _);

    /// <summary>A landing's whole footprint: the centre and eight points around it at nine tenths of the
    /// capsule radius all find standing surface at the same height. A destination at a floor's edge, on a post
    /// or on a beam narrower than the body is not a landing (Codex review of 27528f0).</summary>
    internal bool SupportedFootprint(Vector3 feet) => SupportedFootprint(feet, out _);

    /// <summary>As <see cref="SupportedFootprint(Vector3)"/>, naming the first footprint point that failed.</summary>
    internal bool SupportedFootprint(Vector3 feet, out string why)
    {
        why = "";
        if (!TrySurface(feet, 0.15f, 0.2f, out Vector3 centre)) { why = "no surface under the centre"; return false; }
        float offset = Radius * 0.9f;
        foreach (Vector3 direction in Footprint)
        {
            if (!TrySurface(feet + direction * offset, 0.15f, 0.2f, out Vector3 floor)) { why = "no surface at " + direction.ToString("F1"); return false; }
            if (Mathf.Abs(floor.y - centre.y) > 0.15f) { why = "step " + (floor.y - centre.y).ToString("F2") + " at " + direction.ToString("F1"); return false; }
        }

        return true;
    }

    private static readonly Vector3[] Footprint =
    {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        new Vector3(0.7071f, 0f, 0.7071f), new Vector3(-0.7071f, 0f, 0.7071f),
        new Vector3(0.7071f, 0f, -0.7071f), new Vector3(-0.7071f, 0f, -0.7071f),
    };

    /// <summary>A floor near the run's top: up to a step above it (a ladder ending below a thick floor) or
    /// half a metre below it (a ladder standing proud of the floor).</summary>
    internal bool TryTopSurface(Vector3 desired, out Vector3 floor) => TrySurface(desired, 0.75f, 0.5f, out floor);
    /// <summary>A floor within a step of the climber's feet, above or below, for a mid-ladder dismount.</summary>
    internal bool TryNearbySurface(Vector3 desired, out Vector3 floor) => TrySurface(desired, 0.45f, 0.45f, out floor);

    /// <summary>A standing surface around <paramref name="desired"/>, from <paramref name="above"/> over it to
    /// <paramref name="below"/> under it (nearest in height wins), ladders and characters excluded.</summary>
    internal bool TryLandingSurface(Vector3 desired, float above, float below, out Vector3 floor) => TrySurface(desired, above, below, out floor);

    /// <summary>The standing surface under the feet within <paramref name="maxDrop"/>, ladders and characters excluded.</summary>
    internal bool TryFloorBelow(Vector3 feet, float maxDrop, out float floorY)
    {
        floorY = float.NaN;
        if (!TrySurface(feet, 0.15f, maxDrop, out Vector3 floor)) { return false; }
        floorY = floor.y;
        return true;
    }

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

    private bool OwnRun(Collider collider)
    {
        LadderSegment segment = collider.GetComponentInParent<LadderSegment>();
        return segment != null && _run.Contains(segment);
    }

    /// <summary>Only a hit whose collider belongs to a native Piece other than this run is a building.</summary>
    private void Classify(Collider collider)
    {
        if (OwnRun(collider)) { Blocked(BlockerKind.OwnLadder, collider.name); return; }
        Character actor = collider.GetComponentInParent<Character>();
        if (actor != null) { Blocked(BlockerKind.Actor, actor.m_name); return; }
        Piece piece = collider.GetComponentInParent<Piece>();
        if (piece != null) { Blocked(BlockerKind.Building, piece.m_name); return; }
        if (collider.GetComponent<Heightmap>() != null || collider.gameObject.layer == TerrainLayer)
        {
            Blocked(BlockerKind.Terrain, "terrain");
            return;
        }

        Blocked(BlockerKind.Other, collider.name);
    }

    private void Blocked(BlockerKind kind, string name)
    {
        LastBlocker = kind;
        string text = name ?? string.Empty;
        LastBlockerName = text.Length > 48 ? text.Substring(0, 48) : text;
    }

    private static readonly int TerrainLayer = LayerMask.NameToLayer("terrain");

    private bool Solid(Collider collider) => collider != null && !collider.isTrigger &&
        !collider.transform.IsChildOf(_player.transform);

    private static bool LadderRulesFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void CapsuleAt(Vector3 feet, out Vector3 low, out Vector3 high)
    {
        Vector3 centre = feet + _centreOffset + Vector3.up * _groundLift;
        low = centre - Vector3.up * _halfSegment;
        high = centre + Vector3.up * _halfSegment;
    }
}
