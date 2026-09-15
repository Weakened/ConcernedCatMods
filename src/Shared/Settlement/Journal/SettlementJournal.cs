using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>What one journal line records.
///
/// <see cref="CommitStarted"/> and <see cref="CommitFinished"/> are two entries
/// rather than one on purpose, and they are the reason this type exists. A
/// commit does two things that cannot be made atomic against a game — consume
/// the reserved material and place the piece — so the journal records the
/// intention before and the outcome after. A replay that finds a start with no
/// finish therefore <i>knows</i> that something was in flight, which is a
/// completely different situation from never having tried.</summary>
internal enum JournalEntryKind
{
    OrderTransition = 0,
    Reserved = 1,
    Refunded = 2,

    /// <summary>About to consume and place. Written BEFORE either happens.</summary>
    CommitStarted = 3,

    /// <summary>Both happened. Written AFTER both.</summary>
    CommitFinished = 4,
}

/// <summary>One immutable line of the journal.</summary>
internal sealed class JournalEntry
{
    private readonly List<MaterialStack> _stacks = new List<MaterialStack>();

    public JournalEntry(
        long sequence,
        JournalEntryKind kind,
        OrderId order,
        RequestId request = default,
        OrderTransition transition = OrderTransition.Approve,
        string? container = null,
        IEnumerable<MaterialStack>? stacks = null)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (order.IsEmpty)
        {
            throw new ArgumentException("A journal entry needs an owning order.", nameof(order));
        }

        Sequence = sequence;
        Kind = kind;
        Order = order;
        Request = request;
        Transition = transition;
        Container = container;

        if (stacks != null)
        {
            foreach (MaterialStack stack in stacks)
            {
                _stacks.Add(stack);
            }
        }
    }

    public long Sequence { get; }
    public JournalEntryKind Kind { get; }
    public OrderId Order { get; }
    public RequestId Request { get; }
    public OrderTransition Transition { get; }
    public string? Container { get; }
    public IReadOnlyList<MaterialStack> Stacks => _stacks;

    public override string ToString()
    {
        return Sequence.ToString(CultureInfo.InvariantCulture) + " " + Kind + " " + Order.Value +
            (Request.IsEmpty ? "" : " " + Request.Value);
    }
}

/// <summary>What a replay concluded, including what it refused to conclude.</summary>
internal sealed class ReplayResult
{
    private readonly List<string> _repairs = new List<string>();

    internal ReplayResult(
        IReadOnlyDictionary<string, OrderState> orders,
        CustodyLedger ledger,
        long nextSequence,
        IEnumerable<string> repairs)
    {
        Orders = orders;
        Ledger = ledger;
        NextSequence = nextSequence;
        foreach (string repair in repairs)
        {
            _repairs.Add(repair);
        }
    }

    public IReadOnlyDictionary<string, OrderState> Orders { get; }

    public CustodyLedger Ledger { get; }

    public long NextSequence { get; }

    /// <summary>One actionable sentence per unresolved situation, naming the
    /// order and the request. Empty when everything reconciled.</summary>
    public IReadOnlyList<string> Repairs => _repairs;

    public bool NeedsRepair => _repairs.Count > 0;

    public OrderState StateOf(OrderId order)
    {
        return Orders.TryGetValue(order.Value, out OrderState state) ? state : OrderState.Draft;
    }
}

/// <summary>The append-only record of everything that moved, and the replay
/// that rebuilds state from it.
///
/// Two properties carry the whole design.
///
/// <b>Replay is idempotent.</b> Applying the journal twice produces exactly the
/// state applying it once produces, because every operation underneath is
/// keyed by a request id or guarded by a monotonic state machine. That is what
/// lets a crashed session simply re-read its own journal instead of having to
/// work out how far it got.
///
/// <b>Replay does not resolve.</b> A <see cref="JournalEntryKind.CommitStarted"/>
/// with no matching finish is reported as needing repair, with the order and
/// request named — it is never assumed to have succeeded, and never assumed to
/// have failed. Both assumptions are wrong in one direction each: one conjures
/// material, the other loses it. #273's gate 3 asks for a repairable journal
/// and a clear message rather than a guess, and this is where that lives.</summary>
internal sealed class SettlementJournal
{
    private readonly List<JournalEntry> _entries = new List<JournalEntry>();

