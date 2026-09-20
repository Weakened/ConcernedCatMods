using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedNPC.Work;

namespace TheConcernedCat.ConcernedNPC.Planning;

/// <summary>One storage stop: a container, and exactly what is to be taken out
/// of it.</summary>
internal readonly struct SourceDraw
{
    private readonly StockLine[]? _take;

    internal SourceDraw(SourceStock source, IReadOnlyList<StockLine>? take)
    {
        Source = source;

        if (take == null || take.Count == 0)
        {
            _take = null;
        }
        else
        {
            var copy = new StockLine[take.Count];
            for (int index = 0; index < take.Count; index++)
            {
                copy[index] = take[index];
            }

            _take = copy;
        }
    }

    /// <summary>Which container.</summary>
    internal SourceStock Source { get; }

    /// <summary>What to take out of it, item by item. <b>Never more than the job
    /// needs</b>, which is a construction invariant of
    /// <see cref="SourceSelector"/> rather than something checked
    /// afterwards.</summary>
    internal IReadOnlyList<StockLine> Take => _take ?? Array.Empty<StockLine>();

    /// <summary>How many units in total.</summary>
    internal int Units
    {
        get
        {
            int total = 0;
            foreach (StockLine line in Take)
            {
                total += line.Units;
            }

            return total;
        }
    }

    internal string Key => Source.Key;

    internal NpcPoint Position => Source.Position;

    internal bool IsEmpty => _take == null;
}

/// <summary>Why a provisioning plan stopped before it had covered the manifest,
/// when it did.
///
/// <b>Both values mean "this is not what the world is short of".</b> A
/// truncated selection's shortfall is what is missing <i>given the stops it was
/// allowed</i>, and telling a player the settlement is short of thirteen wood
/// that is demonstrably sitting in chests nine to twelve is a sentence that
/// names the wrong fix. They are two values rather than one because the right
/// answer differs: one of them comes back better next tick and the other never
/// will.</summary>
internal enum SourceTruncation
{
    /// <summary>It finished. Whatever the shortfall says is about the world.
    /// </summary>
    None = 0,

    /// <summary>It stopped at <see cref="SourceSelector.MostStops"/> chests.
    /// <b>Deterministic</b>: asking again gives the identical answer, so this
    /// is never "ask again". What it means is that this trip is too big for one
    /// provisioning phase, and the trip has to get smaller.</summary>
    ChestCap = 1,

    /// <summary>The shared planning budget ran out mid-choice. The next call
    /// gets a fresh one and may well finish, so this <i>is</i> "ask
    /// again".</summary>
    BudgetSpent = 2,
}

/// <summary>Why a provisioning plan is what it is.</summary>
internal enum SourceSelectionOutcome
{
    /// <summary>Nobody chose.</summary>
    Unspecified = 0,

    /// <summary>The job consumes nothing, so there is nothing to fetch. <b>Not a
    /// failure</b>: collecting is a job with targets and no manifest, and
    /// refusing it for being unprovisioned would refuse the commonest job there
    /// is.</summary>
    NothingNeeded = 1,

    /// <summary>Every unit of the manifest is accounted for by the draws.</summary>
    Complete = 2,

    /// <summary>Some of it is. <see cref="SourcePlan.Shortfall"/> is the
    /// rest.</summary>
    Partial = 3,

    /// <summary>Nothing the job may use holds anything the job needs.</summary>
    NoUsableSource = 4,
}

/// <summary>Which chests to open, in what order, and what to take out of each.
/// </summary>
internal readonly struct SourcePlan
{
    private readonly SourceDraw[]? _draws;

    internal SourcePlan(
        SourceSelectionOutcome outcome,
        IReadOnlyList<SourceDraw>? draws,
        JobManifest shortfall,
        float travelMetres,
        SourceTruncation truncation)
    {
        Outcome = outcome;
        Shortfall = shortfall;
        TravelMetres = travelMetres;
        Truncation = truncation;

        if (draws == null || draws.Count == 0)
        {
            _draws = null;
        }
        else
        {
            var copy = new SourceDraw[draws.Count];
            for (int index = 0; index < draws.Count; index++)
            {
                copy[index] = draws[index];
            }

            _draws = copy;
        }
    }

    internal SourceSelectionOutcome Outcome { get; }

    /// <summary>The storage stops, in the order they should be walked.</summary>
    internal IReadOnlyList<SourceDraw> Draws => _draws ?? Array.Empty<SourceDraw>();

    /// <summary>What none of the usable containers can supply. Empty when the
    /// manifest is covered. <b>The sentence a player is told before the job
    /// starts</b>, which is the entire benefit of working the whole job out
    /// first.</summary>
    internal JobManifest Shortfall { get; }

    /// <summary>How far the provisioning walk is, from where the NPC stands
    /// through every chest.</summary>
    internal float TravelMetres { get; }

    /// <summary>Which of its own limits the search stopped on, if any.
    /// <b>Read before the shortfall is ever turned into a sentence</b>: a
    /// truncated plan's shortfall is what is missing <i>given the stops it was
    /// allowed</i>, not what the world is short of.</summary>
    internal SourceTruncation Truncation { get; }

    /// <summary>Whether the search stopped on its own limit rather than because
    /// it had finished.</summary>
    internal bool Truncated => Truncation != SourceTruncation.None;

    /// <summary>How many chests have to be opened.</summary>
    internal int Stops => Draws.Count;

    /// <summary>Whether the job can be provisioned at all.</summary>
    internal bool IsComplete =>
        Outcome == SourceSelectionOutcome.Complete || Outcome == SourceSelectionOutcome.NothingNeeded;
}

