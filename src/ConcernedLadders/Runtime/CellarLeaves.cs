using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>The cellar door's two leaves (CL-005 #486, slice 3): the trapdoor lid's runtime (<see cref="TrapdoorLid"/>)
/// generalised to two hinge pivots driven from one progress. It follows the piece's own native door state and turns the
/// two pivots <c>slope/leaf_left_hinge</c> and <c>slope/leaf_right_hinge</c> (<see cref="CellarBlueprint.Hinge"/>), each of
/// which carries its leaf's kinematic body, collider and visuals, so motion and collision cannot disagree.
///
/// <para>State: read only, from the native <c>Door</c>'s own ZDO value (<c>ZDOVars.s_state</c>) through the piece's own
/// <c>ZNetView</c>, exactly as the lid reads it. This component writes nothing to the network: no RPC, no ZDO field, no
/// ownership claim. Every client moves its own copy of the leaves from that one shared value.</para>
///
/// <para>Motion: <see cref="HatchMotion"/> unchanged, over the donor's own swing time, through
/// <see cref="CellarPairOccupancy{TLeaves}"/>: the pair's pose is occupied when either leaf's is, so a leaf that meets
/// something holds both, and the two leaves are always at the same angle. Each checked pose asks at most one
/// <c>Physics.OverlapBoxNonAlloc</c> per leaf, of that leaf's own collider box at that angle shrunk by the lid's 1 cm skin
/// (<see cref="HatchMotion.QuerySkinMetres"/>), with the lid's layers and blocking rule
/// (<see cref="TrapdoorLid.BlocksByKind"/>). Each leaf's query ignores exactly the piece's own six colliders, by collider
/// identity (<see cref="CellarBlueprint.QueryIgnores"/>): the four frame prisms, the other leaf and itself. In the model
/// a leaf stays 1 cm or more from the frame and 5.7 mm or more from the other leaf at every angle, and the skin shrinks
/// each query a further 1 cm, so no own collider should ever be inside a query; ignoring them by identity means the
/// cover does not depend on margins that thin surviving PhysX's convex-mesh cooking and float error. Unlike the lid's
/// hierarchy test, nothing else is ignored: anything parented under the piece by anyone else is checked. The pivots turn
/// by setting their transforms, never by velocity or <c>MoveRotation</c>, so they push nothing.</para>
///
/// <para>First pose: <see cref="CellarSettle.First{TAll, TMovers}"/>. Closed, whatever is stored, when something is in
/// the closed pose but no character, item, cart, ship or dynamic body is: a fixed wall there holds the cover shut rather
/// than loading it open. Otherwise the trapdoor's <see cref="HatchMotion.Settle{TProbe}"/>: the stored end when clear,
/// and never a pose inside someone.</para>
///
/// <para>Ladders (slice 4): every woken cover is in its own registry (<see cref="Any"/>), not the trapdoor lid's, and
/// answers the ladder code's three questions from the one progress and the native state, as the lid does:
/// <see cref="RouteShut"/> (a route through the opening needs the pair to be a passage), <see cref="MayLandOn"/> (a leaf
/// is a landing only closed and staying closed) and <see cref="OverPanel"/> (how high a route over the frame rises), by
/// <see cref="CellarLadder"/>'s rules. With <c>CellarDoors/Enabled</c> off no cover exists and nothing asks. Ghost and
/// icon: no ZDO, so nothing is read, the leaves stay closed and no ladder question counts them. Idle cost per cellar
/// door per frame is one ZDO integer read; nothing allocates per frame.</para></summary>
internal sealed class CellarLeaves : MonoBehaviour, ICellarLeafOccupancy
{
    /// <summary>The donor's swing time, seconds. A serialized field, so it is copied from the inactive prefab clone to
    /// every instance, as the lid's is.</summary>
    public float SwingSeconds = DoorFamilies.LightSwing;

    /// <summary>The adapter's log, for the one-off fault message. Never called per frame.</summary>
    internal static Action<string>? Log;

