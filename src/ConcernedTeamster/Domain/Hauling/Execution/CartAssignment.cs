using System;
using TheConcernedCat.ConcernedTeamster.Domain.Authority;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>What the player's selection resolved to. Zero is unspecified.
/// </summary>
internal enum CartSelectionState
{
    Unspecified = 0,

    /// <summary>Nothing identifiable is selected.</summary>
    NothingSelected = 1,

    /// <summary>Exactly one cart is selected.</summary>
    OneCart = 2,

    /// <summary>More than one object could be meant. Never resolved by picking
    /// the nearest.</summary>
    SeveralCandidates = 3,
}

/// <summary>What the adapter read about a selected cart at the moment the
/// player confirmed it. A default instance selects nothing.</summary>
internal struct CartAssignmentFacts
{
    public CartSelectionState Selection { get; set; }

    /// <summary>The selection was made in this world load.</summary>
    public bool SelectedInThisWorldLoad { get; set; }

    public bool Resolved { get; set; }

    /// <summary>Only when not resolved: the cart's network record still exists
    /// (unloaded rather than destroyed).</summary>
    public bool RecordExists { get; set; }

    public bool IsHandCart { get; set; }

    public bool ViewValid { get; set; }

    public bool IsOwner { get; set; }

    public bool CapabilityOk { get; set; }

    public CartAuthority Authority => CartAuthorityResolver.Resolve(CapabilityOk, ViewValid, IsOwner);

    /// <summary>Vanilla's <c>InUse()</c>, or the container open.</summary>
    public bool InUse { get; set; }

    public bool Braked { get; set; }

    public float UpDot { get; set; }

    public float PlayerDistanceMetres { get; set; }
}

/// <summary>Outcome of an assignment attempt. Zero is unspecified.</summary>
internal enum AssignmentOutcome
{
    Unspecified = 0,
    Assigned = 1,

    /// <summary>Gunnar already holds exactly this cart; nothing changed.
    /// </summary>
    AlreadyAssigned = 2,

    Refused = 3,
}

internal readonly struct AssignmentVerdict
{
    private AssignmentVerdict(AssignmentOutcome outcome, CartAssignmentRefusal refusal, string detail)
    {
        Outcome = outcome;
        Refusal = refusal;
        Detail = detail ?? string.Empty;
    }

    public AssignmentOutcome Outcome { get; }

    /// <summary>Unspecified unless refused.</summary>
    public CartAssignmentRefusal Refusal { get; }

    public string Detail { get; }

    public bool Allowed => Outcome == AssignmentOutcome.Assigned || Outcome == AssignmentOutcome.AlreadyAssigned;

    public static AssignmentVerdict Assign() => new AssignmentVerdict(AssignmentOutcome.Assigned, CartAssignmentRefusal.Unspecified, string.Empty);

    public static AssignmentVerdict Already() => new AssignmentVerdict(AssignmentOutcome.AlreadyAssigned, CartAssignmentRefusal.Unspecified, string.Empty);

    public static AssignmentVerdict Refuse(CartAssignmentRefusal refusal, string detail)
    {
        if (refusal == CartAssignmentRefusal.Unspecified)
        {
            throw new ArgumentOutOfRangeException(nameof(refusal), "A refusal needs a reason.");
        }

        return new AssignmentVerdict(AssignmentOutcome.Refused, refusal, detail);
    }
}