    public SettlementJournal(SettlementScope scope)
    {
        if (!scope.IsComplete)
        {
            throw new ArgumentException(
                "A settlement journal needs a world and a settlement.", nameof(scope));
        }

        Scope = scope;
    }

    public SettlementScope Scope { get; }

    public IReadOnlyList<JournalEntry> Entries => _entries;

    public long NextSequence => _entries.Count == 0 ? 0 : _entries[_entries.Count - 1].Sequence + 1;

    public bool IsDirty { get; private set; }

    /// <summary>True when this build must not write over the file this journal
    /// came from: a newer schema, another settlement, or lines it could not
    /// read.
    ///
    /// The flag lives on the journal rather than beside it because a caller
    /// holding a journal must be able to ask whether writing it is allowed
    /// <i>without</i> also having kept the load report. CF-SET-004's review
    /// found exactly that gap: a read-only journal did not stop an act that
    /// wrote both the journal and a second file, so the second file recorded a
    /// change the record of which was refused.</summary>
    public bool IsReadOnly { get; private set; }

    internal void MarkReadOnly()
    {
        IsReadOnly = true;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    /// <summary>Appends one line. The sequence is assigned here so a caller
    /// cannot write two entries with the same number, which would make the
    /// order of a replay depend on list insertion rather than on the record.</summary>
    public JournalEntry Append(
        JournalEntryKind kind,
        OrderId order,
        RequestId request = default,
        OrderTransition transition = OrderTransition.Approve,
        string? container = null,
        IEnumerable<MaterialStack>? stacks = null)
    {
        var entry = new JournalEntry(NextSequence, kind, order, request, transition, container, stacks);
        _entries.Add(entry);
        IsDirty = true;
        return entry;
    }

    internal void Restore(JournalEntry entry)
    {
        _entries.Add(entry);
    }

    /// <summary>Rebuilds order states and the custody ledger from the record.</summary>
    public ReplayResult Replay()
    {
        var orders = new Dictionary<string, OrderState>(StringComparer.Ordinal);
        var ledger = new CustodyLedger();
        var startedCommits = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        var finishedCommits = new HashSet<string>(StringComparer.Ordinal);

        foreach (JournalEntry entry in _entries)
        {
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
                    if (!entry.Request.IsEmpty && entry.Container != null && entry.Stacks.Count > 0)
                    {
                        ledger.Reserve(new Reservation(
                            entry.Request, entry.Order, entry.Container, entry.Stacks));
                    }

                    break;

                case JournalEntryKind.Refunded:
                    // Guarded exactly as Reserved above is. A refund line whose
                    // request field is empty -- which a damaged or hand-edited
                    // file can produce, because the codec treats that field as
                    // optional -- would otherwise reach a dictionary lookup on a
                    // null key and take the whole replay down with it.
                    if (!entry.Request.IsEmpty)
                    {
                        ledger.Refund(entry.Request);
                    }

                    break;

                case JournalEntryKind.CommitStarted:
                    startedCommits[entry.Request.Value] = entry;
                    break;

                case JournalEntryKind.CommitFinished:
                    finishedCommits.Add(entry.Request.Value);
                    ledger.Commit(entry.Request);
                    break;
            }
        }

        // Anything that started and did not finish is genuinely unknown, and
        // stays unknown. Both available assumptions are wrong in one direction:
        // assuming success conjures a piece that may not exist, assuming
        // failure returns material that may already be a wall.
        var repairs = new List<string>();
        foreach (KeyValuePair<string, JournalEntry> pending in startedCommits)
        {
            if (finishedCommits.Contains(pending.Key))
            {
                continue;
            }

            JournalEntry entry = pending.Value;
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

        return new ReplayResult(orders, ledger, NextSequence, repairs);
    }
}
