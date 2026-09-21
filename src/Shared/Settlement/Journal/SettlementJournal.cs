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
/// completely different situation from never having tried.
///
/// The values are persisted as numbers, so they are never renumbered: new kinds
/// are appended (schema v3, CONTRACTS.md §5.4, from
/// <see cref="CollectionAccepted"/> on).</summary>
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

    /// <summary>The worker gave it back — or, when the entry says
    /// <see cref="JournalEntry.ReturnIntent"/>, is about to (schema v3): the
    /// intention written before the return moves anything, so an interrupted
    /// return is in the record exactly as an interrupted give is (#300).</summary>
    ToolReturned = 7,

    /// <summary>A person looked at an interrupted handover and said the worker
    /// really does have the tool.</summary>
    ToolResolvedToWorker = 8,

    /// <summary>The player really still has it.
    ///
    /// Usually a person's answer. In one case the handover writes it itself:
    /// the intention was recorded and the worker's inventory then refused the
    /// item, so nothing moved. Those entries say
    /// <see cref="JournalEntry.WrittenByHandover"/> (schema v3), and are read
    /// as "that attempt never happened" rather than as somebody having been
    /// asked (#300).
    ///
    /// Two kinds rather than one carrying a flag for the answer itself, because
    /// the answer IS the content of the entry and a boolean column would be one
    /// more thing a damaged row could get subtly wrong.</summary>
    ToolResolvedToPlayer = 9,

    /// <summary>A collection order was accepted: the full definition. Written
    /// before any work.</summary>
    CollectionAccepted = 10,

    /// <summary>A collection order changed state, with the reason.</summary>
    CollectionTransition = 11,

    /// <summary>About to pick a source. Written before the game's own pick.
    /// </summary>
    PickupStarted = 12,

    /// <summary>What the pick produced: the traced drops.</summary>
    PickupFinished = 13,

    /// <summary>A transfer's intent, written before any engine mutation.
    /// </summary>
    TransferStarted = 14,

    /// <summary>A transfer's receipt, with the actually accepted units.
    /// </summary>
    TransferFinished = 15,

    /// <summary>A person's answer to an uncertain transfer: which side is true.
    /// </summary>
    TransferResolved = 16,

    /// <summary>A cart's pre-existing cargo, before an order first used it.
    /// </summary>
    CartBaselineRecorded = 17,

    /// <summary>A person accepting observed loss.</summary>
    LossRecorded = 18,

    /// <summary>Hold-for-player: material handed to the player.</summary>
    HandoverFinished = 19,

    /// <summary>A world save's snapshot, or a load restating which save the
    /// world is (see <see cref="SaveTimeline"/>).</summary>
    WorldSaveMarker = 20,

    /// <summary>C2: a player-confirmed rebind of an order's scope snapshot and
    /// delivery target after a reload. Quotas, progress and custody unchanged.
    /// </summary>
    CollectionRebound = 21,
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

    /// <summary>The schema-v3 kinds, whose payload is a <see cref="CustodyRow"/>.
    /// </summary>
    public static bool IsCustody(JournalEntryKind kind)
    {
        switch (kind)
        {
            case JournalEntryKind.CollectionAccepted:
            case JournalEntryKind.CollectionTransition:
            case JournalEntryKind.PickupStarted:
            case JournalEntryKind.PickupFinished:
            case JournalEntryKind.TransferStarted:
            case JournalEntryKind.TransferFinished:
            case JournalEntryKind.TransferResolved:
            case JournalEntryKind.CartBaselineRecorded:
            case JournalEntryKind.LossRecorded:
            case JournalEntryKind.HandoverFinished:
            case JournalEntryKind.WorldSaveMarker:
            case JournalEntryKind.CollectionRebound:
                return true;

            default:
                return false;
        }
    }

    /// <summary>The placement-proof material kinds of schema v1.</summary>
    public static bool IsLegacyMaterial(JournalEntryKind kind)
    {
        switch (kind)
        {
            case JournalEntryKind.OrderTransition:
            case JournalEntryKind.Reserved:
            case JournalEntryKind.Refunded:
            case JournalEntryKind.CommitStarted:
            case JournalEntryKind.CommitFinished:
                return true;

            default:
                return false;
        }
    }

    /// <summary>Schema-v3 already stores a request, source and stacks on order
    /// transitions. The production reservation writer uses Reserve/Cancel with
    /// that payload as draw/refund intent. Legacy transitions may carry a
    /// request without any material payload or timestamp; those keep their
    /// original meaning, including after being saved as schema v3. Any partial
    /// payload or timestamp instead requires full intent validation.</summary>
    public static bool IsMaterialIntent(JournalEntry entry) =>
        entry.Kind == JournalEntryKind.OrderTransition && !entry.Request.IsEmpty &&
        (entry.Transition == OrderTransition.Reserve || entry.Transition == OrderTransition.Cancel) &&
        (entry.Container != null || entry.Stacks.Count > 0 || entry.ContainerEpoch != null || entry.WorldTime.HasValue);
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
        ToolSpecimen tool = default,
        string? containerEpoch = null,
        double? worldTime = null,
        Guid loadEpoch = default,
        bool returnIntent = false,
        bool writtenByHandover = false,
        string? note = null)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (JournalEntryKinds.IsCustody(kind))
        {
            throw new ArgumentException(
                "A custody entry is built from its payload; use the payload constructor.", nameof(kind));
        }

        if (!Enum.IsDefined(typeof(JournalEntryKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Not a kind this build defines.");
        }

        ValidateTime(worldTime, loadEpoch);

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

            if (request.IsEmpty)
            {
                // The reader treats a tool row without a request as damage, so
                // the writer must not be able to produce one. Guarding only the
                // reader left this build able to write a file it would later
                // call damaged and go permanently read-only over.
                throw new ArgumentException(
                    "A tool journal entry needs the transaction it is about.", nameof(request));
            }

            if (returnIntent && kind != JournalEntryKind.ToolReturned)
            {
                throw new ArgumentException("Only a return has an intention row.", nameof(returnIntent));
            }

            if (writtenByHandover
                && kind != JournalEntryKind.ToolResolvedToPlayer
                && kind != JournalEntryKind.ToolResolvedToWorker)
            {
                throw new ArgumentException(
                    "Only a settled answer is ever written by the handover itself.", nameof(writtenByHandover));
            }
        }
        else
        {
            if (order.IsEmpty)
            {
                throw new ArgumentException("A journal entry needs an owning order.", nameof(order));
            }

            if (returnIntent || writtenByHandover)
            {
                throw new ArgumentException("Tool flags belong to tool entries.");
            }

            if (SettlementJournal.CarriesRequest(kind) && request.IsEmpty)
            {
                // The same rule as the tool rows, applied to the material kinds
                // it was missing from: the reader calls such a row damage, so
                // the writer must not be able to produce one.
                throw new ArgumentException("This kind of entry needs its request.", nameof(request));
            }
        }

        Sequence = sequence;
        Kind = kind;
        Order = order;
        Request = request;
        Transition = transition;
        Container = container;
        ContainerEpoch = containerEpoch;
        Worker = worker;
        Tool = tool;
        WorldTime = worldTime;
        LoadEpoch = loadEpoch;
        ReturnIntent = returnIntent;
        WrittenByHandover = writtenByHandover;
        Note = note ?? string.Empty;

        if (stacks != null)
        {
            foreach (MaterialStack stack in stacks)
            {
                _stacks.Add(stack);
            }
        }

        if ((kind == JournalEntryKind.Reserved || JournalEntryKinds.IsMaterialIntent(this)) &&
            (string.IsNullOrEmpty(container) || _stacks.Count == 0))
        {
            // A reservation with no container or nothing in it cannot be
            // replayed, and the replay used to skip it without a word — the
            // silent drop #283's audit named. It cannot be written now.
            throw new ArgumentException("A reservation names its container and what it holds.", nameof(stacks));
        }
    }

    /// <summary>A schema-v3 custody entry.</summary>
    public JournalEntry(long sequence, CustodyRow payload, double worldTime, Guid loadEpoch)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        Custody = payload ?? throw new ArgumentNullException(nameof(payload));
        ValidateTime(worldTime, loadEpoch);

        if (loadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A custody entry records which world load wrote it.", nameof(loadEpoch));
        }

        if (payload.Kind != JournalEntryKind.WorldSaveMarker && payload.Order.IsEmpty)
        {
            throw new ArgumentException("A custody entry needs its order.", nameof(payload));
        }

        Sequence = sequence;
        Kind = payload.Kind;
        Order = payload.Order;
        Request = payload.Request;
        WorldTime = worldTime;
        LoadEpoch = loadEpoch;
        Note = string.Empty;
    }

    private static void ValidateTime(double? worldTime, Guid loadEpoch)
    {
        if (worldTime.HasValue && (double.IsNaN(worldTime.Value) || double.IsInfinity(worldTime.Value)))
        {
            throw new ArgumentOutOfRangeException(nameof(worldTime), "A world time is a real number.");
        }

        if (worldTime.HasValue != (loadEpoch != Guid.Empty))
        {
            // Both or neither: a time with no load is not placeable against a
            // save, and a load with no time is not either.
            throw new ArgumentException("A world time and a load epoch are recorded together.", nameof(loadEpoch));
        }
    }

    public long Sequence { get; }
    public JournalEntryKind Kind { get; }
    public OrderId Order { get; }
    public RequestId Request { get; }
    public OrderTransition Transition { get; }
    public string? Container { get; }
    public IReadOnlyList<MaterialStack> Stacks => _stacks;

    /// <summary>Which run of the world <see cref="Container"/> means anything
    /// in (#294). Null for rows written before schema v3, which match by key
    /// only.</summary>
    public string? ContainerEpoch { get; }

    /// <summary>Empty unless this is a tool entry.</summary>
    public WorkerId Worker { get; }

    /// <summary>Empty unless this is a tool entry.</summary>
    public ToolSpecimen Tool { get; }

    /// <summary>The payload of a schema-v3 custody entry; null otherwise.
    /// </summary>
    public CustodyRow? Custody { get; }

    /// <summary>The net world time when the row was written (schema v3). Null
    /// for rows written before v3, which the world-save marker rule never
    /// voids.</summary>
    public double? WorldTime { get; }

    /// <summary>The world load that wrote the row (schema v3).</summary>
    public Guid LoadEpoch { get; }

    /// <summary>On a <see cref="JournalEntryKind.ToolReturned"/> entry: the
    /// intention written before a return moves anything.</summary>
    public bool ReturnIntent { get; }

    /// <summary>On a <see cref="JournalEntryKind.ToolResolvedToPlayer"/> entry:
    /// written by the handover itself because nothing moved, not by a person.
    /// </summary>
    public bool WrittenByHandover { get; }

    /// <summary>Evidence carried by a tool entry (where a dying worker dropped
    /// a tool, for instance). Empty otherwise.</summary>
    public string Note { get; }

    public override string ToString()
    {
        return Sequence.ToString(CultureInfo.InvariantCulture) + " " + Kind +
            (Order.IsEmpty ? "" : " " + Order.Value) +
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
        : this(orders, ledger, tools, new MaterialCustodyLedger(), null, nextSequence, journalInstance, repairs)
    {
    }

    internal ReplayResult(
        IReadOnlyDictionary<string, OrderState> orders,
        CustodyLedger ledger,
        ToolLedger tools,
        MaterialCustodyLedger custody,
        SaveTimelineReport? timeline,
        long nextSequence,
        Guid journalInstance,
        IEnumerable<string> repairs,
        IEnumerable<string>? materialRepairs = null)
    {
        Orders = orders;
        Ledger = ledger;
        Tools = tools;
        Custody = custody;
        Timeline = timeline;
        NextSequence = nextSequence;
        JournalInstance = journalInstance;
        MaterialRepairs = new List<string>(materialRepairs ?? Array.Empty<string>());
        foreach (string repair in repairs)
        {
            _repairs.Add(repair);
        }
    }

    public IReadOnlyDictionary<string, OrderState> Orders { get; }

    public CustodyLedger Ledger { get; }

    public IReadOnlyList<string> MaterialRepairs { get; }

    /// <summary>Which real tools each worker is holding, rebuilt from the
    /// record. This is what makes the tool ledger's idempotence claim true
    /// across a reload rather than only within one session.</summary>
    public ToolLedger Tools { get; }

    /// <summary>Gathered material: collection orders, pickups, transfers and
    /// where every unit is (schema v3).</summary>
    public MaterialCustodyLedger Custody { get; }

    /// <summary>The world-save marker rule's classification of every row, or
    /// null for a record with nothing it applies to.</summary>
    public SaveTimelineReport? Timeline { get; }

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
/// and a clear message rather than a guess, and this is where that lives. The
/// same holds for every custody transfer and tool handover.</summary>
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
    /// came from: a newer schema, another settlement, lines it could not read,
    /// or a record whose closing line says it is incomplete.
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
    /// seeing. A refusal is also how a caller learns that a save it was told
    /// failed did in fact reach disk.</summary>
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

    /// <summary>Whether the entry at <paramref name="index"/> is known to have
    /// reached disk.</summary>
    public bool IsSaved(int index) => index >= 0 && index < _savedCount;

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
        ToolSpecimen tool = default,
        string? containerEpoch = null,
        double? worldTime = null,
        Guid loadEpoch = default,
        bool returnIntent = false,
        bool writtenByHandover = false,
        string? note = null)
    {
        var entry = new JournalEntry(
            NextSequence, kind, order, request, transition, container, stacks, worker, tool,
            containerEpoch, worldTime, loadEpoch, returnIntent, writtenByHandover, note);
        _entries.Add(entry);
        _highestSequence = entry.Sequence;
        IsDirty = true;
        return entry;
    }

    /// <summary>Appends one schema-v3 custody line.</summary>
    public JournalEntry AppendCustody(CustodyRow payload, double worldTime, Guid loadEpoch)
    {
        var entry = new JournalEntry(NextSequence, payload, worldTime, loadEpoch);
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
    /// <c>JournalStore</c> rejects any kind value <c>Enum.IsDefined</c> does not
    /// know, so the line is counted as damage and the journal goes
    /// read-only.</item>
    /// <item>A kind added <b>in code</b> and not classified below falls through
    /// to <c>true</c> — treated as carrying a request, so an entry with an empty
    /// one is refused rather than crashing the replay. Fail-safe, but silent,
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
            case JournalEntryKind.PickupStarted:
            case JournalEntryKind.PickupFinished:
            case JournalEntryKind.TransferStarted:
            case JournalEntryKind.TransferFinished:
            case JournalEntryKind.TransferResolved:
            case JournalEntryKind.LossRecorded:
            case JournalEntryKind.HandoverFinished:
                return true;

            case JournalEntryKind.OrderTransition:
            case JournalEntryKind.CollectionAccepted:
            case JournalEntryKind.CollectionTransition:
            case JournalEntryKind.CartBaselineRecorded:
            case JournalEntryKind.WorldSaveMarker:
            case JournalEntryKind.CollectionRebound:
                return false;
        }

        return true;
    }

    /// <summary>Rebuilds order states, both custody ledgers and the tool ledger
    /// from the record, as the record stands: rows after the last save are
    /// real in this session.</summary>
    public ReplayResult Replay() => JournalReplay.Run(this, load: null);

    /// <summary>The replay a world load makes: the same, plus the world-save
    /// marker rule's decision about what this load rolled back. The caller
    /// persists the restatement the result names before any other write.
    /// </summary>
    public ReplayResult Replay(WorldLoad load) => JournalReplay.Run(this, load);
}
