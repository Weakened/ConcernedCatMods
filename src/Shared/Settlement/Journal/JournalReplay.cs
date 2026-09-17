using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Tools;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>The passes a replay runs, in order: the world-save marker rule,
/// the placement-proof material record, the tool handovers, then gathered
/// material custody.
///
/// Each pass reads the rows it owns and nothing else, and none of them resolves
/// anything: a row it cannot apply is a repair line a person can act on, never
/// a silent skip (#283's audit) and never a guess.</summary>
internal static class JournalReplay
{
    public static ReplayResult Run(SettlementJournal journal, WorldLoad? load)
    {
        IReadOnlyList<JournalEntry> entries = journal.Entries;
        SaveTimelineReport timeline = SaveTimeline.Classify(entries, load);
        var repairs = new List<string>();

        foreach (string problem in timeline.Problems)
        {
            repairs.Add(problem);
        }

        var orders = new Dictionary<string, OrderState>(StringComparer.Ordinal);
        var ledger = new CustodyLedger();
        ReplayMaterial(entries, orders, ledger, repairs);

        ToolLedger tools = ToolReplay.Run(entries, timeline.Standings, repairs);

        MaterialCustodyLedger custody = CustodyReplay.Run(entries, timeline.Standings, repairs);

        if (timeline.AmbiguousCount > 0)
        {
            repairs.Add(
                timeline.AmbiguousCount.ToString(CultureInfo.InvariantCulture) + " recorded change(s) could not be " +
                "placed before or after the world save that was loaded, because the record has no marker for that " +
                "save. They are treated as uncertain and nothing has been credited or taken back. Run " +
                "cf_settle reconcile to compare them with what the worker actually carries.");
        }

        return new ReplayResult(
            orders, ledger, tools, custody, timeline, journal.NextSequence, journal.Instance, repairs);
    }

    /// <summary>The placement proof's reservations and commits (schema v1).
    /// Unchanged in what it concludes; what changed is that a row it cannot
    /// apply now says so.</summary>
    private static void ReplayMaterial(
        IReadOnlyList<JournalEntry> entries, Dictionary<string, OrderState> orders, CustodyLedger ledger,
        List<string> repairs)
    {
        var startedCommits = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        var startedOrder = new List<string>();
        var finishedCommits = new HashSet<string>(StringComparer.Ordinal);

        foreach (JournalEntry entry in entries)
        {
            if (!JournalEntryKinds.IsLegacyMaterial(entry.Kind))
            {
                continue;
            }

            // The constructor refuses a request-carrying row with no request,
            // and the reader calls such a line damage, so this guard is now
            // unreachable from a file or from Append. It stays because a null
            // dictionary key takes the whole replay down, and a replay runs on
            // nearly every console command.
            if (SettlementJournal.CarriesRequest(entry.Kind) && entry.Request.IsEmpty)
            {
                continue;
            }

            switch (entry.Kind)
            {
                case JournalEntryKind.OrderTransition:
                {
                    OrderState current = orders.TryGetValue(entry.Order.Value, out OrderState existing)
                        ? existing
                        : OrderState.Draft;
                    OrderStateMachine.TryApply(current, entry.Transition, out OrderState next);
                    orders[entry.Order.Value] = next;
                    break;
                }

                case JournalEntryKind.Reserved:
                {
                    CustodyOutcome reserved = ledger.Reserve(new Reservation(
                        entry.Request, entry.Order, entry.Container!, entry.Stacks, entry.ContainerEpoch));
                    if (reserved == CustodyOutcome.Rejected)
                    {
                        repairs.Add(
                            "The record reserves material twice under request \"" + entry.Request.Value +
                            "\" (order \"" + entry.Order.Value + "\") with different contents. The first " +
                            "reservation is kept and the second is not applied; check which one is real.");
                    }

                    break;
                }

                case JournalEntryKind.Refunded:
                    if (ledger.Refund(entry.Request) == CustodyOutcome.Rejected)
                    {
                        repairs.Add(Unmatched(entry, "a refund"));
                    }

                    break;

                case JournalEntryKind.CommitStarted:
                    if (!startedCommits.ContainsKey(entry.Request.Value))
                    {
                        startedOrder.Add(entry.Request.Value);
                    }

                    startedCommits[entry.Request.Value] = entry;
                    break;

                case JournalEntryKind.CommitFinished:
                    finishedCommits.Add(entry.Request.Value);
                    if (ledger.Commit(entry.Request) == CustodyOutcome.Rejected)
                    {
                        repairs.Add(Unmatched(entry, "a finished commit"));
                    }

                    break;
            }
        }

        // Anything that started and did not finish is genuinely unknown, and
        // stays unknown. Both available assumptions are wrong in one direction:
        // assuming success conjures a piece that may not exist, assuming
        // failure returns material that may already be a wall.
        foreach (string key in startedOrder)
        {
            if (finishedCommits.Contains(key))
            {
                continue;
            }

            JournalEntry entry = startedCommits[key];
            ledger.MarkUncertain(entry.Request);

            OrderState current = orders.TryGetValue(entry.Order.Value, out OrderState existing)
                ? existing
                : OrderState.Draft;
            OrderStateMachine.TryApply(current, OrderTransition.FlagForRepair, out OrderState next);
            orders[entry.Order.Value] = next;

            repairs.Add(
                "Order \"" + entry.Order.Value + "\" was placing a piece (request \"" +
                entry.Request.Value + "\") when the session ended, and whether the materials became " +
                "part of the building is not recorded. The order is paused and nothing has been " +
                "consumed or returned. Check whether that piece is standing, then resolve the " +
                "request one way or the other.");
        }
    }

