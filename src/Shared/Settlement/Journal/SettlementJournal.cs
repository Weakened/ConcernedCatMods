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
        Guid journalInstance,
        IEnumerable<string> repairs)
    {
        Orders = orders;
        Ledger = ledger;
        NextSequence = nextSequence;
        JournalInstance = journalInstance;
        foreach (string repair in repairs)
        {
            _repairs.Add(repair);
        }
    }

    public IReadOnlyDictionary<string, OrderState> Orders { get; }

    public CustodyLedger Ledger { get; }

    public long NextSequence { get; }

    /// <summary>The journal object this replay came from, so a plan built on it
    /// can refuse to be applied to a different one.</summary>
    public Guid JournalInstance { get; }

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

    /// <summary>Distinguishes one loaded journal from another with the same
    /// scope and the same length.
    ///
    /// A plan fingerprints the journal it was worked out against. Scope plus
    /// length is not enough — two journal objects for the same settlement can
    /// hold different entries and still agree on both — so each instance also
    /// carries an identity nothing else shares.</summary>
    public Guid Instance { get; } = Guid.NewGuid();

    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>One past the highest sequence in the record.
    ///
    /// The MAXIMUM, not the last entry's. Reading the last entry made the
    /// documented guarantee below false for any file whose rows were reordered:
    /// the next append would reuse a number already in use, and replay order
    /// would then depend on list insertion rather than on the record. That
    /// value is also the fingerprint a pending undesignation plan is checked
    /// against, so a repeat would let a stale plan through.</summary>
    public long NextSequence => _highestSequence + 1L;

    /// <summary>Tracked as entries arrive rather than scanned for. Append reads
    /// NextSequence on every call, so scanning would make building a journal
    /// quadratic in its own length.</summary>
    private long _highestSequence = -1L;

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
        _highestSequence = entry.Sequence;
        IsDirty = true;
        return entry;
    }

    internal void Restore(JournalEntry entry)
    {
        _entries.Add(entry);
        if (entry.Sequence > _highestSequence)
        {
            _highestSequence = entry.Sequence;
        }
    }

    /// <summary>True when this kind keys off a request id.
    ///
    /// <b>There is no compiler guarantee here, and an earlier version of this
    /// comment claimed there was.</b> This is a C# switch <i>statement</i> with
    /// a trailing <c>return</c>: adding a member to
    /// <see cref="JournalEntryKind"/> compiles cleanly and silently takes the
    /// fallback. Even a switch <i>expression</i> would only warn. The claim came
    /// from a review suggestion that was written down without being checked
    /// against the language — which is the same defect class this method exists
    /// to guard against, committed while fixing an instance of it.
    ///
    /// The actual contract, in two parts:
    ///
    /// <list type="bullet">
    /// <item>An <b>undefined</b> kind never reaches here from a file.
    /// <c>JournalStore.TryParseEntry</c> rejects any kind value
    /// <c>Enum.IsDefined</c> does not know, so the line is counted as damage and
    /// the journal goes read-only.</item>
    /// <item>A kind added <b>in code</b> and not classified below falls through
    /// to <c>true</c> — treated as carrying a request, so an entry with an empty
    /// one is skipped rather than crashing the replay. Fail-safe, but silent,
    /// which is why the mapping is pinned by a test that enumerates every member
    /// of the enum. Add a member without classifying it and that test fails.</item>
    /// </list></summary>
    internal static bool CarriesRequest(JournalEntryKind kind)
    {
        switch (kind)
        {
            case JournalEntryKind.Reserved:
            case JournalEntryKind.Refunded:
            case JournalEntryKind.CommitStarted:
            case JournalEntryKind.CommitFinished:
                return true;

            case JournalEntryKind.OrderTransition:
                return false;
        }

        return true;
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
            // Four of the five kinds key off a request id, and the codec treats
            // that field as optional for all of them -- so a damaged or
            // hand-edited line can carry an empty one. Every such line reaches
            // a dictionary keyed by RequestId.Value, which is null when the id
            // is default, and a null key takes the whole replay down.
            //
            // Guarding this once, here, is the fix. An earlier version guarded
            // Refunded, then CommitStarted, each time claiming the class was
            // closed; CommitFinished was open both times. A guard per case is a
            // guard somebody forgets.
            //
            // CarriesRequest is a positive list because it makes each existing
            // kind's classification readable and enumerable. It does NOT change
            // what an unclassified new kind answers -- that falls through to
            // true, exactly as "anything except OrderTransition" would have.
            // The only thing that forces a new kind to be classified on purpose
            // is a test that enumerates the enum; see CarriesRequest itself.
            if (CarriesRequest(entry.Kind) && entry.Request.IsEmpty)
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
                    // No request check here: the guard above owns that for
                    // every kind, and leaving a second one would make this the
                    // case whose regression test proves nothing.
                    if (entry.Container != null && entry.Stacks.Count > 0)
                    {
                        ledger.Reserve(new Reservation(
                            entry.Request, entry.Order, entry.Container, entry.Stacks));
                    }

                    break;

                case JournalEntryKind.Refunded:
                    ledger.Refund(entry.Request);
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

        return new ReplayResult(orders, ledger, NextSequence, Instance, repairs);
    }
}