    /// <summary>Shared by every cellar door: queries run one at a time on the main thread. A full buffer counts as
    /// occupied.</summary>
    private static readonly Collider[] Overlaps = new Collider[32];

    private static bool _faultLogged;

    /// <summary>The layers of things that can always move, resolved once: characters (the local player, other players,
    /// creatures, ghosts and their hitboxes), dropped items, and carts and ships (<c>vehicle</c>). With dynamic bodies,
    /// these are what the first pose must never put the closed leaves into (<see cref="CellarSettle"/>). Items, carts and
    /// ships count by layer, not only by body, because a client that does not own one holds its body kinematic until
    /// ownership passes, when it becomes dynamic inside whatever overlaps it. Zero, and every cover stays shut, if any is
    /// missing.</summary>
    private static int _moverMask;

    private static readonly string[] MoverLayers = { "character", "character_net", "character_ghost", "character_noenv", "hitbox", "item", "vehicle" };

    /// <summary>Every cover that has woken in this session and not been destroyed: placed pieces, and also the placement
    /// ghost and icon renders, which have no ZDO and are skipped by every ladder question (slice 4). Empty for the whole
    /// session when <c>CellarDoors/Enabled</c> is off, because then no cellar door prefab exists.</summary>
    private static readonly List<CellarLeaves> Live = new List<CellarLeaves>();

    private ZNetView? _view;
    private Pivot? _left;
    private Pivot? _right;
    private Transform? _leftPart;
    private Transform? _rightPart;
    private Collider[]? _own;
    private CellarPairOccupancy<CellarLeaves> _pair;
    private CellarPairOccupancy<Movers> _movers;
    private float _progress;
    private float _appliedAngle = float.NaN;
    private bool _settled;

    /// <summary>One leaf's hinge pivot and its collider box, measured once in Awake.</summary>
    private sealed class Pivot
    {
        internal Pivot(Transform hinge, BoxCollider box, float sign)
        {
            Hinge = hinge;
            Space = hinge.parent;
            Local = hinge.localPosition;
            Sign = sign;
            BoxCentre = hinge.InverseTransformPoint(box.transform.TransformPoint(box.center));
            BoxRotation = Quaternion.Inverse(hinge.rotation) * box.transform.rotation;
            // Shrunk by the skin on every side: touching is not overlapping (HatchMotion.QuerySkinMetres).
            Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
            float skin = HatchMotion.QuerySkinMetres;
            HalfExtents = new Vector3(Mathf.Max(0.005f, half.x - skin), Mathf.Max(0.005f, half.y - skin), Mathf.Max(0.005f, half.z - skin));
        }

        internal Transform Hinge { get; }

        /// <summary>The slope transform the pivot hangs in.</summary>
        internal Transform Space { get; }

        internal Vector3 Local { get; }

        /// <summary><see cref="CellarGeometry.HingeRotationSign"/>: -1 for the right leaf, +1 for the left.</summary>
        internal float Sign { get; }

        internal Vector3 BoxCentre { get; }

        internal Quaternion BoxRotation { get; }

        internal Vector3 HalfExtents { get; }
    }

    /// <summary>The first pose's second question (<see cref="CellarSettle"/>): would this leaf's pose overlap a
    /// character, an item, a cart, a ship or a dynamic body? The same query as <see cref="Occupied"/>, counting nothing
    /// else.</summary>
    private readonly struct Movers : ICellarLeafOccupancy
    {
        private readonly CellarLeaves _leaves;

        internal Movers(CellarLeaves leaves) => _leaves = leaves;

        public bool Occupied(CellarLeaf leaf, float angleDegrees) => _leaves.Query(leaf, angleDegrees, true);
    }