/// <summary>The rules of "assign this cart to Gunnar" (CART-01). The cart is the
/// one the player explicitly selected and confirmed; a cart being near is
/// never enough, and an uncertain identity is refused, never replaced by the
/// nearest cart.</summary>
internal static class CartAssignmentValidator
{
    public static AssignmentVerdict Evaluate(
        WorkAuthorityVerdict authority,
        bool workerAvailable,
        CartAssignmentFacts facts,
        CartKey? selectedKey,
        CartLeaseBook leases,
        WorkerKey worker,
        HaulLimits limits,
        HaulExecutionLimits execution)
    {
        if (leases == null)
        {
            throw new ArgumentNullException(nameof(leases));
        }

        if (authority != WorkAuthorityVerdict.Granted)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.NoAuthority, "work authority is " + authority);
        }

        if (!workerAvailable)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.WorkerUnavailable, "Gunnar cannot work right now");
        }

        if (facts.Selection != CartSelectionState.OneCart || selectedKey == null || selectedKey.Value.IsEmpty)
        {
            return AssignmentVerdict.Refuse(
                CartAssignmentRefusal.AmbiguousSelection,
                facts.Selection == CartSelectionState.SeveralCandidates
                    ? "more than one object could be meant"
                    : "no cart is selected");
        }

        CartKey key = selectedKey.Value;
        if (!facts.SelectedInThisWorldLoad || !key.IsFromEpoch(leases.WorldLoadEpoch))
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.StaleIdentity, "the selection belongs to an earlier world load");
        }

        // Already Gunnar's: idempotent, whatever else is now true of it (it is
        // in use by definition once he is hitched to it).
        if (leases.TryGetActiveForWorker(worker, out CartLease? held) && held != null && held.Cart.Equals(key))
        {
            return AssignmentVerdict.Already();
        }

        if (!facts.Resolved)
        {
            return facts.RecordExists
                ? AssignmentVerdict.Refuse(CartAssignmentRefusal.NotLoaded, "the cart is not loaded")
                : AssignmentVerdict.Refuse(CartAssignmentRefusal.Destroyed, "the cart no longer exists");
        }

        if (!facts.ViewValid)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.Destroyed, "the cart's network view is not valid");
        }

        if (!facts.IsHandCart)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.NotACart, "the selection is not a hand cart");
        }

        if (!(facts.PlayerDistanceMetres <= execution.SelectionReachMetres))
        {
            return AssignmentVerdict.Refuse(
                CartAssignmentRefusal.TooFarToSelect,
                FormattableString.Invariant($"the cart is {facts.PlayerDistanceMetres:0.#} m away"));
        }

        if (!CartAuthorityPolicy.MayMutate(TeamsterFeature.GunnarHauling, facts.Authority))
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.NotOwnedHere, "cart authority is " + facts.Authority);
        }

        if (facts.InUse)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.InUse, "the cart is open or attached");
        }

        if (facts.Braked)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.Braked, "the parking brake holds the cart");
        }

        if (!(facts.UpDot >= limits.MinUprightDot))
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.NotUpright, "the cart is not upright");
        }

        if (held != null)
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.WorkerBusy, "Gunnar already holds cart " + held.Cart);
        }

        if (leases.TryGetActiveForCart(key, out _))
        {
            return AssignmentVerdict.Refuse(CartAssignmentRefusal.AlreadyLeased, "another lease holds the cart");
        }

        return AssignmentVerdict.Assign();
    }

    /// <summary>Maps what the lease book answered to the assignment outcome.
    /// The book is the authority on one lease per worker and per cart; the
    /// validator above only makes its refusals speak first.</summary>
    public static AssignmentVerdict FromLeaseOutcome(LeaseOutcome outcome)
    {
        switch (outcome)
        {
            case LeaseOutcome.Assigned:
                return AssignmentVerdict.Assign();
            case LeaseOutcome.AlreadySatisfied:
                return AssignmentVerdict.Already();
            case LeaseOutcome.RefusedWorkerBusy:
                return AssignmentVerdict.Refuse(CartAssignmentRefusal.WorkerBusy, "Gunnar already holds another cart");
            case LeaseOutcome.RefusedCartLeased:
            case LeaseOutcome.RejectedDifferentPayload:
            case LeaseOutcome.NotActive:
                return AssignmentVerdict.Refuse(CartAssignmentRefusal.AlreadyLeased, "the lease book refused: " + outcome);
            case LeaseOutcome.RefusedStaleEpoch:
                return AssignmentVerdict.Refuse(CartAssignmentRefusal.StaleIdentity, "the cart key is from another world load");
            default:
                return AssignmentVerdict.Refuse(CartAssignmentRefusal.AmbiguousSelection, "the lease book answered " + outcome);
        }
    }
}

/// <summary>A cart the player pointed at and has not confirmed yet.</summary>
internal readonly struct PendingCartSelection
{
    public PendingCartSelection(CartKey cart, string label, float selectedAt)
    {
        Cart = cart;
        Label = label ?? string.Empty;
        SelectedAt = selectedAt;
    }

    public CartKey Cart { get; }

    /// <summary>What the player was shown, for the confirmation line.</summary>
    public string Label { get; }

    public float SelectedAt { get; }
}

/// <summary>The two-step selection (CART-01): the player points at a cart and
/// selects it, then confirms. A confirmation applies only to the one pending
/// selection, only in the world load it was made in, and only for a while; it
/// never falls back to any other cart.</summary>
internal sealed class CartSelectionBook
{
    private PendingCartSelection? _pending;

    public PendingCartSelection? Pending => _pending;

    public void Propose(CartKey cart, string label, float now)
    {
        if (cart.IsEmpty)
        {
            throw new ArgumentException("A selection needs a cart.", nameof(cart));
        }

        _pending = new PendingCartSelection(cart, label, now);
    }

    public void Clear() => _pending = null;

    /// <summary>The pending selection when it may still be confirmed; the
    /// refusal otherwise. A confirmed or refused selection is consumed.
    /// </summary>
    public bool TryConfirm(Guid currentEpoch, float now, HaulExecutionLimits execution, out PendingCartSelection selection, out CartAssignmentRefusal refusal)
    {
        selection = default;
        refusal = CartAssignmentRefusal.Unspecified;
        PendingCartSelection? pending = _pending;
        _pending = null;

        if (pending == null)
        {
            refusal = CartAssignmentRefusal.AmbiguousSelection;
            return false;
        }

        if (!pending.Value.Cart.IsFromEpoch(currentEpoch))
        {
            refusal = CartAssignmentRefusal.StaleIdentity;
            return false;
        }

        if (!(now - pending.Value.SelectedAt <= execution.SelectionConfirmSeconds))
        {
            refusal = CartAssignmentRefusal.AmbiguousSelection;
            return false;
        }

        selection = pending.Value;
        return true;
    }
}
