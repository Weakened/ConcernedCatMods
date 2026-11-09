using System;
using System.Collections.Generic;
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
/// angle, shrunk by a 1 cm skin so contact is not overlap, into a shared buffer; the lid stops at the last clear pose when anything but fixed world or the hatch
/// itself is in the way (<see cref="Occupied"/>). It turns by setting the transform, never by velocity or
/// <c>MoveRotation</c>, so it gives nothing a push; and since it only ever stops at a pose that was clear, it is never
/// placed into a character, a dynamic body or a piece it checked, beyond the 1 cm skin.</para>
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

    /// <summary>Every lid that has woken in this session and not been destroyed: placed pieces, and also the placement
    /// ghost and icon renders, which have no ZDO and are skipped by every query (slice 4). Empty for the whole session
    /// when <c>Trapdoors/Enabled</c> is off, because then no trapdoor prefab exists.</summary>
    private static readonly List<TrapdoorLid> Live = new List<TrapdoorLid>();

    private ZNetView? _view;
    private Transform? _hinge;
    private Transform? _hingePart;
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
        Live.Add(this);
        _view = GetComponent<ZNetView>();
        Transform? hinge = transform.Find(TrapdoorBlueprint.Hinge);
        _hingePart = hinge;
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
        // Shrunk by the skin on every side: touching is not overlapping (HatchMotion.QuerySkinMetres).
        Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
        float skin = HatchMotion.QuerySkinMetres;
        _boxHalfExtents = new Vector3(Mathf.Max(0.005f, half.x - skin), Mathf.Max(0.005f, half.y - skin), Mathf.Max(0.005f, half.z - skin));
        if (!(SwingSeconds >= HatchMotion.MinimumSwingSeconds && SwingSeconds <= HatchMotion.MaximumSwingSeconds))
        {
            SwingSeconds = DoorFamilies.LightSwing;
        }
    }

    /// <summary>Settles before the first physics step after the piece appears, where the ZDO is already readable, so
    /// a lid stored open is not briefly a closed collider. A ghost or an icon render has no ZDO and settles never.</summary>
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
            if (view == null || !view.IsValid() || _hinge == null) { return; }
            bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
            if (!HatchMotion.IsMoving(_progress, open)) { return; }
            _progress = HatchMotion.Step(_progress, open, Time.deltaTime, SwingSeconds, this);
            Apply();
        }
        catch (Exception exception)
        {
            Stop(exception);
        }
    }

    /// <summary>The first pose, once, from the stored native state (<see cref="HatchMotion.Settle{TProbe}"/>).</summary>
    private void TrySettle()
    {
        ZNetView? view = _view;
        if (view == null || !view.IsValid() || _hinge == null) { return; }
        bool open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
        _settled = true;
        _progress = HatchMotion.Settle(open, this);
        Apply();
    }

    /// <summary>A fault stops this lid where it is, logged once per session, rather than throwing every frame.</summary>
    private void Stop(Exception exception)
    {
        enabled = false;
        if (_faultLogged) { return; }
        _faultLogged = true;
        try { Log?.Invoke("Trapdoor lid stopped (" + SafeFailure.Brief(exception) + "); it stays where it is."); }
        catch (Exception) { }
    }

    private void OnDestroy() => Live.Remove(this);

    /// <summary>True while any trapdoor exists in this session (slice 4). The ladder adapters ask nothing of trapdoors
    /// while it is false, which it always is with <c>Trapdoors/Enabled</c> off, so they behave exactly as before.</summary>
    internal static bool Any => Live.Count > 0;

    /// <summary>The opening is a passage (<see cref="HatchMotion.IsPassage"/>): fully open and not asked to close. A lid
    /// that has not settled, has stopped on a fault, lacks its parts or has no ZDO is neither a passage nor a
    /// landing.</summary>
    internal bool Passage => TryReadOpen(out bool open) && HatchMotion.IsPassage(_progress, open);

    /// <summary>The lid is a floor (<see cref="HatchMotion.IsLanding"/>): closed and not asked to open.</summary>
    internal bool Landing => TryReadOpen(out bool open) && HatchMotion.IsLanding(_progress, open);

    /// <summary>The native door state now, not as of this lid's last update: a Use this frame already counts.</summary>
    private bool TryReadOpen(out bool open)
    {
        open = false;
        ZNetView? view = _view;
        if (!enabled || !_settled || view == null || !view.IsValid() || _hinge == null) { return false; }
        open = HatchMotion.IsOpenState(view.GetZDO().GetInt(ZDOVars.s_state));
        return true;
    }

    /// <summary>Would a climber's capsule of <paramref name="radius"/> at <paramref name="at"/>, occupying world heights
    /// <paramref name="low"/> to <paramref name="high"/> over a route, cross the opening of a placed trapdoor whose lid
    /// is not a passage (<see cref="HatchLadder.Crosses"/>, <see cref="HatchLadder.AllowsRoute"/>)? Ghosts and icon
    /// renders are skipped. No allocation; one pass over the live lids.</summary>
    internal static bool RouteShut(Vector3 at, float radius, float low, float high)
    {
        for (int index = 0; index < Live.Count; index++)
        {
            TrapdoorLid lid = Live[index];
            if (lid == null || lid._view == null || !lid._view.IsValid()) { continue; }
            Transform hatch = lid.transform;
            Vector3 local = hatch.InverseTransformPoint(at);
            float origin = hatch.position.y;
            bool crosses = HatchLadder.Crosses(local.x, local.z, radius, low - origin, high - origin);
            if (!HatchLadder.AllowsRoute(crosses, crosses && lid.Passage)) { return true; }
        }

        return false;
    }

    /// <summary>May the ladder's landing search stand a climber on this collider (<see cref="HatchLadder.IsLandingSurface"/>)?
    /// True for anything that is not part of a trapdoor.</summary>
    internal static bool MayLandOn(Collider collider)
    {
        TrapdoorLid? lid = collider != null ? collider.GetComponentInParent<TrapdoorLid>() : null;
        if (lid == null) { return true; }
        HatchPart part = lid._hingePart == null ? HatchPart.Unknown
            : collider!.transform.IsChildOf(lid._hingePart) ? HatchPart.Lid : HatchPart.Frame;
        return HatchLadder.IsLandingSurface(part, part == HatchPart.Lid && lid.Landing);
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
        return BlocksByKind(collider);
    }

    /// <summary>The blocking rule by what a collider is, once it is not the querying hatch's own: a character, cart,
    /// ship, item or building piece by its layer alone, anything else only as a dynamic body. Shared with the cellar
    /// door's leaves (<see cref="CellarLeaves"/>, CL-005 #486), so the two door runtimes block on the same things.</summary>
    internal static bool BlocksByKind(Collider collider)
    {
        if ((_blockingMask & (1 << collider.gameObject.layer)) != 0) { return true; }
        Rigidbody body = collider.attachedRigidbody;
        return body != null && !body.isKinematic;
    }

    /// <summary>The layers the hatch queries (<see cref="ResolveLayers"/>), shared with the cellar door's leaves. Zero
    /// until resolved.</summary>
    internal static int QueryMask => _queryMask;
}