/// <summary>Choosing which containers a job draws from.
///
/// <b>The preference, in the owner's order.</b> Fewer storage stops, then
/// shorter travel, then satisfying more of the manifest, then less unnecessary
/// item movement. "One chest holding both materials beats two chests" is the
/// worked example, and it is exactly what the first criterion produces.
///
/// <b>How that order is read, and why it is not read literally.</b> Taken
/// literally as a comparison between whole plans it says a one-stop plan that
/// gets a tenth of the job beats a two-stop plan that gets all of it, which
/// nobody wants and which would strand an NPC halfway through a wall. Read as a
/// comparison between <i>candidates at each step of a greedy cover</i> - which
/// is what it is - it is exactly right, because in a greedy cover "covers more
/// of what is still needed" and "leaves fewer stops to make" are the same
/// criterion. So each round picks:
///
/// <i>First, a container that finishes the job on its own</i>, if there is one.
/// Zero further stops beats everything, and this is the rule that makes one
/// sufficient chest beat two.
///
/// <i>Then, the one covering the most kinds of what is still needed.</i> Kinds,
/// not units: a chest with the wood and the nails leaves one stop, a chest with
/// twice the wood and no nails leaves two.
///
/// <i>Then, the nearest</i> - from where the walk will actually be, not from
/// where it started, because the second chest is reached from the first.
///
/// <i>Then, the one covering the most units</i>, which is the manifest
/// criterion.
///
/// <i>Then, the container's own name, ordinally</i>, so that the same chests in
/// a different order give the same plan. Determinism is not a nicety here: a
/// plan that is a pure function of its inputs is what lets an interruption
/// compare a fresh plan against the one it was following and tell whether
/// anything actually changed.
///
/// <b>The fourth preference is a construction invariant, not a comparison.</b>
/// Nothing is ever taken that the job does not need - each draw is capped at
/// what is still outstanding - and no container is drawn from twice, so an item
/// is never split across two chests while one chest could supply it. There is
/// therefore no unnecessary item movement left for a tie-break to prefer away,
/// and a test asserts the invariant rather than the tie-break.
///
/// <b>Cost.</b> One pass over the containers per stop chosen, at most
/// <see cref="MostStops"/> stops: <c>O(MostStops * containers * items)</c>
/// integer work, no allocation per candidate, no probe and no navigation query.
/// Travel is straight-line distance, because asking the ground how far it really
/// is costs a query per pair and the answer would not change which chest is
/// nearest.
///
/// <b>What it never does.</b> It moves nothing. It reserves nothing. It reads
/// no inventory - the counts come from the snapshot, and the custody layer is
/// what actually withdraws and what records that a withdrawal may or may not
/// have happened.
///
/// <b>What it does ask, when it is given somewhere to ask.</b> Every count it
/// uses goes through <see cref="INpcSourceAvailability"/>, so units another job
/// has already set aside are not offered to this one. Without it the selector
/// plans against raw observations, and two jobs a tick apart will both plan the
/// same pile.</summary>
internal static class SourceSelector
{
    /// <summary>The most chests one provisioning phase will open. A round that
    /// needs more than this is not a round any more, and the NPC should come
    /// back for the rest rather than spend the evening opening chests.</summary>
    internal const int MostStops = 8;

