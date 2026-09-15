using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Journal;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Recruitment;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.Settlement.Register;

/// <summary>Everything one settlement's player set up: what is marked, and who
/// is employed.
///
/// The book and the roster are private and stay private. Every change goes
/// through this type because three gates apply to all of them and only one of
/// them is structural: the runtime must be allowed to act, the record must be
/// writable, and the change must be legal. Exposing the book directly would let
/// a caller satisfy the third and skip the first two.
///
/// It also owns the only genuinely awkward operation in this leaf: undesignating
/// something that work depends on. That is split in two —
/// <see cref="PlanUndesignation"/> works out what would happen, and
/// <see cref="ApplyUndesignation"/> does it — because a player deserves to be
/// told "this cancels one order and puts 20 Wood back" <i>before</i> it
/// happens, and because a plan is a value a test can read.</summary>
internal sealed class SettlementRegister
{
    private readonly DesignationBook _book = new DesignationBook();
    private readonly WorkerRoster _roster = new WorkerRoster();

    public SettlementRegister(SettlementScope scope)
    {
        if (!scope.IsComplete)
        {
            throw new ArgumentException(
                "A settlement register needs a world and a settlement.", nameof(scope));
        }

        Scope = scope;
    }

    public SettlementScope Scope { get; }

    /// <summary>True when this build must not write over the file on disk: it
    /// was written by a newer build, belongs elsewhere, or could not be fully
    /// read. New designations and new recruitment are refused; what was read is
    /// kept and shown.</summary>
    public bool IsReadOnly { get; private set; }

    public bool IsDirty { get; private set; }

    public void MarkClean() => IsDirty = false;

    internal void MarkReadOnly() => IsReadOnly = true;

    public IReadOnlyList<Designation> Designations => _book.Designations;

    public IReadOnlyList<WorkerRecord> Workers => _roster.Workers;

    public bool HasSettlementArea => _book.Has(DesignationKind.SettlementArea);

    /// <summary>Tells this register which run of the world it is in, so a chest
    /// key written before a reload can be recognised as meaning nothing now.
    /// Until it is set, no chest can be designated or resolved.</summary>
    public void UseIdentityEpoch(string? epoch) => _book.UseIdentityEpoch(epoch);

    /// <summary>True when a chest is marked but its identity is from a previous
    /// run of the world, so it resolves to nothing until it is marked again.</summary>
    public bool HasStaleSupplyIdentity => _book.HasStaleSupplyIdentity;

    public bool TryGet(DesignationKind kind, out Designation designation)
    {
        return _book.TryGet(kind, out designation);
    }

    /// <summary>True when a tree at this point may be felled. False when no
    /// harvest area is marked — "nothing marked" is never "anywhere".</summary>
    public bool IsInHarvestArea(SitePoint point) => _book.IsInHarvestArea(point);

    /// <summary>True for the one designated container and nothing else.</summary>
    public bool IsSupplyContainer(string? containerKey) => _book.IsSupplyContainer(containerKey);

    public bool Employs(WorkerId id) => _roster.Contains(id);

    /// <summary>Marks something the player explicitly asked for.</summary>
    public DesignationResult Designate(
        DesignationRequest request, IDesignationSite site, bool authorised)
    {
        if (!authorised)
        {
            return DesignationResult.Refused(DesignationRefusal.NotAuthorised);
        }

        if (IsReadOnly)
        {
            return DesignationResult.Refused(DesignationRefusal.RecordReadOnly);
        }

        DesignationResult result = _book.Designate(request, site);
        if (result.Outcome == DesignationOutcome.Designated)
        {
            IsDirty = true;
        }

        return result;
    }

    public RecruitmentResult Recruit(WorkerId id, string role, bool authorised)
    {
        if (!authorised)
        {
            return RecruitmentResult.Refused(RecruitmentRefusal.NotAuthorised);
        }

        if (IsReadOnly)
        {
            return RecruitmentResult.Refused(RecruitmentRefusal.RecordReadOnly);
        }

        RecruitmentResult result = _roster.Recruit(id, role, HasSettlementArea);
        if (result.Outcome == RecruitmentOutcome.Recruited)
        {
            IsDirty = true;
        }

        return result;
    }

