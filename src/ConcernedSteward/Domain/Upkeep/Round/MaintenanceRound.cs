using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.Settlement.Designations;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep.Round;

/// <summary>Why a round is what it is. <see cref="Unspecified"/> is zero, so a
/// round nobody prepared is never one to walk.</summary>
internal enum RoundVerdict
{
    /// <summary>Nobody prepared one.</summary>
    Unspecified = 0,

    /// <summary>A round with stops in it, and the material to pay for them.
    /// </summary>
    Prepared = 1,

    /// <summary>Every light in the settlement is above the threshold, switched
    /// off, or not hers to touch. Nothing to do, and that is an answer rather
    /// than a failure.</summary>
    NothingToDo = 2,

    /// <summary><b>No free fuel.</b> The round is understood and cannot be
    /// provisioned: the approved containers do not hold what it takes. Refused
    /// before a step, which is the whole benefit of totalling the round first —
    /// a player is told what is missing instead of watching four stops happen
    /// and the fifth not.</summary>
    ShortOfMaterial = 3,

    /// <summary>Nothing is approved to take from. Distinct from being short,
    /// because the fix is different: mark a chest rather than fill one.
    /// </summary>
    NoSupply = 4,

    /// <summary>No settlement is marked, so nothing is in scope. Nothing marked
    /// means nothing in scope, never everywhere.</summary>
    NoScope = 5,
}

/// <summary>One prepared maintenance round: what she found, what it takes, and
/// what she is short of.
///
/// <b>What this is and is not.</b> It is the Steward's half of a planned round —
/// which lights are stops, in what order of urgency, what each takes, what the
/// whole round takes, and which approved containers could pay for it. It is
/// <b>not</b> a route and it is not a batch: how many trips that becomes, which
/// chests are opened in which order, and what order the stops are walked in are
/// the shared planner's, because those are the same questions for a wall and a
/// woodpile and this product has no business answering them three times.
///
/// The two halves meet at a list of stops with a priority, a place and a
/// per-stop requirement, which is exactly the shape the shared planner's target
/// takes.</summary>
internal readonly struct RoundPlan
{
    private readonly LightNeed[]? _stops;
    private readonly LightNeed[]? _judged;
    private readonly SupplySighting[]? _sources;

    internal RoundPlan(
        RoundVerdict verdict,
        IReadOnlyList<LightNeed>? judged,
        IReadOnlyList<LightNeed>? stops,
        IReadOnlyList<SupplySighting>? sources,
        RoundManifest manifest,
        RoundManifest shortfall,
        string reason,
        int deferredForMaterial = 0)
    {
        Verdict = verdict;
        Manifest = manifest;
        Shortfall = shortfall;
        Reason = reason ?? string.Empty;
        DeferredForMaterial = deferredForMaterial < 0 ? 0 : deferredForMaterial;
        _judged = Copy(judged);
        _stops = Copy(stops);
        _sources = Copy(sources);
    }

    public RoundVerdict Verdict { get; }

    /// <summary>Every light that was looked at, with its verdict — including the
    /// ones that are not stops. Carried so a player asking "why is she not
    /// touching that one" gets an answer per light rather than a count.
    /// </summary>
    public IReadOnlyList<LightNeed> Judged => _judged ?? Array.Empty<LightNeed>();

    /// <summary>The stops, most urgent first.</summary>
    public IReadOnlyList<LightNeed> Stops => _stops ?? Array.Empty<LightNeed>();

    /// <summary>The approved containers, as they were seen.</summary>
    public IReadOnlyList<SupplySighting> Sources => _sources ?? Array.Empty<SupplySighting>();

    /// <summary><b>The whole round's requirement, computed once.</b></summary>
    public RoundManifest Manifest { get; }

    /// <summary>What nothing approved holds. Empty for a prepared round.
    /// </summary>
    public RoundManifest Shortfall { get; }

    /// <summary>Why it was refused, for a player sentence. Empty when prepared.
    /// </summary>
    public string Reason { get; }

    /// <summary>Lights that want fuel and are not in this round, because nothing
    /// she may take from holds enough of what they burn.
    ///
    /// <b>One of the two sources of "left for another round", and the Steward's
    /// own.</b> The other is the shared planner's, which counts lights a plan
    /// could not cover for reasons of capacity and chest limits. Both mean the
    /// same thing to a player and both have to be added before anything calls a
    /// round finished: a round that serviced everything it could pay for is not
    /// the same as a settlement that is looked after.</summary>
    public int DeferredForMaterial { get; }

    public bool IsActionable => Verdict == RoundVerdict.Prepared && Stops.Count > 0;

    /// <summary>How many times she has to open a chest for this round, if she
    /// can carry <paramref name="unitsPerTrip"/> units at a time.
    ///
    /// <b>The number #382 is actually about.</b> The loop this replaces fetched
    /// for one light at a time, so five low torches were five trips to the chest
    /// whatever she could carry. Here the requirement is a total, so the answer
    /// is the total divided by what she can carry — one trip for five torches
    /// that fit, and only more when they genuinely do not.
    ///
    /// Zero for a round with nothing to fetch, and zero for a capacity of
    /// nothing, which is a caller error rather than an infinite number of
    /// trips.</summary>
    public int StorageTripsNeeded(int unitsPerTrip)
    {
        int units = Manifest.TotalUnits;
        if (units <= 0 || unitsPerTrip <= 0)
        {
            return 0;
        }

        return ((units - 1) / unitsPerTrip) + 1;
    }

    /// <summary>One sentence about the whole round.</summary>
    public string Describe()
    {
        switch (Verdict)
        {
            case RoundVerdict.Prepared:
                return "A round of " + Stops.Count.ToString(CultureInfo.InvariantCulture) +
                    " light(s), needing " + Manifest.Describe() + "." +
                    (DeferredForMaterial > 0
                        ? " " + DeferredForMaterial.ToString(CultureInfo.InvariantCulture) +
                          " more will have to wait: she needs " + Shortfall.Describe() + "."
                        : string.Empty);

            case RoundVerdict.NothingToDo:
                return Reason.Length == 0
                    ? "Every light in the settlement has burning time left. Nothing to do."
                    : Reason;

            case RoundVerdict.ShortOfMaterial:
                return "She needs " + Shortfall.Describe() +
                    ", and nothing she may take from has any. She has not started the round.";

            case RoundVerdict.NoSupply:
                return "Nothing is marked for her to take fuel from. Look at the chest and run " +
                    "\"cs_steward depot\".";

            case RoundVerdict.NoScope:
                return "No settlement is marked, so there is nothing in her care.";

            default:
                return "No round was worked out, which is a bug — please report it.";
        }
    }

    private static T[]? Copy<T>(IReadOnlyList<T>? source)
    {
        if (source == null || source.Count == 0)
        {
            return null;
        }

        var copy = new T[source.Count];
        for (int index = 0; index < source.Count; index++)
        {
            copy[index] = source[index];
        }

        return copy;
    }
}

