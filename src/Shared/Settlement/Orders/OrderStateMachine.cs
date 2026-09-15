namespace TheConcernedCat.Settlement.Orders;

/// <summary>How far one work order has got.
///
/// <see cref="Blocked"/> is deliberately <b>not</b> in this list. "Waiting for
/// wood" and "cannot reach the site" are reasons attached to the stage an order
/// is already at, not stages of their own — modelling them as states would make
/// progress non-monotonic and turn every "has this finished?" question into a
/// case analysis.</summary>
internal enum OrderState
{
    /// <summary>Sketched, not approved. Nothing has been reserved.</summary>
    Draft = 0,

    /// <summary>The player approved the footprint and the cost.</summary>
    Approved = 1,

    /// <summary>Materials are held against the designated container.</summary>
    Reserved = 2,

    /// <summary>A worker is fetching what the reservation did not cover.</summary>
    Gathering = 3,

    /// <summary>Pieces are being placed, in order.</summary>
    Building = 4,

    /// <summary>Every piece is placed and every reservation is settled.</summary>
    Completed = 5,

    /// <summary>The player cancelled. Unspent custody has been returned, once.</summary>
    Cancelled = 6,

    /// <summary>A commit's two halves disagree and this build will not guess
    /// which happened. Terminal for automatic progress <b>by design</b>: it
    /// exists so a settlement halts and explains itself instead of quietly
    /// inventing or losing material.</summary>
    NeedsRepair = 7,
}

internal enum OrderTransition
{
    Approve = 0,
    Reserve = 1,
    BeginGathering = 2,
    BeginBuilding = 3,
    Complete = 4,
    Cancel = 5,
    FlagForRepair = 6,
}

internal enum OrderTransitionOutcome
{
    /// <summary>The order moved.</summary>
    Advanced = 0,

    /// <summary>Already at or past this point. Nothing changed, and nothing is
    /// wrong — this is what makes a replayed request safe.</summary>
    AlreadySatisfied = 1,

    /// <summary>Refused. The order is in a terminal state, or the transition
    /// is not defined.</summary>
    Rejected = 2,
}

/// <summary>The single authority on work-order progress.
///
/// Three properties, and every caller depends on all three:
///
/// <list type="bullet">
/// <item><b>Total.</b> Any state plus any transition yields an answer, so no
/// sequence of crashes, retries and reconnects can reach an undefined
/// state.</item>
/// <item><b>Monotonic.</b> Progress never decreases. An order that reached
/// Building cannot be walked back to Approved by a replayed message, which is
/// what stops a replayed journal re-reserving material.</item>
/// <item><b>Idempotent.</b> Re-applying a transition reports
/// <see cref="OrderTransitionOutcome.AlreadySatisfied"/> and changes nothing,
/// so one-time effects stay one-time.</item>
/// </list>
///
/// The two terminal states are terminal in different ways.
/// <see cref="OrderState.Cancelled"/> is a finished decision.
/// <see cref="OrderState.NeedsRepair"/> is an unfinished one, parked where a
/// person can see it — and nothing in this type will move an order out of it,
/// because a build that could resolve a partial commit automatically would be a
/// build that guesses.</summary>
internal static class OrderStateMachine
{
    public static bool IsTerminal(OrderState state)
    {
        return state == OrderState.Completed
            || state == OrderState.Cancelled
            || state == OrderState.NeedsRepair;
    }

    /// <summary>True once material has been taken from a container, so a
    /// cancellation from here owes a refund.</summary>
    public static bool HoldsCustody(OrderState state)
    {
        return state == OrderState.Reserved
            || state == OrderState.Gathering
            || state == OrderState.Building;
    }

    public static OrderTransitionOutcome TryApply(
        OrderState current, OrderTransition transition, out OrderState next)
    {
        next = current;

        // Repair outranks everything, including cancellation: an order whose
        // material state is unknown must not be quietly tidied away.
        if (transition == OrderTransition.FlagForRepair)
        {
            if (current == OrderState.NeedsRepair)
            {
                return OrderTransitionOutcome.AlreadySatisfied;
            }

            next = OrderState.NeedsRepair;
            return OrderTransitionOutcome.Advanced;
        }

        if (current == OrderState.NeedsRepair)
        {
            // Only a person gets an order out of here, through an explicit
            // repair path that is not a state transition.
            return OrderTransitionOutcome.Rejected;
        }

        if (transition == OrderTransition.Cancel)
        {
            if (current == OrderState.Cancelled)
            {
                return OrderTransitionOutcome.AlreadySatisfied;
            }

            if (current == OrderState.Completed)
            {
                // A finished cottage is not cancellable. Taking it down is a
                // different act with a different cost.
                return OrderTransitionOutcome.Rejected;
            }

            next = OrderState.Cancelled;
            return OrderTransitionOutcome.Advanced;
        }

        if (IsTerminal(current))
        {
            return OrderTransitionOutcome.Rejected;
        }

        OrderState target = TargetFor(transition);
        if (target == current)
        {
            return OrderTransitionOutcome.AlreadySatisfied;
        }

        if (Rank(current) >= Rank(target))
        {
            // Monotonic: a replayed earlier message is satisfied, not applied.
            return OrderTransitionOutcome.AlreadySatisfied;
        }

        next = target;
        return OrderTransitionOutcome.Advanced;
    }

    private static OrderState TargetFor(OrderTransition transition)
    {
        switch (transition)
        {
            case OrderTransition.Approve: return OrderState.Approved;
            case OrderTransition.Reserve: return OrderState.Reserved;
            case OrderTransition.BeginGathering: return OrderState.Gathering;
            case OrderTransition.BeginBuilding: return OrderState.Building;
            case OrderTransition.Complete: return OrderState.Completed;
            default: return OrderState.Draft;
        }
    }

    /// <summary>Progress ordering. Both ordinary terminals rank above every
    /// working stage, so nothing walks back out of them.</summary>
    public static int Rank(OrderState state)
    {
        switch (state)
        {
            case OrderState.Draft: return 0;
            case OrderState.Approved: return 1;
            case OrderState.Reserved: return 2;
            case OrderState.Gathering: return 3;
            case OrderState.Building: return 4;
            case OrderState.Completed: return 5;
            case OrderState.Cancelled: return 5;
            case OrderState.NeedsRepair: return 6;
            default: return 0;
        }
    }

    /// <summary>True when this build defines the state. A journal written by a
    /// newer build can carry a stage this one has never heard of; the codec
    /// uses this to refuse the file rather than trust an out-of-range
    /// cast.</summary>
    public static bool IsKnown(OrderState state)
    {
        switch (state)
        {
            case OrderState.Draft:
            case OrderState.Approved:
            case OrderState.Reserved:
            case OrderState.Gathering:
            case OrderState.Building:
            case OrderState.Completed:
            case OrderState.Cancelled:
            case OrderState.NeedsRepair:
                return true;
            default:
                return false;
        }
    }
}