    /// <summary>Lets somebody go. Never happens as a side effect of anything
    /// else — not a cleared settlement, not a dead creature.</summary>
    public DismissalOutcome Dismiss(WorkerId id, bool authorised)
    {
        if (!authorised || IsReadOnly)
        {
            return DismissalOutcome.Refused;
        }

        if (!_roster.Dismiss(id))
        {
            return DismissalOutcome.NotOnRoster;
        }

        IsDirty = true;
        return DismissalOutcome.Dismissed;
    }

    /// <summary>Works out everything that clearing this designation would do,
    /// without doing any of it.
    ///
    /// The dependency rules are narrow and each one has a reason:
    ///
    /// <list type="bullet">
    /// <item><b>The supply container</b> is the only place an order's materials
    /// may have come from, so clearing it cancels every unfinished order that
    /// ever drew from it and returns whatever is still <i>held</i> against that
    /// order. Dependency is provenance rather than current holdings: an order
    /// that has already spent what it drew is still an order whose only source
    /// just went away. "Returns the container to untouched state" is meant
    /// literally — the refund is written before the designation goes away.</item>
    /// <item><b>The harvest area</b> is the only ground felling is legal on, so
    /// clearing it cancels orders that are actively <see cref="OrderState.Gathering"/>
    /// and nothing else. An order that already has what it needs is not
    /// affected by losing a source it is no longer using.</item>
    /// <item><b>The settlement area</b> owns the other two, so clearing it
    /// clears them as well and then cancels every remaining unfinished order in
    /// the settlement.</item>
    /// </list>
    ///
    /// Two things are deliberately never touched. An order in
    /// <see cref="OrderState.NeedsRepair"/> is left exactly as it is — its
    /// material state is unknown, and tidying it away as part of clearing some
    /// ground is precisely the guess #273's gate 3 forbids. And a reservation
    /// that is not <see cref="ReservationState.Held"/> is not refunded, because
    /// it has already been spent, already been returned, or is the unknown half
    /// of an interrupted commit.</summary>
    public UndesignationPlan PlanUndesignation(
        DesignationKind kind, ReplayResult state, bool authorised)
    {
        if (!authorised)
        {
            return UndesignationPlan.Refused(DesignationRefusal.NotAuthorised);
        }

        if (IsReadOnly)
        {
            return UndesignationPlan.Refused(DesignationRefusal.RecordReadOnly);
        }

        if (kind == DesignationKind.None)
        {
            return UndesignationPlan.Refused(DesignationRefusal.InvalidRequest);
        }

        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var removed = new List<Designation>();
        CollectRemovals(kind, removed);
        if (removed.Count == 0)
        {
            return UndesignationPlan.Nothing(kind);
        }

        var ordersToCancel = new List<OrderId>();
        var toRefund = new List<Reservation>();

        // Every flag below comes from what is ACTUALLY being removed, never
        // from the kind that was asked for. Clearing a settlement area that is
        // not marked -- possible when a file carries orphaned children -- must
        // not cancel every order in the settlement on the strength of a request
        // that removes nothing of that kind.
        bool clearsSettlement = false;
        bool clearsHarvest = false;
        string? clearedContainer = null;
        foreach (Designation gone in removed)
        {
            switch (gone.Kind)
            {
                case DesignationKind.SettlementArea: clearsSettlement = true; break;
                case DesignationKind.HarvestArea: clearsHarvest = true; break;
                case DesignationKind.SupplyContainer: clearedContainer = gone.ContainerKey; break;
            }
        }

        IReadOnlyList<Reservation> allReservations = state.Ledger.Reservations;

        // An order can be known ONLY by its reservation: replay records a state
        // for a transition entry, but a Reserved entry adds nothing to the
        // order table. Iterating that table alone would leave such an order
        // invisible to the cascade -- its material never returned, and the
        // player told that clearing costs nothing.
        var orderKeys = new List<string>();
        var seenOrders = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, OrderState> known in state.Orders)
        {
            if (seenOrders.Add(known.Key))
            {
                orderKeys.Add(known.Key);
            }
        }

