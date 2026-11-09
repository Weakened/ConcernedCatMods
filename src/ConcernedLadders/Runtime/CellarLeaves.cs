using System;
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
/// <para>Not a trapdoor: it is not in <see cref="TrapdoorLid"/>'s registry, so the ladder code still treats a cellar door
/// as an ordinary piece (the ladder integration is the next slice). Ghost and icon: no ZDO, so nothing is read and the
/// leaves stay closed. Idle cost per cellar door per frame is one ZDO integer read; nothing allocates per frame.</para></summary>
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

    private ZNetView? _view;
    private Pivot? _left;
    private Pivot? _right;
    private Collider[]? _own;
    private CellarPairOccupancy<CellarLeaves> _pair;
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

    private void Awake()
    {
        _view = GetComponent<ZNetView>();
        Transform? slope = transform.Find(CellarBlueprint.SlopeRoot);
        Transform? leftHinge = slope != null ? slope.Find(CellarBlueprint.Hinge(CellarLeaf.Left)) : null;
        Transform? rightHinge = slope != null ? slope.Find(CellarBlueprint.Hinge(CellarLeaf.Right)) : null;
        BoxCollider? leftBox = LeafBox(leftHinge, CellarLeaf.Left);
        BoxCollider? rightBox = LeafBox(rightHinge, CellarLeaf.Right);
        Collider[]? own = OwnColliders();
        if (_view == null || leftHinge == null || rightHinge == null || leftBox == null || rightBox == null || own == null ||
            !TrapdoorLid.ResolveLayers())
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
        if (!(SwingSeconds >= HatchMotion.MinimumSwingSeconds && SwingSeconds <= HatchMotion.MaximumSwingSeconds))
        {
            SwingSeconds = DoorFamilies.LightSwing;
        }
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

    /// <summary>The first pose, once, from the stored native state (<see cref="HatchMotion.Settle{TProbe}"/>), for the
    /// pair.</summary>
    private void TrySettle()
    {
        ZNetView? view = _view;
        if (view == null || !view.IsValid() || _left == null) { return; }
        bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
        _settled = true;
        _progress = HatchMotion.Settle(open, _pair);
        Apply();
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
        Pivot part = leaf == CellarLeaf.Left ? _left! : _right!;
        Quaternion turn = Quaternion.Euler(0f, 0f, part.Sign * angleDegrees);
        Vector3 centre = part.Space.TransformPoint(part.Local + turn * part.BoxCentre);
        Quaternion rotation = part.Space.rotation * turn * part.BoxRotation;
        int found = Physics.OverlapBoxNonAlloc(centre, part.HalfExtents, Overlaps, rotation, TrapdoorLid.QueryMask, QueryTriggerInteraction.Ignore);
        bool occupied = found >= Overlaps.Length;
        for (int index = 0; index < found && !occupied; index++) { occupied = Blocks(Overlaps[index]); }
        Array.Clear(Overlaps, 0, Math.Min(found, Overlaps.Length));
        return occupied;
    }

    private bool Blocks(Collider collider)
    {
        if (collider == null || collider.isTrigger || IsOwn(collider)) { return false; }
        return TrapdoorLid.BlocksByKind(collider);
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
