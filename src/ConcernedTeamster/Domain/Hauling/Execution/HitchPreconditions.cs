using System;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>Whether one hitch may happen now, and if not, the first reason.
/// </summary>
internal readonly struct HitchVerdict
{
    private HitchVerdict(bool allowed, HitchRefusal refusal, string detail)
    {
        Allowed = allowed;
        Refusal = refusal;
        Detail = detail ?? string.Empty;
    }

    public bool Allowed { get; }

    /// <summary>Unspecified only when allowed.</summary>
    public HitchRefusal Refusal { get; }

    /// <summary>The exact failed condition, for the log and the evidence.
    /// </summary>
    public string Detail { get; }

    public static HitchVerdict Allow() => new HitchVerdict(true, HitchRefusal.Unspecified, string.Empty);

    public static HitchVerdict Refuse(HitchRefusal refusal, string detail)
    {
        if (refusal == HitchRefusal.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), "A refusal needs a reason.");
        }

        return new HitchVerdict(false, refusal, detail);
    }
}

/// <summary>Every precondition of DECISIONS.md D4, evaluated from facts the
/// adapter read in the same frame as the attach call it guards.
///
/// The order is D4's own (hand cart, owned, mass current, not in use, brake,
/// no joint on the client, upright, reach, puller body), preceded by the three
/// things that make every other question moot: the seam exists, work authority
/// is granted, the lease is active and its cart still resolves. A cart failing
/// several conditions reports the first; nothing here ever grants on a value it
/// could not read (NaN and missing facts refuse).</summary>
internal static class HitchPreconditions
{
    public static HitchVerdict Evaluate(
        bool seamAvailable,
        WorkAuthorityVerdict authority,
        bool leaseActive,
        CartObservation cart,
        PullerBodyFacts puller,
        HaulLimits limits,
        HaulExecutionLimits execution)
    {
        if (!seamAvailable)
        {
            return HitchVerdict.Refuse(HitchRefusal.SeamUnavailable, "the cart attach seam is not available on this game build");
        }

        if (authority != WorkAuthorityVerdict.Granted)
        {
            return HitchVerdict.Refuse(HitchRefusal.NoAuthority, "work authority is " + authority);
        }

        if (!leaseActive)
        {
            return HitchVerdict.Refuse(HitchRefusal.LeaseNotActive, "no active lease holds this cart for Gunnar");
        }

        if (!cart.Resolved)
        {
            return HitchVerdict.Refuse(
                HitchRefusal.CartGone,
                cart.RecordExists ? "the cart is not loaded" : "the cart no longer exists");
        }

        // D4.1: a live hand cart with a valid network view.
        if (!cart.ViewValid)
        {
            return HitchVerdict.Refuse(HitchRefusal.CartGone, "the cart's network view is not valid");
        }

        if (!cart.IsHandCart)
        {
            return HitchVerdict.Refuse(HitchRefusal.CartGone, "the leased object is no longer a hand cart");
        }

        // D4.2: this client already owns the cart. Decided through the one
        // policy that governs every Teamster cart mutation, so the document,
        // the brake and hauling cannot drift apart.
        if (!CartAuthorityPolicy.MayMutate(TeamsterFeature.GunnarHauling, cart.Authority))
        {
            return HitchVerdict.Refuse(HitchRefusal.NotOwnedHere, "cart authority is " + cart.Authority);
        }

        // D4.3: the cart's bodies weigh what its load says, so no stale, light
        // mass is pulled right after ownership moved here.
        if (!execution.MassesAgree(cart.BodyMassSumKg, cart.ExpectedMassKg) || !(cart.ExpectedMassKg > 0f))
        {
            return HitchVerdict.Refuse(
                HitchRefusal.MassNotCurrent,
                FormattableString.Invariant($"cart bodies weigh {cart.BodyMassSumKg:0.##} kg, its load says {cart.ExpectedMassKg:0.##} kg"));
        }

        // D4.4: nobody is using it: container closed, no joint, no attach flag.
        if (cart.InUse || cart.ContainerOpen || cart.HasJoint || cart.AttachFlag)
        {
            return HitchVerdict.Refuse(
                HitchRefusal.InUse,
                cart.ContainerOpen ? "the cart's container is open" : "the cart is already attached to someone");
        }

        // D4.5: the parking brake is respected, never released.
        if (cart.BrakeEngaged || cart.RootFrozen)
        {
            return HitchVerdict.Refuse(
                HitchRefusal.Braked,
                cart.BrakeEngaged ? "Teamster's parking brake holds the cart" : "the cart's body is frozen in place");
        }

        // D4.6: the cart's attach detaches every loaded cart first.
        if (cart.AnyJointOnClient)
        {
            return HitchVerdict.Refuse(HitchRefusal.OtherJointOnClient, "another cart on this client holds a joint");
        }

        // D4.7: stricter than vanilla's own 0.1.
        if (!(cart.UpDot >= limits.MinUprightDot))
        {
            return HitchVerdict.Refuse(
                HitchRefusal.NotUpright,
                FormattableString.Invariant($"cart up axis {cart.UpDot:0.###} is below {limits.MinUprightDot:0.###}"));
        }

        // D4.8: Gunnar walked there, well inside vanilla's own detach distance.
        float reach = limits.HitchReachFraction * cart.DetachDistanceMetres;
        if (!(cart.DetachDistanceMetres > 0f) || !(cart.HitchDistanceMetres <= reach))
        {
            return HitchVerdict.Refuse(
                HitchRefusal.OutOfReach,
                FormattableString.Invariant($"hitch distance {cart.HitchDistanceMetres:0.##} m exceeds {reach:0.##} m"));
        }

        // D4.9: the body the joint will hold.
        string? bodyProblem = DescribePullerProblem(puller, execution);
        if (bodyProblem != null)
        {
            return HitchVerdict.Refuse(HitchRefusal.PullerBodyInvalid, bodyProblem);
        }

        return HitchVerdict.Allow();
    }