/// <summary>Working out one whole round before anybody takes a step.
///
/// <b>The shape this exists to replace, written out so nobody reintroduces
/// it.</b> Find the emptiest fire, walk to the chest, take what that one fire
/// needs, walk to the fire, feed it, walk back, and start again. Five low
/// torches are five walks to the chest under that loop, and every one of those
/// decisions is taken with the rest of the settlement unknown.
///
/// Here the survey is judged once, the requirement is totalled once, the
/// approved containers are asked once, and the whole thing is refused up front
/// if it cannot be paid for. What comes out is a list of stops the shared
/// planner can batch and route — which is what turns five walks into one.
/// </summary>
internal static class MaintenanceRound
{
    /// <summary>Prepares a round.</summary>
    /// <param name="lights">Everything the fire adapter found in the settlement.
    /// </param>
    /// <param name="settlement">The marked area, or null for nothing marked.
    /// </param>
    /// <param name="sources">Every container the player has approved, with what
    /// each was seen to hold.</param>
    /// <param name="epoch">The current world load.</param>
    /// <param name="thresholds">When a light is due, and how far it is filled.
    /// </param>
    internal static RoundPlan Prepare(
        IReadOnlyList<FuelTargetObservation>? lights,
        Designation? settlement,
        IReadOnlyList<SupplySighting>? sources,
        string? epoch,
        in MaintenanceThresholds thresholds)
    {
        if (settlement == null)
        {
            return Refuse(RoundVerdict.NoScope, string.Empty);
        }

        bool anySource = false;
        if (sources != null)
        {
            foreach (SupplySighting source in sources)
            {
                if (source.CanTake)
                {
                    anySource = true;
                    break;
                }
            }
        }

        if (!anySource)
        {
            return Refuse(RoundVerdict.NoSupply, string.Empty);
        }

        List<LightNeed> judged = LightNeeds.Assess(lights, settlement, epoch, thresholds);
        List<LightNeed> wanted = LightNeeds.StopsIn(judged);
        if (wanted.Count == 0)
        {
            return new RoundPlan(
                RoundVerdict.NothingToDo, judged, null, sources,
                RoundManifest.Empty, RoundManifest.Empty, DescribeNothingToDo(judged));
        }

        // What is free, per item, across every container she may TAKE from. A
        // container approved only for deposits is not a source, however full it
        // is.
        var free = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SupplySighting source in sources!)
        {
            if (!source.CanTake)
            {
                continue;
            }

            foreach (SupplyLine line in source.Lines)
            {
                free.TryGetValue(line.Item, out int running);
                free[line.Item] = running + line.Units;
            }
        }