        foreach (Reservation reservation in allReservations)
        {
            if (seenOrders.Add(reservation.Order.Value))
            {
                orderKeys.Add(reservation.Order.Value);
            }
        }

        foreach (string orderKey in orderKeys)
        {
            var order = new OrderId(orderKey);
            if (OrderStateMachine.IsTerminal(state.StateOf(order)))
            {
                continue;
            }

            // Dependency on a container is provenance, not current holdings.
            // An order that already spent everything it drew from that chest is
            // still an order whose only source has just gone away, and it is
            // unfinished — so it is cancelled. Asking "is it holding anything
            // from there right now" instead would leave exactly that order
            // running with nowhere left to draw from.
            bool drewFromClearedContainer = false;
            var held = new List<Reservation>();
            foreach (Reservation reservation in allReservations)
            {
                if (!reservation.Order.Equals(order))
                {
                    continue;
                }

                if (clearedContainer != null
                    && string.Equals(reservation.Container, clearedContainer, StringComparison.Ordinal))
                {
                    drewFromClearedContainer = true;
                }

                if (reservation.State == ReservationState.Held)
                {
                    held.Add(reservation);
                }
            }

            bool affected = clearsSettlement
                || (clearsHarvest && state.StateOf(order) == OrderState.Gathering)
                || drewFromClearedContainer;

            if (!affected)
            {
                continue;
            }

            ordersToCancel.Add(order);
            toRefund.AddRange(held);
        }