    private void Awake()
    {
        Live.Add(this);
        _view = GetComponent<ZNetView>();
        Transform? slope = transform.Find(CellarBlueprint.SlopeRoot);
        Transform? leftHinge = slope != null ? slope.Find(CellarBlueprint.Hinge(CellarLeaf.Left)) : null;
        Transform? rightHinge = slope != null ? slope.Find(CellarBlueprint.Hinge(CellarLeaf.Right)) : null;
        _leftPart = leftHinge;
        _rightPart = rightHinge;
        BoxCollider? leftBox = LeafBox(leftHinge, CellarLeaf.Left);
        BoxCollider? rightBox = LeafBox(rightHinge, CellarLeaf.Right);
        Collider[]? own = OwnColliders();
        if (_view == null || leftHinge == null || rightHinge == null || leftBox == null || rightBox == null || own == null ||
            !TrapdoorLid.ResolveLayers() || !ResolveMoverLayers())
        {
            // A cellar door without its parts or the game's layers cannot check its sweep: the leaves stay where the
            // prefab put them, closed. (The ghost and the icon render keep their parts but never get a ZDO, so Update
            // returns before reading anything.)
            enabled = false;
            return;
        }

        leftHinge.localRotation = Quaternion.identity;
        rightHinge.localRotation = Quaternion.identity;
        _left = new Pivot(leftHinge, leftBox, CellarGeometry.HingeRotationSign(CellarLeaf.Left));
        _right = new Pivot(rightHinge, rightBox, CellarGeometry.HingeRotationSign(CellarLeaf.Right));
        _own = own;
        _pair = new CellarPairOccupancy<CellarLeaves>(this);
        _movers = new CellarPairOccupancy<Movers>(new Movers(this));
        if (!(SwingSeconds >= HatchMotion.MinimumSwingSeconds && SwingSeconds <= HatchMotion.MaximumSwingSeconds))
        {
            SwingSeconds = DoorFamilies.LightSwing;
        }
    }

