using System;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>What Gunnar's body is, read by the adapter in one frame.
///
/// A plain value with settable members on purpose: the adapter fills one per
/// frame without allocating, and a default instance is the fail-closed answer
/// (no body, no rigidbody, nothing calibrated).</summary>
internal struct PullerBodyFacts
{
    /// <summary>A bound body exists, its network view is valid and this process
    /// owns it: the only state in which the vanilla motor runs for it.</summary>
    public bool Present { get; set; }

    public bool Dead { get; set; }

    /// <summary>The worker's tick threw and latched; the body is inert.</summary>
    public bool Faulted { get; set; }

    /// <summary>More than one body carries Gunnar's identity, so none is bound
    /// (C2 <c>WorkerBodyDuplicated</c>).</summary>
    public bool Duplicated { get; set; }

    public WorkPoint Position { get; set; }

    /// <summary>Horizontal facing, not necessarily normalized.</summary>
    public float ForwardX { get; set; }

    public float ForwardZ { get; set; }

    public float SpeedMetresPerSecond { get; set; }

    /// <summary>A <c>Rigidbody</c> sits on the exact object passed to the cart's
    /// attach (the attach searches neither parents nor children).</summary>
    public bool HasRigidbody { get; set; }

    public bool IsKinematic { get; set; }

    public bool UsesGravity { get; set; }

    public bool DetectsCollisions { get; set; }

    /// <summary>The body's rotation constraints match the local player's,
    /// measured at calibration: whatever keeps a player upright under the
    /// joint's anchor 0.8 m above the pivot keeps Gunnar upright too. False
    /// until measured.</summary>
    public bool RotationLockedUpright { get; set; }

    /// <summary>The body's world scale is one (within tolerance).</summary>
    public bool UnitScale { get; set; }

    /// <summary><c>Rigidbody.mass</c> right now.</summary>
    public float BodyMassKg { get; set; }

    /// <summary>The character's base mass (<c>m_originalMass</c>): what vanilla's
    /// own <c>SetExtraMass</c> adds a cart's pull mass to, and resets to on
    /// detach.</summary>
    public float BaseMassKg { get; set; }

    /// <summary>The local player's measured base mass this body was calibrated
    /// to (DECISIONS.md D5), or NaN when no measurement exists yet.</summary>
    public float CalibratedMassKg { get; set; }

    /// <summary>The body's own pathfinder found a path on its last request (a
    /// field read, never a fresh search).</summary>
    public bool HasPath { get; set; }

    /// <summary>A walk or steer command is in force.</summary>
    public bool MotorCommanded { get; set; }
}

/// <summary>Everything the mechanics read about the leased cart in one frame:
/// identity, ownership, use, brake, joint, pose and the hitch geometry measured
/// the way vanilla's <c>CanAttach</c> measures it.
///
/// A default instance is the fail-closed answer: the cart does not resolve.
/// </summary>
internal struct CartObservation
{
    /// <summary>A live cart component resolves from the lease's key in this
    /// world load.</summary>
    public bool Resolved { get; set; }

    /// <summary>Only meaningful when <see cref="Resolved"/> is false: the cart's
    /// network record still exists, so it was unloaded rather than destroyed.
    /// </summary>
    public bool RecordExists { get; set; }

    /// <summary>A hand cart: no siege-engine component on it.</summary>
    public bool IsHandCart { get; set; }

    public bool ViewValid { get; set; }

    public bool IsOwner { get; set; }

    /// <summary>The seam's capability probe verified every member it uses.
    /// </summary>
    public bool CapabilityOk { get; set; }

    /// <summary>The local client's vanilla relationship to this cart, resolved
    /// fail-closed by the same resolver every Teamster feature uses.</summary>
    public CartAuthority Authority => CartAuthorityResolver.Resolve(CapabilityOk, ViewValid, IsOwner);

    /// <summary>Sum of the cart's own body masses (<c>m_bodies</c>).</summary>
    public float BodyMassSumKg { get; set; }

    /// <summary><c>m_baseMass + totalWeight × m_itemWeightMassFactor</c>: what the
    /// owner's periodic mass update converges to.</summary>
    public float ExpectedMassKg { get; set; }

    public bool ContainerOpen { get; set; }

    /// <summary>Vanilla's own <c>InUse()</c>: container open, a local joint or the
    /// replicated attach flag.</summary>
    public bool InUse { get; set; }

    /// <summary>This cart holds a joint component.</summary>
    public bool HasJoint { get; set; }

    /// <summary>This cart's joint is connected to Gunnar's own body.</summary>
    public bool JointConnectedToPuller { get; set; }

    /// <summary>This cart's joint is connected to the local player's body.
    /// </summary>
    public bool JointConnectedToLocalPlayer { get; set; }

    /// <summary>The replicated <c>attachJoint</c> flag on the cart's record.
    /// </summary>
    public bool AttachFlag { get; set; }

    /// <summary>Teamster's parking brake holds this cart.</summary>
    public bool BrakeEngaged { get; set; }

    /// <summary>The cart's root body is frozen (any brake, from any source).
    /// </summary>
    public bool RootFrozen { get; set; }

    /// <summary>Some cart on this client holds a joint. The cart's attach
    /// detaches every loaded cart first, so attaching would steal it.</summary>
    public bool AnyJointOnClient { get; set; }