    private static string Unmatched(JournalEntry entry, string what)
    {
        return "The record has " + what + " for request \"" + entry.Request.Value + "\" (order \"" +
            entry.Order.Value + "\") that no held reservation matches — the reservation is missing, or was " +
            "already settled another way. Nothing was returned or spent because of that line; the record " +
            "may be missing lines, so check it before trusting the totals.";
    }
}

/// <summary>The tool-handover pass (#299, #300).
///
/// <b>One small state machine per handover, folded in record order.</b> The
/// previous replay collected rows into dictionaries and applied them in fixed
/// passes, because a return applied in file order before its holding existed was
/// silently dropped. That ordering problem came from creating holdings in a
/// post-pass; folding each request's own rows in sequence has no such hazard,
/// and it can express what the passes could not: an interrupted <i>return</i>
/// (#300 item 1), a second attempt under an id whose first attempt never moved
/// anything, and a row the world-save marker rule could not place.
///
/// Holdings are issued in the order their request first appears in the record,
/// so which axe a worker reaches for never depends on dictionary enumeration
/// (#300 item 3). A repair line is raised by looking at a holding's own state,
/// once (#300 item 4).</summary>
internal static class ToolReplay
{
    private enum Phase
    {
        Unspecified = 0,

        /// <summary>No holding: nothing recorded yet, or the attempt was
        /// abandoned by the handover itself because nothing moved.</summary>
        NoHolding = 1,

        GiveInFlight = 2,
        Held = 3,
        ReturnInFlight = 4,
        Returned = 5,

        /// <summary>A row the world-save marker rule could not place.</summary>
        Doubtful = 6,
    }

    private sealed class Fold
    {
        public Fold(JournalEntry first)
        {
            Request = first.Request;
            Worker = first.Worker;
            Tool = first.Tool;
            Phase = Phase.NoHolding;
        }

        public RequestId Request { get; }

        public WorkerId Worker { get; set; }

        public ToolSpecimen Tool { get; set; }

        public Phase Phase { get; set; }

