using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using TheConcernedCat.ConcernedTeamster.Domain.Capabilities;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The attach seam over the cart (DECISIONS.md D4, CONTRACTS.md §2.5):
/// the cart's own private <c>AttachTo(GameObject)</c> and <c>Detach()</c>,
/// called directly through Teamster's publicized reference. Never the cart's
/// interaction, its ownership request or an owner change.
///
/// <b>Probe.</b> Every member this seam and Gunnar's body use is verified once
/// at startup; anything missing makes hauling unavailable
/// (<see cref="HitchRefusal.SeamUnavailable"/>) with one log line, and telemetry
/// and the brake are unaffected. Any exception while reading latches the seam
/// unavailable the same way.
///
/// <b>Attach.</b> Two last-instant guards run right before the call, in the same
/// frame as the executor's D4 evaluation: this client owns the cart (a non-owner
/// call would write a replicated flag it has no authority over) and no cart on
/// this client holds a joint (the attach detaches every loaded cart first). The
/// joint is then verified: connected to Gunnar's rigidbody, the cart reports
/// itself attached, the replicated attach flag is set, and Gunnar weighs his
/// calibrated base plus the cart's player pull mass. Any failure detaches again.
///
/// <b>Release</b> calls the cart's detach only when the joint is Gunnar's or no
/// joint exists (which clears a stale flag exactly as vanilla's next update
/// would), never when another body holds it.</summary>
internal sealed class VagonHitchSeam : ICartHitchSeam
{
    private const float GroundProbeHeightMetres = 1f;
    private const float GroundProbeDepthMetres = 4f;
    private const float AlongOffsetMetres = 1.5f;
    private const float AcrossOffsetMetres = 1f;

    private readonly TeamsterWorkerBody _body;
    private readonly Func<string?> _engagedBrakeCartId;
    private readonly HaulExecutionLimits _execution;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly int _groundMask;

    private GameCapabilityReport _probe;
    private string _faultDetail = string.Empty;
    private CartKey _cachedKey;
    private Vagon? _cachedCart;
    private ZDOID _cachedId;
    private bool _footprintMeasured;
    private float _footprintWidth;
    private float _footprintLength;
    private float _hitchLength;

