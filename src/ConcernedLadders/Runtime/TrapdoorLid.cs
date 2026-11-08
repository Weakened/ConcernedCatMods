using System;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Runtime;

/// <summary>The trapdoor's lid (CL-004 #485, slice 3). It follows the piece's own native door state and turns the one
/// <see cref="TrapdoorBlueprint.Hinge"/> transform, which carries the lid's kinematic body, its collider and its
/// visuals, so motion and collision cannot disagree.
///
/// <para>State: read only, from the native <c>Door</c>'s own ZDO value (<c>ZDOVars.s_state</c>, the integer
/// <c>Door.RPC_UseDoor</c> writes and <c>Door.UpdateState</c> copies into the hidden animator) through the piece's own
/// <c>ZNetView</c>. This component writes nothing to the network: no RPC, no ZDO field, no ownership claim. The native
/// door's replication, persistence, late-join and ward behaviour are therefore the lid's too. Every client moves its own
/// copy of the lid from that shared value.</para>
///
/// <para>Motion: <see cref="HatchMotion.Step{TProbe}"/> over the donor's own swing time
/// (<see cref="SwingSeconds"/>, set by the piece adapter from <c>DoorFamily.SwingSeconds</c>), in checked poses at
/// most 5 degrees apart. Each check is one <c>Physics.OverlapBoxNonAlloc</c> of the lid collider's own box at that
/// angle into a shared buffer; the lid stops at the last clear pose when anything but fixed world or the hatch
/// itself is in the way (<see cref="Occupied"/>). It turns by setting the transform, never by velocity or
/// <c>MoveRotation</c>, so it gives nothing a push; and since it only ever stops at a pose that was clear, it is never
/// placed into a character, a dynamic body or a piece it checked.</para>
///
/// <para>Ghost and icon: the placement ghost and the icon render are instances with no ZDO, so nothing is read and
/// the lid stays closed. Idle cost per hatch per frame is one ZDO integer read; nothing allocates per frame.</para></summary>
internal sealed class TrapdoorLid : MonoBehaviour, IHatchOccupancy
{
    /// <summary>The donor's swing time, seconds. A serialized field, so it is copied from the inactive prefab clone to
    /// every instance (the ladders' <c>LadderSegment</c> fields rely on the same copy).</summary>
    public float SwingSeconds = DoorFamilies.LightSwing;

    /// <summary>The adapter's log, for the one-off messages below. Never called per frame.</summary>
    internal static Action<string>? Log;

    /// <summary>Shared by every lid: queries run one at a time on the main thread. A full buffer counts as occupied.</summary>
    private static readonly Collider[] Overlaps = new Collider[32];

    private static bool _layersResolved;
    private static int _queryMask;
    private static int _blockingMask;
    private static bool _faultLogged;

    private ZNetView? _view;
    private Transform? _hinge;
    private Vector3 _hingeLocal;
    private Vector3 _boxCentreInHinge;
    private Quaternion _boxRotationInHinge = Quaternion.identity;
    private Vector3 _boxHalfExtents;
    private float _progress;
    private float _appliedAngle = float.NaN;
    private bool _settled;