        return UndesignationPlan.For(
            kind, removed, ordersToCancel, toRefund, state.NextSequence, state.JournalInstance);
    }

    /// <summary>Carries out a plan.
    ///
    /// The plan is re-checked against the book first. A plan is a snapshot, and
    /// applying a stale one would remove a designation the player has since
    /// replaced, or cancel orders on the strength of a container that is no
    /// longer the one designated. Refusing a stale plan is cheaper than being
    /// subtly wrong about whose wood went where.
    ///
    /// The journal is written <b>before</b> the book changes, and refunds are
    /// written before cancellations, so that a crash at any point leaves a
    /// record which replays to a coherent state: material back in the container
    /// it came from, then the order that will not be finishing marked as
    /// cancelled.</summary>
    /// <summary><paramref name="authorised"/> is re-asked here and not taken
    /// from the plan. Authority is deliberately re-read on every act elsewhere
    /// in this runtime, and a plan can sit unconfirmed for as long as a player
    /// takes to type -- long enough to switch the runtime off in between. This
    /// was the one mutating entry point that did not honour that.</summary>
    public UndesignationOutcome ApplyUndesignation(
        UndesignationPlan plan, SettlementJournal journal, bool authorised)
    {
        if (!authorised)
        {
            return UndesignationOutcome.Refused;
        }

        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (journal == null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (plan.IsRefused)
        {
            return UndesignationOutcome.Refused;
        }

        if (IsReadOnly)
        {
            return UndesignationOutcome.Refused;
        }

        if (!journal.Scope.Equals(Scope))
        {
            throw new ArgumentException(
                "That journal belongs to a different settlement.", nameof(journal));
        }

        if (journal.IsReadOnly)
        {
            // The record of what this would do cannot be written, so the thing
            // itself must not happen. Otherwise the designation disappears
            // while the refund that justified it does not survive the session.
            return UndesignationOutcome.Refused;
        }

        if (plan.Removed.Count == 0)
        {
            return UndesignationOutcome.NotDesignated;
        }

        // A plan is a snapshot of BOTH the book and the journal, and staleness
        // in each is unsafe in a different way.
        //
        // The journal moving on is the dangerous one. A reservation appended
        // after the plan was made is not in ToRefund, so applying would cancel
        // its order without returning it -- and a cancelled order is terminal,
        // so no later cascade would ever reach that material again. A commit
        // started after the plan was made is worse: refunding it would put
        // material back that may already be standing as a wall, which is
        // exactly the guess this type refuses to make everywhere else.
        if (journal.NextSequence != plan.JournalSequence
            || journal.Instance != plan.JournalInstance)
        {
            return UndesignationOutcome.Stale;
        }

        // The book is re-checked by re-deriving the whole cascade rather than
        // by confirming the listed rows, because the cascade is what apply
        // actually performs. Confirming only plan.Removed would let a
        // designation marked since the plan was made be swept away unlisted,
        // undescribed, and with anything drawn from it stranded.
        var currentRemovals = new List<Designation>();
        CollectRemovals(plan.Kind, currentRemovals);
        if (currentRemovals.Count != plan.Removed.Count)
        {
            return UndesignationOutcome.Stale;
        }

        for (int index = 0; index < currentRemovals.Count; index++)
        {
            if (!currentRemovals[index].SameAs(plan.Removed[index]))
            {
                return UndesignationOutcome.Stale;
            }
        }

        foreach (Reservation reservation in plan.ToRefund)
        {
            journal.Append(
                JournalEntryKind.Refunded, reservation.Order, reservation.Request,
                container: reservation.Container, stacks: reservation.Stacks);
        }

        foreach (OrderId order in plan.OrdersToCancel)
        {
            journal.Append(
                JournalEntryKind.OrderTransition, order, transition: OrderTransition.Cancel);
        }

        _book.Undesignate(plan.Kind);
        IsDirty = true;
        return UndesignationOutcome.Removed;
    }

    private void CollectRemovals(DesignationKind kind, List<Designation> removed)
    {
        if (kind == DesignationKind.SettlementArea)
        {
            foreach (DesignationKind child in new[]
            {
                DesignationKind.SupplyContainer, DesignationKind.HarvestArea,
            })
            {
                if (_book.TryGet(child, out Designation owned))
                {
                    removed.Add(owned);
                }
            }
        }

        if (_book.TryGet(kind, out Designation target))
        {
            removed.Add(target);
        }
    }

    /// <summary>Restores a row read from disk. False when the row cannot be
    /// taken -- a second designation of a kind that already has one, or a
    /// worker past this build's roster size. The caller counts a refusal as a
    /// damaged line rather than dropping it quietly, because a row this build
    /// cannot represent is a row it must not write back either.</summary>
    internal bool Restore(Designation designation) => _book.Restore(designation);

    internal bool Restore(WorkerRecord record) => _roster.Restore(record);
}

internal enum UndesignationOutcome
{
    /// <summary>Nothing was removed. Zero, so an unfilled result never reads as
    /// success.</summary>
    Refused = 0,

    Removed = 1,

    /// <summary>There was nothing marked. Idempotent.</summary>
    NotDesignated = 2,

    /// <summary>The plan no longer matches what is marked, so it was not
    /// applied. Work out a fresh one.</summary>
    Stale = 3,
}

/// <summary>What clearing a designation would remove, cancel and return.
///
/// A value, on purpose: it can be shown to a player, asserted on in a test, and
/// handed back to <see cref="SettlementRegister.ApplyUndesignation"/>
/// unchanged.</summary>
internal sealed class UndesignationPlan
{
    private static readonly Designation[] NoDesignations = new Designation[0];
    private static readonly OrderId[] NoOrders = new OrderId[0];
    private static readonly Reservation[] NoReservations = new Reservation[0];

    private UndesignationPlan(
        DesignationKind kind,
        bool isRefused,
        DesignationRefusal refusal,
        IReadOnlyList<Designation> removed,
        IReadOnlyList<OrderId> ordersToCancel,
        IReadOnlyList<Reservation> toRefund,
        long journalSequence,
        Guid journalInstance)
    {
        Kind = kind;
        IsRefused = isRefused;
        Refusal = refusal;
        Removed = removed;
        OrdersToCancel = ordersToCancel;
        ToRefund = toRefund;
        JournalSequence = journalSequence;
        JournalInstance = journalInstance;
    }