        /// <summary>What was interrupted, for the repair sentence.</summary>
        public Phase InterruptedPhase { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    public static ToolLedger Run(IReadOnlyList<JournalEntry> entries, RowStanding[] standings, List<string> repairs)
    {
        var folds = new Dictionary<string, Fold>(StringComparer.Ordinal);
        var order = new List<string>();

        for (int index = 0; index < entries.Count; index++)
        {
            JournalEntry entry = entries[index];
            if (!JournalEntryKinds.IsTool(entry.Kind) || entry.Request.IsEmpty)
            {
                continue;
            }

            RowStanding standing = standings[index];
            if (standing == RowStanding.Voided)
            {
                continue;
            }

            if (!folds.TryGetValue(entry.Request.Value, out Fold? fold))
            {
                fold = new Fold(entry);
                folds.Add(entry.Request.Value, fold);
                order.Add(entry.Request.Value);
            }

            Apply(fold!, entry, standing == RowStanding.Ambiguous);
        }

        var ledger = new ToolLedger();
        foreach (string key in order)
        {
            Fold fold = folds[key];
            if (fold.Phase == Phase.NoHolding)
            {
                continue;
            }

            ledger.Issue(new ToolHolding(fold.Request, fold.Worker, fold.Tool));

            switch (fold.Phase)
            {
                case Phase.Returned:
                    ledger.Return(fold.Request);
                    break;

                case Phase.GiveInFlight:
                case Phase.ReturnInFlight:
                case Phase.Doubtful:
                    ledger.MarkUncertain(fold.Request);
                    repairs.Add(Describe(fold));
                    break;
            }
        }

        return ledger;
    }

    private static void Apply(Fold fold, JournalEntry entry, bool doubtful)
    {
        if (doubtful)
        {
            // Whatever this row says may not be in the world. Everything the
            // fold knew about the holding up to here is kept for the sentence,
            // and only a person's answer settles it.
            if (fold.Phase == Phase.NoHolding)
            {
                fold.Worker = entry.Worker;
                fold.Tool = entry.Tool;
            }

            if (fold.Phase != Phase.Doubtful)
            {
                fold.InterruptedPhase = fold.Phase == Phase.NoHolding ? Phase.GiveInFlight : fold.Phase;
            }

            fold.Phase = Phase.Doubtful;
            return;
        }

        switch (entry.Kind)
        {
            case JournalEntryKind.ToolHandoverStarted:
                // First start wins: a second start under the same id must not
                // quietly swap the tool. Only an attempt the handover itself
                // closed as "nothing moved" may be started again.
                if (fold.Phase == Phase.NoHolding)
                {
                    fold.Worker = entry.Worker;
                    fold.Tool = entry.Tool;
                    fold.Phase = Phase.GiveInFlight;
                }

                break;

            case JournalEntryKind.ToolHandoverFinished:
                if (fold.Phase == Phase.NoHolding)
                {
                    // A finish whose start did not survive is still evidence the
                    // tool moved: a finish is only written after it did.
                    fold.Worker = entry.Worker;
                    fold.Tool = entry.Tool;
                    fold.Phase = Phase.Held;
                }
                else if (fold.Phase == Phase.GiveInFlight)
                {
                    fold.Phase = Phase.Held;
                }

                break;

            case JournalEntryKind.ToolReturned:
                if (entry.ReturnIntent)
                {
                    if (fold.Phase == Phase.Held)
                    {
                        fold.Phase = Phase.ReturnInFlight;
                        fold.Note = entry.Note;
                    }
                }
                else if (fold.Phase == Phase.Held || fold.Phase == Phase.ReturnInFlight)
                {
                    fold.Phase = Phase.Returned;
                }

                break;

            case JournalEntryKind.ToolResolvedToWorker:
                if (IsWaiting(fold.Phase))
                {
                    fold.Phase = Phase.Held;
                }

                break;

            case JournalEntryKind.ToolResolvedToPlayer:
                if (entry.WrittenByHandover)
                {
                    // The handover recorded its intention and the worker's
                    // inventory then refused the item: that attempt never moved
                    // anything, so there is no holding to settle at all.
                    if (fold.Phase == Phase.GiveInFlight)
                    {
                        fold.Phase = Phase.NoHolding;
                    }
                }
                else if (IsWaiting(fold.Phase))
                {
                    fold.Phase = Phase.Returned;
                }

                break;
        }
    }

    private static bool IsWaiting(Phase phase) =>
        phase == Phase.GiveInFlight || phase == Phase.ReturnInFlight || phase == Phase.Doubtful;

    private static string Describe(Fold fold)
    {
        Phase what = fold.Phase == Phase.Doubtful ? fold.InterruptedPhase : fold.Phase;
        string action;
        switch (what)
        {
            case Phase.ReturnInFlight:
                action = "was being given back";
                break;

            case Phase.Held:
            case Phase.Returned:
                action = "changed hands in a part of the record the loaded world save may not contain";
                break;

            default:
                action = "was changing hands";
                break;
        }

        string evidence = string.IsNullOrEmpty(fold.Note) ? string.Empty : " (" + fold.Note + ")";

        return "A tool " + action + " (" + fold.Tool + ", worker \"" + fold.Worker.Value +
            "\", request \"" + fold.Request.Value + "\") when the session ended" + evidence +
            ", and whether it moved is not recorded. Nothing has been taken or given back. Check both " +
            "inventories, then run: cf_settle resolve " + fold.Request.Value + " mine|his";
    }
}

/// <summary>The gathered-material pass: collection orders, pickups, transfers,
/// baselines, losses and handovers, in record order, into the
/// <see cref="MaterialCustodyLedger"/>.
///
/// Voided rows are counted and never applied. Ambiguous rows make their
/// transfer or pickup uncertain. Whatever is still open at the end of the
/// record is uncertain too — a replay has no in-flight work.</summary>
internal static class CustodyReplay
{
    public static MaterialCustodyLedger Run(IReadOnlyList<JournalEntry> entries, RowStanding[] standings, List<string> repairs)
    {
        var ledger = new MaterialCustodyLedger();

        for (int index = 0; index < entries.Count; index++)
        {
            JournalEntry entry = entries[index];
            if (entry.Custody == null)
            {
                continue;
            }

            ledger.CountRecord();
            RowStanding standing = standings[index];
            bool ambiguous = standing == RowStanding.Ambiguous;

            switch (entry.Custody)
            {
                case CollectionAcceptedRow accepted:
                    Report(repairs, entry, ledger.Accept(accepted.Definition));
                    break;

                case CollectionTransitionRow transition:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    CustodyLedgerOutcome moved = ledger.Transition(
                        transition.Order, transition.From, transition.To, transition.Reason);
                    if (moved == CustodyLedgerOutcome.Rejected || moved == CustodyLedgerOutcome.Stale)
                    {
                        repairs.Add(
                            "Order \"" + transition.Order.Value + "\" is recorded moving from " + transition.From +
                            " to " + transition.To + ", which does not follow from where the record has it. " +
                            "That line was not applied.");
                    }

                    break;

                case PickupStartedRow started:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    Report(repairs, entry, ledger.BeginPickup(started.Pickup, started.Order, started.Source));
                    if (ambiguous)
                    {
                        ledger.MarkPickupUncertain(started.Pickup);
                    }

                    break;

                case PickupFinishedRow finished:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    Report(repairs, entry, ledger.FinishPickup(finished.Pickup, finished.Result, ambiguous));
                    break;

                case TransferStartedRow intent:
                    if (standing == RowStanding.Voided)
                    {
                        ledger.RegisterVoided(intent.Intent);
                        break;
                    }

                    Report(repairs, entry, ledger.Begin(intent.Intent, ambiguous));
                    break;

                case TransferFinishedRow receipt:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    Report(repairs, entry, ledger.Finish(receipt.Receipt, ambiguous));
                    break;

                case TransferResolvedRow resolved:
                {
                    CustodyLedgerOutcome answer = ledger.TryGetTransfer(resolved.Request, out TransferRecord _)
                        ? ledger.Resolve(resolved.Request, resolved.Side, resolved.Units)
                        : ledger.ResolvePickup(resolved.Request);
                    if (answer == CustodyLedgerOutcome.Rejected)
                    {
                        repairs.Add(
                            "A person's answer for request \"" + resolved.Request.Value + "\" was recorded, but " +
                            "that request is not waiting on one in the record as it now reads. It was not applied.");
                    }

                    break;
                }

                case CartBaselineRow baseline:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    Report(repairs, entry, ledger.RecordBaseline(
                        baseline.Order, baseline.LeaseId, baseline.CartSessionKey, baseline.ProviderEpoch, baseline.Counts));
                    break;

                case LossRecordedRow loss:
                    Report(repairs, entry, ledger.RecordLoss(loss.Request, loss.Order, loss.At, loss.Item, loss.Count));
                    break;

                case HandoverFinishedRow handover:
                    if (standing == RowStanding.Voided)
                    {
                        break;
                    }

                    Report(repairs, entry, ledger.RecordHandover(handover.Transfer, handover.Order, handover.Item, handover.Count));
                    break;

                case WorldSaveMarkerRow _:
                    break;
            }
        }

        ledger.CloseOpenIntents();

        foreach (TransferRecord transfer in ledger.Transfers)
        {
            if (transfer.AwaitsResolution)
            {
                repairs.Add(
                    "Moving " + transfer.Intent.Count.ToString(CultureInfo.InvariantCulture) + " " + transfer.Intent.Item +
                    " from " + transfer.Intent.From + " to " + transfer.Intent.To + " (order \"" + transfer.Order.Value +
                    "\", request \"" + transfer.Request.Value + "\") has no certain outcome: " + transfer.Evidence +
                    ". Nothing was credited, replayed or given back. Compare both places, then run: cf_settle resolve " +
                    transfer.Request.Value + " source|destination");
            }
        }

        foreach (PickupRecord pickup in ledger.Pickups)
        {
            if (pickup.IsUncertain)
            {
                repairs.Add(
                    "Picking " + pickup.Source + " for order \"" + pickup.Order.Value + "\" (request \"" +
                    pickup.Pickup.Value + "\") has no certain result, so nothing was credited from it. Any stone or " +
                    "branch it dropped is an ordinary item on the ground. Run: cf_settle resolve " +
                    pickup.Pickup.Value + " source");
            }
        }

        return ledger;
    }

    private static void Report(List<string> repairs, JournalEntry entry, CustodyLedgerOutcome outcome)
    {
        switch (outcome)
        {
            case CustodyLedgerOutcome.Applied:
            case CustodyLedgerOutcome.AlreadySatisfied:
                return;

            case CustodyLedgerOutcome.RejectedDifferentPayload:
                repairs.Add(
                    "Line " + entry.Sequence.ToString(CultureInfo.InvariantCulture) + " (" + entry.Kind + ") reuses " +
                    "an id the record already holds with different contents. The first is kept and this line was " +
                    "not applied.");
                return;

            default:
                repairs.Add(
                    "Line " + entry.Sequence.ToString(CultureInfo.InvariantCulture) + " (" + entry.Kind + ", order \"" +
                    entry.Order.Value + "\") does not fit the record as it reads — it refers to something that " +
                    "is not there, or asks for more than is held. It was not applied.");
                return;
        }
    }
}