    /// <summary>The query and blocking layers, resolved once from the game's own layer names (each name is one the
    /// installed game's own <c>LayerMask.GetMask</c> calls use). False, and every lid stays shut, if any is missing.
    /// Characters (the local player, other players, creatures, ghosts and their hitboxes), carts and ships
    /// (<c>vehicle</c>), dropped items and every building piece block on their layer alone; on the general layers
    /// only a dynamic (non-kinematic) body blocks, so a resting log or ragdoll does and fixed world does not.
    /// Terrain and static rock are not queried: the lid may stand into them, as a native door leaf does.</summary>
    internal static bool ResolveLayers()
    {
        if (_layersResolved) { return true; }
        int blocking = 0, general = 0;
        foreach (string name in BlockingLayers)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0) { return false; }
            blocking |= 1 << layer;
        }

        foreach (string name in GeneralLayers)
        {
            int layer = LayerMask.NameToLayer(name);
            if (layer < 0) { return false; }
            general |= 1 << layer;
        }

        _blockingMask = blocking;
        _queryMask = blocking | general;
        _layersResolved = true;
        return true;
    }

    private static readonly string[] BlockingLayers =
    {
        "character", "character_net", "character_ghost", "character_noenv", "hitbox", "vehicle", "item", "piece", "piece_nonsolid",
    };

    private static readonly string[] GeneralLayers = { "Default", "Default_small" };

    private void Awake()
    {
        _view = GetComponent<ZNetView>();
        Transform? hinge = transform.Find(TrapdoorBlueprint.Hinge);
        Transform? holder = hinge != null ? hinge.Find(TrapdoorBlueprint.LidCollider.Name) : null;
        BoxCollider? box = holder != null ? holder.GetComponent<BoxCollider>() : null;
        if (_view == null || hinge == null || box == null || !ResolveLayers())
        {
            // A hatch without its parts or the game's layers cannot check its sweep: the lid stays where the prefab
            // put it, closed. (The placement ghost and the icon render keep their parts but never get a ZDO, so
            // Update returns before reading anything; that check is not made here because it would depend on the
            // order in which Unity wakes this component and the ZNetView.)
            enabled = false;
            return;
        }

        _hinge = hinge;
        _hingeLocal = hinge.localPosition;
        hinge.localRotation = Quaternion.identity;
        _boxCentreInHinge = hinge.InverseTransformPoint(box.transform.TransformPoint(box.center));
        _boxRotationInHinge = Quaternion.Inverse(hinge.rotation) * box.transform.rotation;
        _boxHalfExtents = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
        if (!(SwingSeconds >= HatchMotion.MinimumSwingSeconds && SwingSeconds <= HatchMotion.MaximumSwingSeconds))
        {
            SwingSeconds = DoorFamilies.LightSwing;
        }
    }

    private void Update()
    {
        try
        {
            ZNetView? view = _view;
            if (view == null || !view.IsValid() || _hinge == null) { return; }
            bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
            if (!_settled)
            {
                _settled = true;
                _progress = HatchMotion.Settle(open, this);
                Apply();
                return;
            }

            if (!HatchMotion.IsMoving(_progress, open)) { return; }
            _progress = HatchMotion.Step(_progress, open, Time.deltaTime, SwingSeconds, this);
            Apply();
        }
        catch (Exception exception)
        {
            // A fault stops this lid where it is, once, rather than throwing every frame.
            enabled = false;
            if (_faultLogged) { return; }
            _faultLogged = true;
            try { Log?.Invoke("Trapdoor lid stopped (" + SafeFailure.Brief(exception) + "); it stays where it is."); }
            catch (Exception) { }
        }
    }

    private void Apply()
    {
        float angle = HatchMotion.Angle(_progress);
        if (angle == _appliedAngle) { return; }
        _appliedAngle = angle;
        _hinge!.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }

    /// <summary>Would the lid's box at this hinge angle overlap a character, a dynamic body or a building piece other
    /// than this hatch? One non-allocating box overlap with triggers ignored; a full buffer is occupied.</summary>
    public bool Occupied(float angleDegrees)
    {
        Quaternion turn = Quaternion.Euler(angleDegrees, 0f, 0f);
        Vector3 centre = transform.TransformPoint(_hingeLocal + turn * _boxCentreInHinge);
        Quaternion rotation = transform.rotation * turn * _boxRotationInHinge;
        int found = Physics.OverlapBoxNonAlloc(centre, _boxHalfExtents, Overlaps, rotation, _queryMask, QueryTriggerInteraction.Ignore);
        bool occupied = found >= Overlaps.Length;
        for (int index = 0; index < found && !occupied; index++) { occupied = Blocks(Overlaps[index]); }
        Array.Clear(Overlaps, 0, Math.Min(found, Overlaps.Length));
        return occupied;
    }

    private bool Blocks(Collider collider)
    {
        if (collider == null || collider.isTrigger || collider.transform.IsChildOf(transform)) { return false; }
        if ((_blockingMask & (1 << collider.gameObject.layer)) != 0) { return true; }
        Rigidbody body = collider.attachedRigidbody;
        return body != null && !body.isKinematic;
    }
}