        // Greedy, in the order she would walk them: the most urgent light that
        // can be paid for in full goes into this round, and one that cannot
        // waits for the next. Greedy rather than best-fit because "she saw to
        // the three that were about to go out and came back for the rest" is
        // something a player watches and understands, and "she saw to the
        // first, the fourth and the fifth" is not.
        //
        // A stop is taken WHOLE or not at all. Half-filling the most urgent
        // light and leaving nothing for the next one spends a player's wood on
        // the worst available distribution of it.
        var affordable = new List<LightNeed>();
        int deferred = 0;
        foreach (LightNeed stop in wanted)
        {
            free.TryGetValue(stop.Item, out int available);
            if (stop.Item.Length != 0 && available >= stop.Units)
            {
                free[stop.Item] = available - stop.Units;
                affordable.Add(stop);
            }
            else
            {
                deferred++;
            }
        }

        // Measured against what the WHOLE settlement wants, not against this
        // round: the sentence a player needs is "she is short of forty resin",
        // and a shortfall computed after the affordable stops were taken out
        // would always be empty.
        RoundManifest shortfall = RoundManifestArithmetic.Shortfall(
            RoundManifestArithmetic.Total(wanted), sources);

        if (affordable.Count == 0)
        {
            // Nothing she can pay for at all. Refused before a step, with the
            // material named: ten torches and no resin is "she needs forty
            // resin", never "nothing to tend".
            return new RoundPlan(
                RoundVerdict.ShortOfMaterial, judged, null, sources,
                RoundManifest.Empty, shortfall, string.Empty, deferred);
        }

        // The manifest is computed over the stops the round ACTUALLY covers,
        // never over the ones it started with. A round that claimed a total it
        // did not cover reconciles to finished with half the settlement dark,
        // and nothing anywhere would say so.
        return new RoundPlan(
            RoundVerdict.Prepared,
            judged,
            affordable,
            sources,
            RoundManifestArithmetic.Total(affordable),
            shortfall,
            string.Empty,
            deferred);
    }

    private static string DescribeNothingToDo(IReadOnlyList<LightNeed> judged)
    {
        if (judged.Count == 0)
        {
            return "There are no fuel-burning lights inside the marked settlement.";
        }

        int notDue = 0;
        int off = 0;
        int neverConsumes = 0;
        int notHers = 0;
        int wrongFuel = 0;
        foreach (LightNeed need in judged)
        {
            switch (need.Status)
            {
                case FuelTargetStatus.NotDue:
                case FuelTargetStatus.AlreadyFuelled: notDue++; break;
                case FuelTargetStatus.NotLit: off++; break;
                case FuelTargetStatus.NeverConsumes: neverConsumes++; break;
                case FuelTargetStatus.NotOwnedHere:
                case FuelTargetStatus.AccessDenied:
                case FuelTargetStatus.StaleIdentity: notHers++; break;
                case FuelTargetStatus.WrongFuel: wrongFuel++; break;
            }
        }

        var text = new System.Text.StringBuilder("Nothing to tend: ");
        text.Append(notDue.ToString(CultureInfo.InvariantCulture));
        text.Append(" of ");
        text.Append(judged.Count.ToString(CultureInfo.InvariantCulture));
        text.Append(" lights have burning time left");

        if (off > 0)
        {
            text.Append(", ");
            text.Append(off.ToString(CultureInfo.InvariantCulture));
            text.Append(" are switched off");
        }

        if (neverConsumes > 0)
        {
            text.Append(", ");
            text.Append(neverConsumes.ToString(CultureInfo.InvariantCulture));
            text.Append(" burn nothing");
        }

        if (wrongFuel > 0)
        {
            text.Append(", ");
            text.Append(wrongFuel.ToString(CultureInfo.InvariantCulture));
            text.Append(" burn something no approved chest stocks");
        }

        if (notHers > 0)
        {
            text.Append(", ");
            text.Append(notHers.ToString(CultureInfo.InvariantCulture));
            text.Append(" are not this session's to touch");
        }

        return text.Append('.').ToString();
    }

    private static RoundPlan Refuse(RoundVerdict verdict, string reason) =>
        new RoundPlan(verdict, null, null, null, RoundManifest.Empty, RoundManifest.Empty, reason);
}