    /// <summary>Some cart on this client is attached to the local player.
    /// </summary>
    public bool LocalPlayerHasJoint { get; set; }

    /// <summary>The local player is pointing at this cart (its hover target
    /// resolves to it): the evidence that a vanished joint was the player's own
    /// Use.</summary>
    public bool LocalPlayerHoveringCart { get; set; }

    /// <summary><c>transform.up.y</c>.</summary>
    public float UpDot { get; set; }

    /// <summary><c>Distance(puller.position + m_attachOffset, m_attachPoint.position)</c>,
    /// exactly as <c>CanAttach</c> measures it; NaN without a body.</summary>
    public float HitchDistanceMetres { get; set; }

    /// <summary><c>m_detachDistance</c>.</summary>
    public float DetachDistanceMetres { get; set; }

    public WorkPoint CartPosition { get; set; }

    /// <summary>The handle, <c>m_attachPoint.position</c>.</summary>
    public WorkPoint HandlePosition { get; set; }

    /// <summary>Where the puller's pivot rests when the joint is relaxed: the
    /// handle minus <c>m_attachOffset</c>.</summary>
    public WorkPoint ApproachPoint { get; set; }

    /// <summary>Horizontal unit vector from the cart's centre to its handle:
    /// the direction the cart is pulled.</summary>
    public float HeadingX { get; set; }

    public float HeadingZ { get; set; }

    public float SpeedMetresPerSecond { get; set; }

    /// <summary><c>m_playerExtraPullMass</c>: what vanilla's attach adds to a
    /// character puller's base mass.</summary>
    public float ExtraPullMassKg { get; set; }

    /// <summary>Magnitude of the joint's current force; zero without a joint.
    /// </summary>
    public float JointForceNewtons { get; set; }

    /// <summary><c>m_breakForce</c>.</summary>
    public float BreakForceNewtons { get; set; }

    /// <summary>The cart's footprint, measured from its colliders.</summary>
    public float FootprintWidthMetres { get; set; }

    public float FootprintLengthMetres { get; set; }

    public float HitchLengthMetres { get; set; }

    /// <summary>Cargo, for evidence only; the mechanics never count material.
    /// </summary>
    public int CargoStacks { get; set; }

    public float CargoWeightKg { get; set; }

    /// <summary>The cart's measured footprint, or false when it could not be
    /// measured (a route is then never planned).</summary>
    public bool TryGetFootprint(out CartFootprint footprint)
    {
        footprint = default;
        if (!(FootprintWidthMetres > 0f) || !(FootprintLengthMetres > 0f) || !(HitchLengthMetres >= 0f) ||
            !HaulExecutionLimits.IsFinite(FootprintWidthMetres) || !HaulExecutionLimits.IsFinite(FootprintLengthMetres) ||
            !HaulExecutionLimits.IsFinite(HitchLengthMetres))
        {
            return false;
        }

        footprint = new CartFootprint(FootprintWidthMetres, FootprintLengthMetres, HitchLengthMetres);
        return true;
    }
}

/// <summary>The ground a cart would be left standing on (the safe-parking
/// decision). Sampled around the cart along and across its heading, ignoring
/// every moving body. A default instance is unmeasured, which is unsafe.
/// </summary>
internal struct ParkingGround
{
    public bool Measured { get; set; }

    /// <summary>Rise over run along the cart's heading.</summary>
    public float GradeAlongRatio { get; set; }

    /// <summary>Rise over run across the cart.</summary>
    public float GradeAcrossRatio { get; set; }

    public bool InWater { get; set; }
}

/// <summary>What a hitch attempt did (DECISIONS.md D4).</summary>
internal readonly struct AttachResult
{
    private AttachResult(bool attached, HitchRefusal refusal, string detail)
    {
        Attached = attached;
        Refusal = refusal;
        Detail = detail ?? string.Empty;
    }

    /// <summary>The joint exists, is connected to Gunnar's body, the cart
    /// reports itself attached and its attach flag is set.</summary>
    public bool Attached { get; }

    /// <summary>Why not; unspecified only when attached.</summary>
    public HitchRefusal Refusal { get; }

    public string Detail { get; }

    public static AttachResult Verified() => new AttachResult(true, HitchRefusal.Unspecified, string.Empty);

    public static AttachResult Refused(HitchRefusal refusal, string detail)
    {
        if (refusal == HitchRefusal.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), "A refused attach needs a reason.");
        }

        return new AttachResult(false, refusal, detail);
    }
}

/// <summary>What a release of the joint did. Zero is unspecified.</summary>
internal enum ReleaseResult
{
    Unspecified = 0,

    /// <summary>The cart does not resolve; there is nothing to release.</summary>
    NoCart = 1,

    /// <summary>No joint existed; the cart's own detach ran anyway, which clears
    /// a stale attach flag exactly as vanilla's next update would.</summary>
    NoJoint = 2,

    /// <summary>Gunnar's joint was released through the cart's own detach.
    /// </summary>
    Released = 3,

    /// <summary>The joint belongs to another puller and was left alone:
    /// releasing it would take the cart out of someone else's hands.</summary>
    NotOurs = 4,

    /// <summary>The cart's detach ran but a joint to Gunnar still exists.
    /// </summary>
    StillAttached = 5,
}