    private static bool ResolveMoverLayers()
    {
        if (_moverMask != 0) { return true; }
        int mask = 0;
        foreach (string name in MoverLayers)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0) { return false; }
            mask |= 1 << layer;
        }

        _moverMask = mask;
        return true;
    }

    private static BoxCollider? LeafBox(Transform? hinge, CellarLeaf leaf)
    {
        Transform? holder = hinge != null ? hinge.Find(CellarBlueprint.LeafCollider(leaf).Name) : null;
        return holder != null ? holder.GetComponent<BoxCollider>() : null;
    }

    /// <summary>The piece's own colliders, by identity, from <see cref="CellarBlueprint.QueryIgnores"/>; null when any
    /// is missing, which leaves the cover shut.</summary>
    private Collider[]? OwnColliders()
    {
        var own = new Collider[CellarBlueprint.QueryIgnores.Count];
        for (int index = 0; index < own.Length; ++index)
        {
            Transform? part = transform.Find(CellarBlueprint.QueryIgnores[index]);
            Collider? collider = part != null ? part.GetComponent<Collider>() : null;
            if (collider == null) { return null; }
            own[index] = collider;
        }

        return own;
    }

    /// <summary>Settles before the first physics step after the piece appears, where the ZDO is already readable, so
    /// a cover stored open is not briefly closed colliders. A ghost or an icon render has no ZDO and settles never.</summary>
    private void Start()
    {
        try { TrySettle(); }
        catch (Exception exception) { Stop(exception); }
    }

    private void Update()
    {
        try
        {
            if (!_settled) { TrySettle(); return; }
            ZNetView? view = _view;
            if (view == null || !view.IsValid() || _left == null) { return; }
            bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
            if (!HatchMotion.IsMoving(_progress, open)) { return; }
            _progress = HatchMotion.Step(_progress, open, Time.deltaTime, SwingSeconds, _pair);
            Apply();
        }
        catch (Exception exception)
        {
            Stop(exception);
        }
    }

    /// <summary>The first pose, once, from the stored native state, for the pair: <see cref="CellarSettle.First{TAll, TMovers}"/>, which
    /// is fail-closed against fixed obstructions and otherwise <see cref="HatchMotion.Settle{TProbe}"/>.</summary>
    private void TrySettle()
    {
        ZNetView? view = _view;
        if (view == null || !view.IsValid() || _left == null) { return; }
        bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
        _settled = true;
        _progress = CellarSettle.First(open, _pair, _movers);
        Apply();
    }

    private void OnDestroy() => Live.Remove(this);

    /// <summary>True while any cellar door exists in this session (slice 4). The ladder adapters ask nothing of cellar
    /// doors while it is false, which it always is with <c>CellarDoors/Enabled</c> off, so they behave exactly as
    /// before.</summary>
    internal static bool Any => Live.Count > 0;

    /// <summary>The opening is a passage (<see cref="HatchMotion.IsPassage"/>): both leaves fully open and not asked to
    /// close. A cover that has not settled, has stopped on a fault, lacks its parts or has no ZDO is neither a passage nor
    /// a landing.</summary>
    internal bool Passage => TryReadOpen(out bool open) && HatchMotion.IsPassage(_progress, open);

    /// <summary>The leaves are a floor (<see cref="HatchMotion.IsLanding"/>): closed and not asked to open.</summary>
    internal bool Landing => TryReadOpen(out bool open) && HatchMotion.IsLanding(_progress, open);

    /// <summary>The native door state now, not as of this cover's last update: a Use this frame already counts.</summary>
    private bool TryReadOpen(out bool open)
    {
        open = false;
        ZNetView? view = _view;
        if (!enabled || !_settled || view == null || !view.IsValid() || _left == null) { return false; }
        open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
        return true;
    }

    /// <summary>Would a climber's capsule of <paramref name="radius"/> at <paramref name="at"/>, occupying world heights
    /// <paramref name="low"/> to <paramref name="high"/> over one leg of a route, cross the opening of a placed cellar door
    /// whose pair is not a passage (<see cref="CellarLadder.Crosses"/>, <see cref="HatchLadder.AllowsRoute"/>)? Ghosts and
    /// icon renders are skipped. No allocation; one pass over the live covers.</summary>
    internal static bool RouteShut(Vector3 at, float radius, float low, float high)
    {
        for (int index = 0; index < Live.Count; index++)
        {
            CellarLeaves cover = Live[index];
            if (cover == null || cover._view == null || !cover._view.IsValid()) { continue; }
            Transform piece = cover.transform;
            Vector3 local = piece.InverseTransformPoint(at);
            float origin = piece.position.y;
            bool crosses = CellarLadder.Crosses(local.x, local.z, radius, low - origin, high - origin);
            if (!HatchLadder.AllowsRoute(crosses, crosses && cover.Passage)) { return true; }
        }

        return false;
    }

    /// <summary>The world height a route between <paramref name="inside"/> and <paramref name="outside"/> must carry the
    /// feet to, when it passes over a placed cellar door's frame (<see cref="CellarLadder.LeavesOverFrame"/>): that
    /// cover's <see cref="CellarLadder.OverPanelY"/>, the highest of any such cover. Negative infinity when the route
    /// passes over none, so the standard lifts stand. Ghosts and icon renders are skipped. No allocation.</summary>
    internal static float OverPanel(Vector3 inside, Vector3 outside)
    {
        float best = float.NegativeInfinity;
        for (int index = 0; index < Live.Count; index++)
        {
            CellarLeaves cover = Live[index];
            if (cover == null || cover._view == null || !cover._view.IsValid()) { continue; }
            Transform piece = cover.transform;
            Vector3 from = piece.InverseTransformPoint(inside);
            Vector3 to = piece.InverseTransformPoint(outside);
            float origin = piece.position.y;
            if (!CellarLadder.LeavesOverFrame(from.x, inside.y - origin, from.z, to.x, outside.y - origin, to.z)) { continue; }
            best = Mathf.Max(best, origin + CellarLadder.OverPanelY);
        }

        return best;
    }

    /// <summary>May the ladder's landing search stand a climber on this collider (<see cref="HatchLadder.IsLandingSurface"/>)?
    /// True for anything that is not part of a cellar door, and for its fixed frame. Anything under either hinge pivot (a
    /// leaf) only while the pair is a landing; any part of a cover whose pivots could not be found, never. This is the
    /// only hierarchy lookup in this type, and only the ladder code calls it: the leaves' own occupancy queries compare
    /// colliders by identity.</summary>
    internal static bool MayLandOn(Collider collider)
    {
        CellarLeaves? cover = collider != null ? collider.GetComponentInParent<CellarLeaves>() : null;
        if (cover == null) { return true; }
        HatchPart part = cover._leftPart == null || cover._rightPart == null ? HatchPart.Unknown
            : collider!.transform.IsChildOf(cover._leftPart) || collider.transform.IsChildOf(cover._rightPart) ? HatchPart.Lid
            : HatchPart.Frame;
        return HatchLadder.IsLandingSurface(part, part == HatchPart.Lid && cover.Landing);
    }

    /// <summary>A fault stops this cover where it is, both leaves at the one angle, logged once per session.</summary>
    private void Stop(Exception exception)
    {
        enabled = false;
        if (_faultLogged) { return; }
        _faultLogged = true;
        try { Log?.Invoke("Cellar door leaves stopped (" + SafeFailure.Brief(exception) + "); they stay where they are."); }
        catch (Exception) { }
    }

    /// <summary>Both pivots from the one progress, mirror images: the right leaf about its local z by minus the angle,
    /// the left by plus it (<see cref="CellarGeometry.HingeRotationSign"/>).</summary>
    private void Apply()
    {
        float angle = HatchMotion.Angle(_progress);
        if (angle == _appliedAngle) { return; }
        _appliedAngle = angle;
        _left!.Hinge.localRotation = Quaternion.Euler(0f, 0f, _left.Sign * angle);
        _right!.Hinge.localRotation = Quaternion.Euler(0f, 0f, _right.Sign * angle);
    }

    /// <summary>Would this leaf's box at this angle overlap a character, a dynamic body or a building piece that is not
    /// one of this piece's own six colliders? One non-allocating box overlap with triggers ignored; a full buffer is
    /// occupied.</summary>
    public bool Occupied(CellarLeaf leaf, float angleDegrees)
    {
        return Query(leaf, angleDegrees, false);
    }

    /// <summary>The one query. With <paramref name="moversOnly"/>, only a mover (<see cref="Moves"/>) counts; a full
    /// buffer is occupied either way.</summary>
    private bool Query(CellarLeaf leaf, float angleDegrees, bool moversOnly)
    {
        Pivot part = leaf == CellarLeaf.Left ? _left! : _right!;
        Quaternion turn = Quaternion.Euler(0f, 0f, part.Sign * angleDegrees);
        Vector3 centre = part.Space.TransformPoint(part.Local + turn * part.BoxCentre);
        Quaternion rotation = part.Space.rotation * turn * part.BoxRotation;
        int found = Physics.OverlapBoxNonAlloc(centre, part.HalfExtents, Overlaps, rotation, TrapdoorLid.QueryMask, QueryTriggerInteraction.Ignore);
        bool occupied = found >= Overlaps.Length;
        for (int index = 0; index < found && !occupied; index++) { occupied = moversOnly ? Moves(Overlaps[index]) : Blocks(Overlaps[index]); }
        Array.Clear(Overlaps, 0, Math.Min(found, Overlaps.Length));
        return occupied;
    }

    private bool Blocks(Collider collider)
    {
        if (collider == null || collider.isTrigger || IsOwn(collider)) { return false; }
        return TrapdoorLid.BlocksByKind(collider);
    }

    /// <summary>A character, an item, a cart or a ship, by its layer, or a dynamic body: something the closed leaves
    /// could push, now or once its owner changes.</summary>
    private bool Moves(Collider collider)
    {
        if (collider == null || collider.isTrigger || IsOwn(collider)) { return false; }
        if ((_moverMask & (1 << collider.gameObject.layer)) != 0) { return true; }
        Rigidbody body = collider.attachedRigidbody;
        return body != null && !body.isKinematic;
    }

    /// <summary>One of this piece's own six colliders, by identity (Unity's equality: the same native object), never by
    /// name, layer or parent.</summary>
    private bool IsOwn(Collider collider)
    {
        Collider[] own = _own!;
        for (int index = 0; index < own.Length; index++)
        {
            if (own[index] == collider) { return true; }
        }

        return false;
    }
}
