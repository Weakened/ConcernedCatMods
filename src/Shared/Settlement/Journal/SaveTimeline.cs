using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Collection;

namespace TheConcernedCat.Settlement.Journal;

/// <summary>Where one journal row stands against the world saves the record
/// knows about.</summary>
internal enum RowStanding
{
    Unspecified = 0,

    /// <summary>Not a world effect (an acceptance, a person's answer, a marker)
    /// or written before schema v3 without a world time. Never voided.</summary>
    Record = 1,

    /// <summary>A world effect a later save in the live lineage contains.
    /// </summary>
    Saved = 2,

    /// <summary>A world effect after the last live save: real in this session,
    /// in no save yet.</summary>
    Unsaved = 3,

    /// <summary>The world was loaded from a save made before it, so the world
    /// rolled it back. Never credited, refunded or replayed.</summary>
    Voided = 4,

    /// <summary>Cannot be placed before or after the loaded save. Treated as
    /// uncertain: a person decides from the actual inventories.</summary>
    Ambiguous = 5,
}

/// <summary>A world load, for the one replay that decides what the load rolled
/// back.</summary>
internal readonly struct WorldLoad
{
    public WorldLoad(double loadedWorldTime, Guid loadEpoch)
    {
        if (double.IsNaN(loadedWorldTime) || double.IsInfinity(loadedWorldTime))
        {
            throw new ArgumentOutOfRangeException(nameof(loadedWorldTime), "A loaded world time is a real number.");
        }

        if (loadEpoch == Guid.Empty)
        {
            throw new ArgumentException("A world load has its own epoch.", nameof(loadEpoch));
        }

        LoadedWorldTime = loadedWorldTime;
        LoadEpoch = loadEpoch;
    }

    /// <summary><c>ZNet.GetTimeSeconds()</c> read as soon as the world is up,
    /// before any time can pass: the net time the save stored.</summary>
    public double LoadedWorldTime { get; }

    public Guid LoadEpoch { get; }
}

/// <summary>What the marker rule concluded.</summary>
internal sealed class SaveTimelineReport
{
    internal SaveTimelineReport(
        RowStanding[] standings, int topGeneration, int? restatementGeneration, double restatementTime,
        IReadOnlyList<string> problems, int voided, int ambiguous)
    {
        Standings = standings;
        TopGeneration = topGeneration;
        RestatementGeneration = restatementGeneration;
        RestatementTime = restatementTime;
        Problems = problems;
        VoidedCount = voided;
        AmbiguousCount = ambiguous;
    }

    /// <summary>One standing per journal entry, in entry order.</summary>
    public RowStanding[] Standings { get; }

    /// <summary>The generation of the live lineage's last save. The next save
    /// marker is this plus one.</summary>
    public int TopGeneration { get; }

    /// <summary>For a load: the generation the loaded world is, when the load
    /// changed what the record means and must be written down as a restatement.
    /// Null when nothing after the matched save needed deciding.</summary>
    public int? RestatementGeneration { get; }

    public double RestatementTime { get; }

    /// <summary>Marker rows that do not fit the lineage. Reported, never
    /// guessed around.</summary>
    public IReadOnlyList<string> Problems { get; }

    public int VoidedCount { get; }

    public int AmbiguousCount { get; }