    public VagonHitchSeam(TeamsterWorkerBody body, Func<string?> engagedBrakeCartId, HaulExecutionLimits execution)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _engagedBrakeCartId = engagedBrakeCartId ?? throw new ArgumentNullException(nameof(engagedBrakeCartId));
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        _probe = HaulingCapabilityProbe.Run();
    }

    public GameCapabilityReport Probe => _probe;

    public bool IsAvailable => _probe.Enabled && _faultDetail.Length == 0;

    public string UnavailableDetail =>
        !_probe.Enabled ? "missing " + string.Join(", ", _probe.MissingMembers) :
        _faultDetail.Length > 0 ? _faultDetail : string.Empty;

    /// <summary>Remembers the cart the player selected, so it resolves by
    /// reference and its record can be told apart from a destroyed one.
    /// </summary>
    internal void Remember(CartKey key, Vagon cart)
    {
        if (!_cachedKey.Equals(key))
        {
            _footprintMeasured = false;
        }

        _cachedKey = key;
        _cachedCart = cart;
        ZNetView view = cart.m_nview;
        if (view != null && view.IsValid())
        {
            _cachedId = view.GetZDO().m_uid;
        }
    }

    internal void Forget()
    {
        _cachedKey = default;
        _cachedCart = null;
        _cachedId = ZDOID.None;
        _footprintMeasured = false;
    }

    /// <summary>The cart the lease names, or null when it no longer resolves in
    /// this world load.</summary>
    internal Vagon? Resolve(CartKey key)
    {
        if (!IsAvailable || key.IsEmpty)
        {
            return null;
        }

        try
        {
            return ResolveCore(key);
        }
        catch (Exception exception)
        {
            Latch(exception);
            return null;
        }
    }

    public CartObservation Observe(CartKey cart)
    {
        var observation = new CartObservation { CapabilityOk = IsAvailable };
        if (!IsAvailable)
        {
            return observation;
        }

        try
        {
            ObserveCore(cart, ref observation);
        }
        catch (Exception exception)
        {
            Latch(exception);
            observation = new CartObservation { CapabilityOk = false, Resolved = false, RecordExists = true };
        }

        return observation;
    }

    public ParkingGround ReadGround(CartKey cart)
    {
        if (!IsAvailable)
        {
            return default;
        }

        try
        {
            return ReadGroundCore(cart);
        }
        catch (Exception exception)
        {
            Latch(exception);
            return default;
        }
    }

    public AttachResult AttachAndVerify(CartKey cart)
    {
        if (!IsAvailable)
        {
            return AttachResult.Refused(HitchRefusal.SeamUnavailable, UnavailableDetail);
        }

        try
        {
            return AttachCore(cart);
        }
        catch (Exception exception)
        {
            Latch(exception);
            TryReleaseAfterFault(cart);
            return AttachResult.Refused(HitchRefusal.SeamUnavailable, "the attach seam threw " + exception.GetType().Name);
        }
    }

    public ReleaseResult ReleaseJoint(CartKey cart)
    {
        // Releasing control is never blocked by an unavailable probe: if the
        // joint exists, the members to release it exist too.
        try
        {
            return ReleaseCore(cart);
        }
        catch (Exception exception)
        {
            Latch(exception);
            return ReleaseResult.StillAttached;
        }
    }

    /// <summary>Everything the assignment rules read about a selected cart.
    /// </summary>
    internal CartAssignmentFacts ReadAssignmentFacts(CartKey key, Guid currentEpoch)
    {
        var facts = new CartAssignmentFacts
        {
            Selection = CartSelectionState.OneCart,
            SelectedInThisWorldLoad = key.IsFromEpoch(currentEpoch),
            CapabilityOk = IsAvailable,
        };
        CartObservation observation = Observe(key);
        facts.Resolved = observation.Resolved;
        facts.RecordExists = observation.RecordExists;
        facts.IsHandCart = observation.IsHandCart;
        facts.ViewValid = observation.ViewValid;
        facts.IsOwner = observation.IsOwner;
        facts.InUse = observation.InUse || observation.ContainerOpen;
        facts.Braked = observation.BrakeEngaged || observation.RootFrozen;
        facts.UpDot = observation.UpDot;
        facts.PlayerDistanceMetres = float.PositiveInfinity;
        Player player = Player.m_localPlayer;
        if (player != null && observation.Resolved)
        {
            Vector3 at = player.transform.position;
            facts.PlayerDistanceMetres = new WorkPoint(at.x, at.y, at.z).HorizontalDistanceTo(observation.CartPosition);
        }

        return facts;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vagon? ResolveCore(CartKey key)
    {
        if (_cachedKey.Equals(key) && _cachedCart != null)
        {
            // Compared by id, not by text: this runs every frame and must not
            // allocate.
            ZNetView cachedView = _cachedCart.m_nview;
            if (cachedView != null && cachedView.IsValid() && cachedView.GetZDO().m_uid == _cachedId)
            {
                return _cachedCart;
            }
        }

        List<Vagon> carts = Vagon.m_instances;
        for (int index = 0; index < carts.Count; index++)
        {
            Vagon candidate = carts[index];
            if (candidate == null)
            {
                continue;
            }

            ZNetView view = candidate.m_nview;
            if (view == null || !view.IsValid())
            {
                continue;
            }

            if (string.Equals(view.GetZDO().m_uid.ToString(), key.SessionId, StringComparison.Ordinal))
            {
                Remember(key, candidate);
                return candidate;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool RecordStillExists(CartKey key)
    {
        ZDOMan zdos = ZDOMan.instance;
        if (zdos == null)
        {
            return false;
        }

        ZDOID id = _cachedKey.Equals(key) ? _cachedId : ZDOID.None;
        if (id.IsNone() && !TryParseId(key.SessionId, out id))
        {
            return false;
        }

        return zdos.GetZDO(id) != null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ObserveCore(CartKey key, ref CartObservation observation)
    {
        Vagon? cart = ResolveCore(key);
        if (cart == null)
        {
            observation.Resolved = false;
            observation.RecordExists = RecordStillExists(key);
            return;
        }

        observation.Resolved = true;
        observation.RecordExists = true;
        ZNetView view = cart.m_nview;
        observation.ViewValid = view != null && view.IsValid();
        observation.IsOwner = observation.ViewValid && view!.IsOwner();
        observation.IsHandCart = cart.GetComponent<Catapult>() == null && cart.GetComponent<SiegeMachine>() == null;

        float bodyMass = 0f;
        Rigidbody[] bodies = cart.m_bodies;
        if (bodies != null)
        {
            for (int index = 0; index < bodies.Length; index++)
            {
                if (bodies[index] != null)
                {
                    bodyMass += bodies[index].mass;
                }
            }
        }

        observation.BodyMassSumKg = bodyMass;
        float cargoWeight = 0f;
        Container container = cart.m_container;
        if (container != null && container.GetInventory() != null)
        {
            Inventory inventory = container.GetInventory();
            cargoWeight = inventory.GetTotalWeight();
            observation.CargoStacks = inventory.NrOfItems();
            observation.ContainerOpen = container.IsInUse();
        }

        observation.CargoWeightKg = cargoWeight;
        observation.ExpectedMassKg = cart.m_baseMass + (cargoWeight * cart.m_itemWeightMassFactor);
        observation.InUse = cart.InUse();
        observation.AttachFlag = observation.ViewValid && view!.GetZDO().GetBool(ZDOVars.s_attachJointHash);

        Rigidbody? puller = _body.Rigidbody;
        Player player = Player.m_localPlayer;
        Rigidbody? playerBody = player != null ? player.GetComponent<Rigidbody>() : null;
        ConfigurableJoint joint = cart.m_attachJoin;
        observation.HasJoint = joint != null;
        if (joint != null)
        {
            Rigidbody connected = joint.connectedBody;
            observation.JointConnectedToPuller = connected != null && puller != null && connected == puller;
            observation.JointConnectedToLocalPlayer = connected != null && playerBody != null && connected == playerBody;
            observation.JointForceNewtons = joint.currentForce.magnitude;
        }

        bool anyJoint = false;
        bool playerJoint = false;
        List<Vagon> carts = Vagon.m_instances;
        for (int index = 0; index < carts.Count; index++)
        {
            Vagon other = carts[index];
            if (other == null || other.m_attachJoin == null)
            {
                continue;
            }

            anyJoint = true;
            Rigidbody otherConnected = other.m_attachJoin.connectedBody;
            if (playerBody != null && otherConnected != null && otherConnected == playerBody)
            {
                playerJoint = true;
            }
        }

        observation.AnyJointOnClient = anyJoint;
        observation.LocalPlayerHasJoint = playerJoint;
        if (player != null)
        {
            GameObject hover = player.GetHoverObject();
            observation.LocalPlayerHoveringCart = hover != null && hover.GetComponentInParent<Vagon>() == cart;
        }

        string? braked = _engagedBrakeCartId();
        observation.BrakeEngaged = braked != null && string.Equals(braked, key.SessionId, StringComparison.Ordinal);
        Rigidbody root = cart.m_body != null ? cart.m_body : cart.GetComponent<Rigidbody>();
        observation.RootFrozen = root != null && root.constraints == RigidbodyConstraints.FreezeAll;
        if (root != null)
        {
            observation.SpeedMetresPerSecond = root.linearVelocity.magnitude;
        }

        Transform transform = cart.transform;
        observation.UpDot = transform.up.y;
        observation.CartPosition = ToPoint(transform.position);
        observation.DetachDistanceMetres = cart.m_detachDistance;
        observation.BreakForceNewtons = cart.m_breakForce;
        observation.ExtraPullMassKg = cart.m_playerExtraPullMass;

        Transform attach = cart.m_attachPoint;
        Vector3 heading = attach != null ? attach.position - transform.position : transform.forward;
        heading.y = 0f;
        if (heading.sqrMagnitude < 0.0001f)
        {
            heading = transform.forward;
            heading.y = 0f;
        }

        heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
        observation.HeadingX = heading.x;
        observation.HeadingZ = heading.z;
        if (attach != null)
        {
            observation.HandlePosition = ToPoint(attach.position);
            observation.ApproachPoint = ToPoint(attach.position - cart.m_attachOffset);
            GameObject? pullerObject = _body.GameObject;
            observation.HitchDistanceMetres = pullerObject != null
                ? Vector3.Distance(pullerObject.transform.position + cart.m_attachOffset, attach.position)
                : float.NaN;
        }
        else
        {
            observation.HandlePosition = observation.CartPosition;
            observation.ApproachPoint = observation.CartPosition;
            observation.HitchDistanceMetres = float.NaN;
        }

        MeasureFootprint(cart, attach);
        observation.FootprintWidthMetres = _footprintWidth;
        observation.FootprintLengthMetres = _footprintLength;
        observation.HitchLengthMetres = _hitchLength;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ParkingGround ReadGroundCore(CartKey key)
    {
        Vagon? cart = ResolveCore(key);
        if (cart == null)
        {
            return default;
        }

        Transform transform = cart.transform;
        Vector3 centre = transform.position;
        Transform attach = cart.m_attachPoint;
        Vector3 along = attach != null ? attach.position - centre : transform.forward;
        along.y = 0f;
        along = along.sqrMagnitude > 0.0001f ? along.normalized : Vector3.forward;
        var across = new Vector3(along.z, 0f, -along.x);

        if (!TryGroundHeight(centre, out float middle) ||
            !TryGroundHeight(centre + (along * AlongOffsetMetres), out float front) ||
            !TryGroundHeight(centre - (along * AlongOffsetMetres), out float back) ||
            !TryGroundHeight(centre + (across * AcrossOffsetMetres), out float right) ||
            !TryGroundHeight(centre - (across * AcrossOffsetMetres), out float left))
        {
            return default;
        }

        float liquid = Floating.GetLiquidLevel(centre, 1f, LiquidType.All);
        return new ParkingGround
        {
            Measured = true,
            GradeAlongRatio = (front - back) / (2f * AlongOffsetMetres),
            GradeAcrossRatio = (right - left) / (2f * AcrossOffsetMetres),
            InWater = liquid > middle + 0.1f,
        };
    }

    /// <summary>The highest static surface under a point, starting just above
    /// the cart (so a roof overhead is not mistaken for the ground) and
    /// skipping every moving body, the cart and Gunnar included.</summary>
    private bool TryGroundHeight(Vector3 point, out float height)
    {
        height = 0f;
        Vector3 origin = point + (Vector3.up * GroundProbeHeightMetres);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, GroundProbeHeightMetres + GroundProbeDepthMetres, _groundMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = _hits[index];
            if (hit.collider == null || hit.collider.attachedRigidbody != null)
            {
                continue;
            }

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                height = hit.point.y;
            }
        }

        return !float.IsPositiveInfinity(nearest);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private AttachResult AttachCore(CartKey key)
    {
        Vagon? cart = ResolveCore(key);
        if (cart == null)
        {
            return AttachResult.Refused(HitchRefusal.CartGone, "the cart does not resolve");
        }

        GameObject? pullerObject = _body.GameObject;
        Rigidbody? puller = _body.Rigidbody;
        if (pullerObject == null || puller == null)
        {
            return AttachResult.Refused(HitchRefusal.PullerBodyInvalid, "Gunnar's body or its rigidbody is gone");
        }

        ZNetView view = cart.m_nview;
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return AttachResult.Refused(HitchRefusal.NotOwnedHere, "this client does not own the cart at the moment of attaching");
        }

        List<Vagon> carts = Vagon.m_instances;
        for (int index = 0; index < carts.Count; index++)
        {
            if (carts[index] != null && carts[index].m_attachJoin != null)
            {
                return AttachResult.Refused(HitchRefusal.OtherJointOnClient, "a cart on this client holds a joint at the moment of attaching");
            }
        }

        cart.AttachTo(pullerObject);

        ConfigurableJoint joint = cart.m_attachJoin;
        float expectedMass = _body.CalibratedMassKg + cart.m_playerExtraPullMass;
        string? problem =
            joint == null ? "no joint was created" :
            joint.connectedBody != puller ? "the joint is not connected to Gunnar's rigidbody" :
            !cart.IsAttached() ? "the cart does not report itself attached" :
            !view.GetZDO().GetBool(ZDOVars.s_attachJointHash) ? "the cart's attach flag is not set" :
            !_execution.MassesAgree(puller.mass, expectedMass)
                ? string.Format(CultureInfo.InvariantCulture, "Gunnar weighs {0:0.##} kg attached, expected {1:0.##} kg", puller.mass, expectedMass)
                : null;
        if (problem != null)
        {
            cart.Detach();
            return AttachResult.Refused(HitchRefusal.VerifyFailed, problem);
        }

        return AttachResult.Verified();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private ReleaseResult ReleaseCore(CartKey key)
    {
        Vagon? cart = ResolveCore(key);
        if (cart == null)
        {
            return ReleaseResult.NoCart;
        }

        ConfigurableJoint joint = cart.m_attachJoin;
        if (joint != null)
        {
            Rigidbody connected = joint.connectedBody;
            Rigidbody? puller = _body.Rigidbody;
            if (connected != null && (puller == null || connected != puller))
            {
                return ReleaseResult.NotOurs;
            }
        }

        bool held = joint != null;
        cart.Detach();
        return cart.m_attachJoin != null ? ReleaseResult.StillAttached : held ? ReleaseResult.Released : ReleaseResult.NoJoint;
    }

    private void TryReleaseAfterFault(CartKey key)
    {
        try
        {
            ReleaseCore(key);
        }
        catch
        {
            // The seam is already latched off; nothing more can be done here.
        }
    }

    private void MeasureFootprint(Vagon cart, Transform? attach)
    {
        if (_footprintMeasured)
        {
            return;
        }

        Transform root = cart.transform;
        bool any = false;
        var min = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, 0f, float.NegativeInfinity);
        foreach (Collider collider in cart.GetComponentsInChildren<Collider>())
        {
            if (collider == null || collider.isTrigger || !collider.enabled)
            {
                continue;
            }

            Bounds bounds = collider.bounds;
            Vector3 extents = bounds.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                var world = new Vector3(
                    bounds.center.x + ((corner & 1) == 0 ? -extents.x : extents.x),
                    bounds.center.y + ((corner & 2) == 0 ? -extents.y : extents.y),
                    bounds.center.z + ((corner & 4) == 0 ? -extents.z : extents.z));
                Vector3 local = root.InverseTransformPoint(world);
                min.x = Math.Min(min.x, local.x);
                min.z = Math.Min(min.z, local.z);
                max.x = Math.Max(max.x, local.x);
                max.z = Math.Max(max.z, local.z);
                any = true;
            }
        }

        _footprintMeasured = true;
        if (!any)
        {
            _footprintWidth = 0f;
            _footprintLength = 0f;
            _hitchLength = 0f;
            return;
        }

        _footprintWidth = max.x - min.x;
        _footprintLength = max.z - min.z;
        Vector3 toHandle = attach != null ? attach.position - root.position : Vector3.zero;
        toHandle.y = 0f;
        _hitchLength = toHandle.magnitude;
    }

    private void Latch(Exception exception)
    {
        if (_faultDetail.Length == 0)
        {
            _faultDetail = "the cart seam faulted (" + exception.GetType().Name + ": " + exception.Message + ")";
        }
    }

    private static bool TryParseId(string sessionId, out ZDOID id)
    {
        id = ZDOID.None;
        int colon = sessionId.IndexOf(':');
        if (colon <= 0 ||
            !long.TryParse(sessionId.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) ||
            !uint.TryParse(sessionId.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint number))
        {
            return false;
        }

        id = new ZDOID(user, number);
        return true;
    }

    private static WorkPoint ToPoint(Vector3 value) => new WorkPoint(value.x, value.y, value.z);
}