    /// <summary>Works out the provisioning for one manifest.</summary>
    /// <param name="wanted">What has to be fetched.</param>
    /// <param name="sources">The containers the job may draw from. Each is asked
    /// whether it may be used now, not whether it could be earlier.</param>
    /// <param name="from">Where the NPC is standing.</param>
    /// <param name="budget">How much choosing is allowed. One unit per stop
    /// chosen.</param>
    internal static SourcePlan Select(
        JobManifest wanted,
        IReadOnlyList<SourceStock>? sources,
        NpcPoint from,
        PlanningBudget budget,
        INpcSourceAvailability? availability = null)
    {
        if (budget == null)
        {
            throw new ArgumentNullException(nameof(budget));
        }

        if (wanted.IsEmpty)
        {
            return new SourcePlan(
                SourceSelectionOutcome.NothingNeeded, null, JobManifest.Empty, 0f, SourceTruncation.None);
        }

        JobManifest remaining = wanted;
        NpcPoint cursor = from;
        float travel = 0f;
        SourceTruncation truncation = SourceTruncation.None;
        var draws = new List<SourceDraw>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        while (!remaining.IsEmpty)
        {
            if (draws.Count >= MostStops)
            {
                truncation = SourceTruncation.ChestCap;
                break;
            }

            if (!budget.TrySpend())
            {
                truncation = SourceTruncation.BudgetSpent;
                break;
            }

            if (!TryChoose(
                    remaining, sources, cursor, used, availability,
                    out SourceStock chosen, out List<StockLine> take))
            {
                break;
            }

            travel += cursor.HorizontalDistanceTo(chosen.Position);
            cursor = chosen.Position;
            used.Add(chosen.Key);
            draws.Add(new SourceDraw(chosen, take));
            remaining = ManifestArithmetic.Subtract(remaining, new JobManifest(AsRequirement(take)));
        }

        SourceSelectionOutcome outcome;
        if (remaining.IsEmpty)
        {
            outcome = SourceSelectionOutcome.Complete;
        }
        else if (draws.Count == 0)
        {
            outcome = SourceSelectionOutcome.NoUsableSource;
        }
        else
        {
            outcome = SourceSelectionOutcome.Partial;
        }

        return new SourcePlan(outcome, draws, remaining, travel, truncation);
    }

    /// <summary>One round of the greedy: the best container for what is still
    /// needed, and what to take from it.</summary>
    private static bool TryChoose(
        JobManifest remaining,
        IReadOnlyList<SourceStock>? sources,
        NpcPoint cursor,
        HashSet<string> used,
        INpcSourceAvailability? availability,
        out SourceStock chosen,
        out List<StockLine> take)
    {
        chosen = default;
        take = new List<StockLine>();

        if (sources == null)
        {
            return false;
        }

        bool found = false;
        bool bestFinishes = false;
        int bestKinds = 0;
        int bestUnits = 0;
        float bestDistance = 0f;
        List<StockLine>? bestTake = null;

        foreach (SourceStock source in sources)
        {
            if (!source.IsUsable || source.Key.Length == 0 || used.Contains(source.Key))
            {
                continue;
            }

            List<StockLine> offer = Offer(remaining, source, availability);
            if (offer.Count == 0)
            {
                continue;
            }

            int kinds = offer.Count;
            int units = 0;
            foreach (StockLine line in offer)
            {
                units += line.Units;
            }

            bool finishes = kinds == remaining.Lines.Count && units == remaining.TotalUnits;
            float distance = cursor.HorizontalDistanceTo(source.Position);

            if (!found || Beats(finishes, kinds, distance, units, source.Key,
                    bestFinishes, bestKinds, bestDistance, bestUnits, chosen.Key))
            {
                found = true;
                bestFinishes = finishes;
                bestKinds = kinds;
                bestUnits = units;
                bestDistance = distance;
                bestTake = offer;
                chosen = source;
            }
        }

        if (!found || bestTake == null)
        {
            return false;
        }

        take = bestTake;
        return true;
    }

    /// <summary>The preference, spelled out in the order it was stated. Every
    /// comparison is total and every tie falls through to the next, ending on
    /// the container's own name - so there is no input on which two containers
    /// are equally good and the answer depends on which was read first.</summary>
    private static bool Beats(
        bool finishes, int kinds, float distance, int units, string key,
        bool bestFinishes, int bestKinds, float bestDistance, int bestUnits, string bestKey)
    {
        if (finishes != bestFinishes)
        {
            return finishes;
        }

        if (kinds != bestKinds)
        {
            return kinds > bestKinds;
        }

        if (distance != bestDistance)
        {
            return distance < bestDistance;
        }

        if (units != bestUnits)
        {
            return units > bestUnits;
        }

        return string.CompareOrdinal(key, bestKey) < 0;
    }

    /// <summary>What one container can contribute to what is still needed,
    /// capped at the need. <b>The cap is the no-overcollection rule</b>: an NPC
    /// that took a chest's whole stack because it was there has moved a player's
    /// material for nothing and has to put it back.</summary>
    private static List<StockLine> Offer(
        JobManifest remaining, SourceStock source, INpcSourceAvailability? availability)
    {
        var offer = new List<StockLine>();
        foreach (JobManifestLine line in remaining.Lines)
        {
            int held = source.UnitsOf(line.Item, availability);
            if (held <= 0)
            {
                continue;
            }

            offer.Add(new StockLine(line.Item, held < line.Required ? held : line.Required));
        }

        return offer;
    }

    /// <summary>Stock taken becomes requirement covered. The two types are
    /// deliberately different - see <see cref="StockLine"/> - and this is the
    /// one place the conversion is allowed to happen, going this way
    /// only.</summary>
    private static List<JobManifestLine> AsRequirement(IReadOnlyList<StockLine> taken)
    {
        var lines = new List<JobManifestLine>(taken.Count);
        foreach (StockLine line in taken)
        {
            lines.Add(new JobManifestLine(line.Item, line.Units));
        }

        return lines;
    }
}