    /// <summary>Why Gunnar's body cannot be a puller, or null when it can. The
    /// body must be present, alive and working; carry a non-kinematic rigidbody
    /// with gravity and collisions on (a kinematic body would drag the cart with
    /// unlimited force); stay upright under the joint's torque; be unscaled (the
    /// joint's anchor is in its local space); and weigh exactly its D5
    /// calibration, both now and as vanilla's base mass.</summary>
    public static string? DescribePullerProblem(PullerBodyFacts puller, HaulExecutionLimits execution)
    {
        if (!puller.Present)
        {
            return "Gunnar's body is not present and owned here";
        }

        if (puller.Faulted)
        {
            return "Gunnar's worker faulted and is inert";
        }

        if (puller.Dead)
        {
            return "Gunnar's body is dead";
        }

        if (!puller.HasRigidbody)
        {
            return "Gunnar's body has no rigidbody on the object the joint would hold";
        }

        if (puller.IsKinematic)
        {
            return "Gunnar's body is kinematic";
        }

        if (!puller.UsesGravity || !puller.DetectsCollisions)
        {
            return "Gunnar's body has gravity or collisions switched off";
        }

        if (!puller.RotationLockedUpright)
        {
            return "Gunnar's rotation constraints do not match the player's (or were never measured)";
        }

        if (!puller.UnitScale)
        {
            return "Gunnar's body is scaled";
        }

        if (!HaulExecutionLimits.IsFinite(puller.CalibratedMassKg) || !(puller.CalibratedMassKg > 0f))
        {
            return "Gunnar's pull strength has not been calibrated to the player";
        }

        if (!execution.MassesAgree(puller.BodyMassKg, puller.CalibratedMassKg) ||
            !execution.MassesAgree(puller.BaseMassKg, puller.CalibratedMassKg))
        {
            return FormattableString.Invariant(
                $"Gunnar's body weighs {puller.BodyMassKg:0.##} kg (base {puller.BaseMassKg:0.##} kg), calibrated {puller.CalibratedMassKg:0.##} kg");
        }

        return null;
    }
}
