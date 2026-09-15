using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Custody;
using TheConcernedCat.Settlement.Identity;
using TheConcernedCat.Settlement.Orders;
using TheConcernedCat.Settlement.Tools;

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

    /// <summary>A real tool is about to change hands. Written BEFORE the item
    /// moves, for the same reason <see cref="CommitStarted"/> is: the two halves
    /// of a handover cannot be made atomic against a game, so a replay that
    /// finds a start with no finish <i>knows</i> something was in flight.</summary>
    ToolHandoverStarted = 5,

    /// <summary>The item moved and the worker has it. Written AFTER.</summary>
    ToolHandoverFinished = 6,

    /// <summary>The worker gave it back.</summary>
    ToolReturned = 7,

    /// <summary>A person looked at an interrupted handover and said the worker
    /// really does have the tool.</summary>
    ToolResolvedToWorker = 8,

    /// <summary>A person looked at an interrupted handover and said the player
    /// really still has it.
    ///
    /// Two kinds rather than one carrying a flag, because the answer IS the
    /// content of the entry and a boolean column would be one more thing a
    /// damaged row could get subtly wrong.</summary>
    ToolResolvedToPlayer = 9,
}

/// <summary>Which half of the record an entry belongs to.
///
/// Tool entries are keyed by a <b>worker</b>, material entries by an
/// <b>order</b>. Neither is a stand-in for the other, and an entry that carried
/// a synthetic order id so it could reuse the material shape would be a record
/// that reads wrongly forever after.</summary>
internal static class JournalEntryKinds
{
    public static bool IsTool(JournalEntryKind kind)
    {
        switch (kind)
        {
            case JournalEntryKind.ToolHandoverStarted:
            case JournalEntryKind.ToolHandoverFinished:
            case JournalEntryKind.ToolReturned:
            case JournalEntryKind.ToolResolvedToWorker:
            case JournalEntryKind.ToolResolvedToPlayer:
                return true;

            default:
                return false;
        }
    }
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
        IEnumerable<MaterialStack>? stacks = null,
        WorkerId worker = default,
        ToolSpecimen tool = default)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (JournalEntryKinds.IsTool(kind))
        {
            // A handover belongs to a WORKER, not to an order. Inventing an
            // order id so the entry could reuse the material shape would make
            // every later read of this record wrong about who was involved.
            if (worker.IsEmpty)
            {
                throw new ArgumentException(
                    "A tool journal entry needs the worker it is about.", nameof(worker));
            }

            if (tool.IsEmpty)
            {
                throw new ArgumentException(
                    "A tool journal entry needs the tool it is about.", nameof(tool));
            }

            if (!order.IsEmpty)
            {
                // "Refuses to pretend otherwise" has to mean this too. Accepting
                // an order and then dropping it on the way to disk would leave a
                // caller believing the record said something it never did.
                throw new ArgumentException(
                    "A tool journal entry belongs to a worker, not an order.", nameof(order));
            }
        }
        else if (order.IsEmpty)
        {
            throw new ArgumentException("A journal entry needs an owning order.", nameof(order));
        }

        Sequence = sequence;
        Kind = kind;
        Order = order;
        Request = request;
        Transition = transition;
        Container = container;
        Worker = worker;
        Tool = tool;

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

    /// <summary>Empty unless this is a tool entry.</summary>
    public WorkerId Worker { get; }

    /// <summary>Empty unless this is a tool entry.</summary>
    public ToolSpecimen Tool { get; }

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
        ToolLedger tools,
        long nextSequence,
        Guid journalInstance,
        IEnumerable<string> repairs)
    {
        Orders = orders;
        Ledger = ledger;
        Tools = tools;
        NextSequence = nextSequence;
        JournalInstance = journalInstance;
        foreach (string repair in repairs)
        {
            _repairs.Add(repair);
        }
    }

    public IReadOnlyDictionary<string, OrderState> Orders { get; }

    public CustodyLedger Ledger { get; }

    /// <summary>Which real tools each worker is holding, rebuilt from the
    /// record. This is what makes the tool ledger's idempotence claim true
    /// across a reload rather than only within one session.</summary>
    public ToolLedger Tools { get; }

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
        _savedCount = _entries.Count;
    }

    /// <summary>How many entries are known to have reached disk. Zero until a
    /// save succeeds, and never decreases — what is written is written.</summary>
    private int _savedCount;

    /// <summary>Drops trailing entries that never reached disk.
    ///
    /// For one case only, and it is a real one: an act appends its intention,
    /// tries to persist it, and the save fails. The act is refused and nothing
    /// in the world changed — but the entry is still sitting in this list, and
    /// some later unrelated save would carry it to disk as a record of
    /// something that never happened. For a handover that means a phantom
    /// unresolved transfer and a player hunting for an axe that never moved.
    ///
    /// <b>It can only ever remove what is not on disk.</b> A request to truncate
    /// below <see cref="_savedCount"/> is refused outright rather than
    /// clamped — un-writing a persisted entry is not something this type will
    /// do by arithmetic, and a caller asking for it has a bug worth
    /// seeing.</summary>
    public bool TryDiscardUnsaved(int keep)
    {
        if (keep < _savedCount || keep > _entries.Count)
        {
            return false;
        }

        if (keep == _entries.Count)
        {
            return true;
        }

        _entries.RemoveRange(keep, _entries.Count - keep);

        long highest = -1L;
        foreach (JournalEntry entry in _entries)
        {
            if (entry.Sequence > highest)
            {
                highest = entry.Sequence;
            }
        }

        _highestSequence = highest;
        IsDirty = _entries.Count != _savedCount;
        return true;
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
        IEnumerable<MaterialStack>? stacks = null,
        WorkerId worker = default,
        ToolSpecimen tool = default)
    {
        var entry = new JournalEntry(
            NextSequence, kind, order, request, transition, container, stacks, worker, tool);
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
            case JournalEntryKind.ToolHandoverStarted:
            case JournalEntryKind.ToolHandoverFinished:
            case JournalEntryKind.ToolReturned:
            case JournalEntryKind.ToolResolvedToWorker:
            case JournalEntryKind.ToolResolvedToPlayer:
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
        var tools = new ToolLedger();
        var startedCommits = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        var finishedCommits = new HashSet<string>(StringComparer.Ordinal);
        var startedHandovers = new Dictionary<string, JournalEntry>(StringComparer.Ordinal);
        var finishedHandovers = new HashSet<string>(StringComparer.Ordinal);
        var resolutions = new Dictionary<string, bool>(StringComparer.Ordinal);

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

                case JournalEntryKind.ToolHandoverStarted:
                    // First one wins. Last-write-wins would let a second start
                    // under the same id quietly replace the tool or the worker,
                    // bypassing the mismatch rejection ToolLedger.Issue makes a
                    // point of.
                    if (!startedHandovers.ContainsKey(entry.Request.Value))
                    {
                        startedHandovers[entry.Request.Value] = entry;
                    }

                    break;

                case JournalEntryKind.ToolHandoverFinished:
                    finishedHandovers.Add(entry.Request.Value);
                    tools.Issue(new ToolHolding(entry.Request, entry.Worker, entry.Tool));
                    break;

                case JournalEntryKind.ToolReturned:
                    tools.Return(entry.Request);
                    break;

                case JournalEntryKind.ToolResolvedToWorker:
                case JournalEntryKind.ToolResolvedToPlayer:
                    resolutions[entry.Request.Value] =
                        entry.Kind == JournalEntryKind.ToolResolvedToWorker;
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

        // A handover that started and did not finish is genuinely unknown, and
        // stays unknown -- the same rule as an interrupted material commit, for
        // the same reason. Assuming it completed hands a worker a tool the
        // player may still be holding; assuming it did not loses the record of
        // one they are not.
        foreach (KeyValuePair<string, JournalEntry> pending in startedHandovers)
        {
            if (finishedHandovers.Contains(pending.Key))
            {
                continue;
            }

            JournalEntry entry = pending.Value;
            tools.Issue(new ToolHolding(entry.Request, entry.Worker, entry.Tool));
            tools.MarkUncertain(entry.Request);

            // A person may already have said which way it went. That answer is
            // part of the record, so replaying it here is what makes
            // "resolvable" durably true rather than true until the next load.
            if (resolutions.TryGetValue(entry.Request.Value, out bool workerHasIt))
            {
                tools.Resolve(entry.Request, workerHasIt);
                continue;
            }

            repairs.Add(
                "A tool was changing hands (" + entry.Tool + ", worker \"" + entry.Worker.Value +
                "\", request \"" + entry.Request.Value + "\") when the session ended, and whether " +
                "it moved is not recorded. Nothing has been taken or given back. Check both " +
                "inventories, then say which way it went.");
        }

        return new ReplayResult(orders, ledger, tools, NextSequence, Instance, repairs);
    }
}