    /// <summary>Which journal object this plan was worked out against. Scope
    /// and sequence together are not enough: two journals for the same
    /// settlement can hold different entries and still agree on both.</summary>
    public Guid JournalInstance { get; }

    /// <summary>One past the highest sequence the journal held when this plan
    /// was worked out.
    ///
    /// What to return and what to cancel both depend on the journal as it was
    /// at that instant. If it has grown since, the plan describes a settlement
    /// that no longer exists, and applying it would act on the difference
    /// without accounting for it.</summary>
    public long JournalSequence { get; }

    public DesignationKind Kind { get; }

    public bool IsRefused { get; }

    public DesignationRefusal Refusal { get; }

    /// <summary>Children first, then the designation asked for, so applying in
    /// order returns a container's contents before the settlement it stood in
    /// stops existing.</summary>
    public IReadOnlyList<Designation> Removed { get; }

    public IReadOnlyList<OrderId> OrdersToCancel { get; }

    /// <summary>Only reservations still <see cref="ReservationState.Held"/>.
    /// Spent, returned and unknown ones are never in here.</summary>
    public IReadOnlyList<Reservation> ToRefund { get; }

    public bool ChangesNothing => !IsRefused && Removed.Count == 0;

    internal static UndesignationPlan Refused(DesignationRefusal refusal)
    {
        return new UndesignationPlan(
            DesignationKind.None, true, refusal,
            NoDesignations, NoOrders, NoReservations, -1L, Guid.Empty);
    }

    internal static UndesignationPlan Nothing(DesignationKind kind)
    {
        return new UndesignationPlan(
            kind, false, DesignationRefusal.Unspecified,
            NoDesignations, NoOrders, NoReservations, -1L, Guid.Empty);
    }

    internal static UndesignationPlan For(
        DesignationKind kind,
        IReadOnlyList<Designation> removed,
        IReadOnlyList<OrderId> ordersToCancel,
        IReadOnlyList<Reservation> toRefund,
        long journalSequence,
        Guid journalInstance)
    {
        return new UndesignationPlan(
            kind, false, DesignationRefusal.Unspecified,
            removed, ordersToCancel, toRefund, journalSequence, journalInstance);
    }

    /// <summary>One sentence a player can read before deciding.</summary>
    public string Describe()
    {
        if (IsRefused)
        {
            return DesignationResult.Refused(Refusal).Describe();
        }

        if (ChangesNothing)
        {
            return "Nothing to clear: no " + Describe(Kind) + " is marked.";
        }

        var parts = new List<string>();
        foreach (Designation gone in Removed)
        {
            parts.Add(Describe(gone.Kind));
        }

        string text = "Clearing " + string.Join(", ", parts.ToArray());

        if (OrdersToCancel.Count > 0)
        {
            text += string.Format(
                CultureInfo.InvariantCulture,
                ". This cancels {0} unfinished order(s)", OrdersToCancel.Count);

            IReadOnlyDictionary<string, int> returned = Totals();
            if (returned.Count > 0)
            {
                var stacks = new List<string>();
                foreach (KeyValuePair<string, int> stack in returned)
                {
                    stacks.Add(stack.Value.ToString(CultureInfo.InvariantCulture) + " " + stack.Key);
                }

                text += " and returns " + string.Join(", ", stacks.ToArray());
            }
        }

        return text + ".";
    }

    /// <summary>Everything the refunds add up to, per item.</summary>
    public IReadOnlyDictionary<string, int> Totals()
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Reservation reservation in ToRefund)
        {
            foreach (MaterialStack stack in reservation.Stacks)
            {
                totals.TryGetValue(stack.Item, out int running);
                totals[stack.Item] = running + stack.Count;
            }
        }

        return totals;
    }

    private static string Describe(DesignationKind kind)
    {
        switch (kind)
        {
            case DesignationKind.SettlementArea: return "the settlement area";
            case DesignationKind.HarvestArea: return "the harvest area";
            case DesignationKind.SupplyContainer: return "the supply container";
            default: return "nothing";
        }
    }

    public override string ToString() => Describe();
}