    /// <summary>True when rows exist that no save has confirmed yet: the next
    /// world save must write a marker.</summary>
    public bool HasUnsaved
    {
        get
        {
            foreach (RowStanding standing in Standings)
            {
                if (standing == RowStanding.Unsaved)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>The world-save marker rule (CONTRACTS.md §5.5, PICKUP_SEAM_AUDIT.md
/// §7.3).
///
/// <b>The problem.</b> A journal row reaches disk the moment it is written; the
/// world effect it describes reaches disk only at the game's next save. After
/// any crash the journal is <i>expected</i> to be ahead of the loaded world, and
/// a reconciliation that did not know that would call every crash a mismatch.
///
/// <b>The markers.</b> At <c>ZNet.WorldSaveStarted</c> — invoked on the main
/// thread immediately before the save snapshot — the runtime appends
/// <c>WorldSaveMarker{generation = top + 1, world time}</c>. Custody mutations
/// run synchronously on the main thread too, so nothing can be written between
/// the marker and the snapshot: every world-effect row <i>before</i> a marker in
/// the record is in that save, and every row after it is not.
///
/// <b>The chain.</b> Saves form a lineage: generation 1, 2, 3… A marker whose
/// generation is one above the live chain's top is a <b>save</b>. A marker whose
/// generation is already in the chain is a <b>load restatement</b>: written
/// once, at load, when the loaded world is an older save than the record's top
/// or rows follow the matched save. It records the decision this class makes at
/// load, so every later replay makes the same one — without it, a crashed
/// session's rows would look confirmed as soon as a later session saved.
///
/// <b>The rule at load.</b> With the loaded world time <c>T</c>:
/// <list type="number">
/// <item>The matched save is the last chain marker whose time is ≤ T (none: the
/// world predates every marker, generation 0).</item>
/// <item>Every world-effect row after it is <see cref="RowStanding.Voided"/> when
/// its own time is after T, or when T is within
/// <see cref="SnapshotToleranceSeconds"/> of the matched marker (the loaded save
/// IS that marker's save, and the rows are after its snapshot).</item>
/// <item>Otherwise the loaded save is a later one the record has no marker for
/// — a marker write failed — and a row at or before T may be in it:
/// <see cref="RowStanding.Ambiguous"/>.</item>
/// <item>When net time did not advance between the matched marker and the one
/// before it, rows between the two are ambiguous as well: either save could be
/// the one loaded.</item>
/// </list>
///
/// Record-only rows — acceptances, transitions other than completion, a
/// person's answers, markers — are never voided: the world does not hold them.
/// Rows written before schema v3 carry no world time and are never voided
/// either.</summary>
internal static class SaveTimeline
{
    /// <summary>How long after its marker a save's stored net time may be. The
    /// save thread writes the chunks before it reads the net time, and the net
    /// time keeps advancing meanwhile; one minute is far longer than a chunked
    /// save takes and far shorter than any autosave interval.</summary>
    public const double SnapshotToleranceSeconds = 60.0;

    private readonly struct Link
    {
        public Link(int generation, double time, int index)
        {
            Generation = generation;
            Time = time;
            Index = index;
        }

        public int Generation { get; }

        public double Time { get; }

        public int Index { get; }
    }

    /// <summary>True for rows describing something the world holds.</summary>
    public static bool IsWorldEffect(JournalEntry entry)
    {
        if (entry == null || !entry.WorldTime.HasValue)
        {
            return false;
        }

        switch (entry.Kind)
        {
            case JournalEntryKind.Reserved:
            case JournalEntryKind.Refunded:
            case JournalEntryKind.CommitStarted:
            case JournalEntryKind.CommitFinished:
                return true;

            case JournalEntryKind.OrderTransition:
                return JournalEntryKinds.IsMaterialIntent(entry);

            case JournalEntryKind.PickupStarted:
            case JournalEntryKind.PickupFinished:
            case JournalEntryKind.TransferStarted:
            case JournalEntryKind.TransferFinished:
            case JournalEntryKind.CartBaselineRecorded:
            case JournalEntryKind.HandoverFinished:
            case JournalEntryKind.ToolHandoverStarted:
            case JournalEntryKind.ToolHandoverFinished:
            case JournalEntryKind.ToolReturned:
                return true;

            case JournalEntryKind.CollectionTransition:
                // Completion is concluded from delivery credit, which the world
                // holds; every other transition is a record of intent.
                return entry.Custody is CollectionTransitionRow transition
                    && transition.To == CollectionOrderState.Completed;

            default:
                return false;
        }
    }

    public static SaveTimelineReport Classify(IReadOnlyList<JournalEntry> entries, WorldLoad? load = null)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        var standings = new RowStanding[entries.Count];
        var chain = new List<Link>();
        var problems = new List<string>();

        for (int index = 0; index < entries.Count; index++)
        {
            JournalEntry entry = entries[index];

            if (entry.Kind == JournalEntryKind.WorldSaveMarker && entry.Custody is WorldSaveMarkerRow marker)
            {
                standings[index] = RowStanding.Record;
                double time = entry.WorldTime ?? double.NaN;
                int top = chain.Count == 0 ? 0 : chain[chain.Count - 1].Generation;

                if (marker.Generation == top + 1)
                {
                    ConfirmUnsaved(standings, chain.Count == 0 ? -1 : chain[chain.Count - 1].Index, index);
                    chain.Add(new Link(marker.Generation, time, index));
                }
                else if (marker.Generation <= top)
                {
                    ApplyLoad(entries, standings, chain, marker.Generation, time, index, problems);
                }
                else
                {
                    problems.Add(
                        "The record's world-save marker at line " + entry.Sequence.ToString(CultureInfo.InvariantCulture) +
                        " skips from save " + top.ToString(CultureInfo.InvariantCulture) + " to " +
                        marker.Generation.ToString(CultureInfo.InvariantCulture) + ". It is read as a save; " +
                        "if lines are missing, the record's closing line will say so too.");
                    ConfirmUnsaved(standings, chain.Count == 0 ? -1 : chain[chain.Count - 1].Index, index);
                    chain.Add(new Link(marker.Generation, time, index));
                }

                continue;
            }

            standings[index] = IsWorldEffect(entry) ? RowStanding.Unsaved : RowStanding.Record;
        }

        int? restatement = null;
        double restatementTime = 0.0;

        if (load.HasValue)
        {
            double loaded = load.Value.LoadedWorldTime;
            int matched = -1;
            for (int link = chain.Count - 1; link >= 0; link--)
            {
                if (chain[link].Time <= loaded)
                {
                    matched = link;
                    break;
                }
            }

            int matchedIndex = matched < 0 ? -1 : chain[matched].Index;
            bool rowsAfter = false;
            for (int index = matchedIndex + 1; index < entries.Count; index++)
            {
                if (standings[index] == RowStanding.Saved || standings[index] == RowStanding.Unsaved)
                {
                    rowsAfter = true;
                    break;
                }
            }

            bool olderThanTop = matched < chain.Count - 1;
            bool equalTimeBefore = matched > 0
                && chain[matched - 1].Time.Equals(chain[matched].Time)
                && HasSavedBetween(standings, chain[matched - 1].Index, chain[matched].Index);

            if (rowsAfter || olderThanTop || equalTimeBefore)
            {
                int generation = matched < 0 ? 0 : chain[matched].Generation;
                restatement = generation;
                restatementTime = loaded;
                ApplyLoad(entries, standings, chain, generation, loaded, entries.Count, problems);
            }
        }

        int voided = 0;
        int ambiguous = 0;
        foreach (RowStanding standing in standings)
        {
            if (standing == RowStanding.Voided)
            {
                voided++;
            }
            else if (standing == RowStanding.Ambiguous)
            {
                ambiguous++;
            }
        }

        return new SaveTimelineReport(
            standings,
            chain.Count == 0 ? 0 : chain[chain.Count - 1].Generation,
            restatement,
            restatementTime,
            problems,
            voided,
            ambiguous);
    }

    private static void ConfirmUnsaved(RowStanding[] standings, int afterIndex, int beforeIndex)
    {
        for (int index = afterIndex + 1; index < beforeIndex; index++)
        {
            if (standings[index] == RowStanding.Unsaved)
            {
                standings[index] = RowStanding.Saved;
            }
        }
    }

    private static bool HasSavedBetween(RowStanding[] standings, int afterIndex, int beforeIndex)
    {
        for (int index = afterIndex + 1; index < beforeIndex; index++)
        {
            if (standings[index] == RowStanding.Saved)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The world at world time <paramref name="loaded"/> is the save of
    /// <paramref name="generation"/>. Decides every world-effect row after that
    /// save, cuts the chain back to it, and puts the load itself in its place:
    /// rows written after this point in the record happened after the load, so
    /// a later load judges them against the loaded time, exactly as it would
    /// judge rows after a save.</summary>
    private static void ApplyLoad(
        IReadOnlyList<JournalEntry> entries, RowStanding[] standings, List<Link> chain,
        int generation, double loaded, int position, List<string> problems)
    {
        int link = -1;
        for (int candidate = chain.Count - 1; candidate >= 0; candidate--)
        {
            if (chain[candidate].Generation == generation)
            {
                link = candidate;
                break;
            }
        }

        if (generation > 0)
        {
            if (link < 0)
            {
                problems.Add(
                    "The record says the world was loaded from save " + generation.ToString(CultureInfo.InvariantCulture) +
                    ", which is not in its lineage. Everything not yet confirmed is treated as uncertain.");
                for (int index = 0; index < position; index++)
                {
                    if (standings[index] == RowStanding.Unsaved)
                    {
                        standings[index] = RowStanding.Ambiguous;
                    }
                }

                return;
            }
        }

        int baseIndex = link < 0 ? -1 : chain[link].Index;
        double baseTime = link < 0 ? double.NegativeInfinity : chain[link].Time;
        bool loadedIsBase = !double.IsInfinity(baseTime) && loaded - baseTime <= SnapshotToleranceSeconds;

        for (int index = baseIndex + 1; index < position; index++)
        {
            RowStanding standing = standings[index];
            if (standing != RowStanding.Saved && standing != RowStanding.Unsaved)
            {
                continue;
            }

            double rowTime = entries[index].WorldTime ?? double.NaN;
            standings[index] = rowTime > loaded || loadedIsBase ? RowStanding.Voided : RowStanding.Ambiguous;
        }

        // Net time that did not advance: the save before the matched one has
        // the same time, so either could be the one on disk.
        if (link > 0 && chain[link - 1].Time.Equals(baseTime))
        {
            for (int index = chain[link - 1].Index + 1; index < baseIndex; index++)
            {
                if (standings[index] == RowStanding.Saved)
                {
                    standings[index] = RowStanding.Ambiguous;
                }
            }
        }

        if (link < 0)
        {
            chain.Clear();
        }
        else
        {
            chain.RemoveRange(link, chain.Count - link);
        }

        chain.Add(new Link(generation, loaded, position));
    }
}
