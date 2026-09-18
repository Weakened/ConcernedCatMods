using System;
using System.Collections.Generic;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;

namespace TheConcernedCat.ConcernedSteward.Domain.Upkeep;

/// <summary>One fire, classified.</summary>
internal readonly struct FuelTargetVerdict
{
    public FuelTargetVerdict(FuelTargetObservation target, FuelTargetStatus status)
    {
        Target = target;
        Status = status;
    }

    public FuelTargetObservation Target { get; }

    public FuelTargetStatus Status { get; }

    public bool IsEligible => Status == FuelTargetStatus.Eligible;
}

/// <summary>What one scan found. Bounded by construction: the list can never be
/// longer than the limit that produced it.</summary>
internal sealed class FuelTargetScan
{
    internal FuelTargetScan(
        IReadOnlyList<FuelTargetVerdict> considered, int offered, int examined, bool truncated)
    {
        Considered = considered ?? Array.Empty<FuelTargetVerdict>();
        Offered = offered;
        Examined = examined;
        Truncated = truncated;
    }

    /// <summary>Every fire the scan classified, already in the order the
    /// Steward would take them.</summary>
    public IReadOnlyList<FuelTargetVerdict> Considered { get; }

    /// <summary>How many the adapter handed over.</summary>
    public int Offered { get; }

    /// <summary>How many were classified. Never more than
    /// <see cref="UpkeepLimits.MaxTargetsScanned"/>.</summary>
    public int Examined { get; }

    /// <summary>True when the settlement holds more fires than one scan looks
    /// at. Reported rather than hidden: a player with forty torches should be
    /// told the Steward is working through a subset, not left wondering.
    /// </summary>
    public bool Truncated { get; }

    /// <summary>The fire to tend next, or null when none is eligible.</summary>
    public FuelTargetObservation? Next
    {
        get
        {
            foreach (FuelTargetVerdict verdict in Considered)
            {
                if (verdict.IsEligible)
                {
                    return verdict.Target;
                }
            }

            return null;
        }
    }

    public int EligibleCount
    {
        get
        {
            int count = 0;
            foreach (FuelTargetVerdict verdict in Considered)
            {
                if (verdict.IsEligible)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>The verdict for one fire, or <see cref="FuelTargetStatus.Unavailable"/>
    /// when the scan did not examine it.</summary>
    public FuelTargetStatus StatusOf(FuelTargetKey key)
    {
        foreach (FuelTargetVerdict verdict in Considered)
        {
            if (verdict.Target.Key.Equals(key))
            {
                return verdict.Status;
            }
        }

        return FuelTargetStatus.Unavailable;
    }

    internal static FuelTargetScan Empty { get; } =
        new FuelTargetScan(Array.Empty<FuelTargetVerdict>(), 0, 0, false);
}

/// <summary>Chooses which fire to tend, deterministically and within a bound.
///
/// <b>What this deliberately is not.</b> It is not "the nearest fire", because
/// nearest to <i>what</i> would have to be the player, and a target that moves
/// when the player walks is a target nobody chose. It is not a global sweep of
/// the world either: the adapter is given the marked settlement area and hands
/// over what is inside it, and this layer re-checks that containment rather
/// than trusting it.
///
/// <b>The order, and why each tie-break exists.</b>
///
/// <list type="number">
/// <item><b>Emptiest first.</b> A steward's job is that no fire goes out. The
/// fire with the least fuel is the one with the least time left, whatever its
/// capacity, so it is served first.</item>
/// <item><b>Then closest to the depot.</b> Every trip starts at the depot, so
/// depot distance is the part of the walk this choice actually controls. The
/// depot is a designated fixed point, which is what keeps this deterministic —
/// ordering by distance to the <i>player</i> would reshuffle the queue every
/// time somebody walked across their own base.</item>
/// <item><b>Then the key, ordinally.</b> The last tie-break has to be total, or
/// two identical fires would swap places between scans depending on engine
/// enumeration order, and "deterministic" would be true of the sort and false
/// of the result.</item>
/// </list>
///
/// The bound is applied <i>after</i> the sort, never before: truncating the
/// engine's enumeration order would drop whichever fires the engine felt like
/// dropping, and the ones most in need would be exactly as likely to go as any
/// other.</summary>
internal static class FuelTargetSelector
{
    /// <summary>Classifies and orders the fires the adapter found.</summary>
    /// <param name="offered">What the adapter found inside the settlement.</param>
    /// <param name="settlement">The marked settlement area. Null means nothing
    /// is marked, and nothing marked means no fire is in scope — never "every
    /// fire".</param>
    /// <param name="depot">Where the supply depot stands, for the second
    /// tie-break.</param>
    /// <param name="stockedFuelNames">The fuel item names the depot actually
    /// stocks. A fire that burns something else is not a fault and not
    /// eligible.</param>
    /// <param name="epoch">The current world-load epoch. A key from any other
    /// one resolves to nothing.</param>
    public static FuelTargetScan Scan(
        IReadOnlyList<FuelTargetObservation>? offered,
        Designation? settlement,
        SitePoint depot,
        IReadOnlyCollection<string>? stockedFuelNames,
        string? epoch,
        UpkeepLimits limits)
    {
        if (offered == null || offered.Count == 0)
        {
            return FuelTargetScan.Empty;
        }

        var ordered = new List<FuelTargetObservation>(offered.Count);
        foreach (FuelTargetObservation candidate in offered)
        {
            ordered.Add(candidate);
        }

        ordered.Sort((left, right) => Compare(left, right, depot));

        int examined = Math.Min(ordered.Count, limits.MaxTargetsScanned);
        var verdicts = new List<FuelTargetVerdict>(examined);
        for (int index = 0; index < examined; index++)
        {
            FuelTargetObservation target = ordered[index];
            verdicts.Add(new FuelTargetVerdict(
                target, Classify(target, settlement, stockedFuelNames, epoch)));
        }

        return new FuelTargetScan(verdicts, offered.Count, examined, ordered.Count > examined);
    }

    /// <summary>Why one fire is or is not worth a trip.
    ///
    /// The order of these checks is the order of their cost and their
    /// consequence. Identity and ownership come first because they are the two
    /// that can cause real harm; "already fuelled" comes before anything that
    /// would spend wood; and containment is checked here even though the
    /// adapter was asked for fires inside the settlement, because a layer that
    /// trusts its adapter's filtering has no filtering a test can prove.
    /// </summary>
    public static FuelTargetStatus Classify(
        in FuelTargetObservation target,
        Designation? settlement,
        IReadOnlyCollection<string>? stockedFuelNames,
        string? epoch)
    {
        if (!target.Key.IsFrom(epoch))
        {
            return FuelTargetStatus.StaleIdentity;
        }

        if (!target.OwnedHere)
        {
            // The audit's section 3. On an unowned fire vanilla destroys the
            // item; on someone else's, the result is unobservable. Neither is a
            // thing to find out about afterwards.
            return FuelTargetStatus.NotOwnedHere;
        }

        if (!target.AccessGranted)
        {
            return FuelTargetStatus.AccessDenied;
        }

        if (settlement == null || !settlement.Contains(target.Position))
        {
            return FuelTargetStatus.OutsideSettlement;
        }

        if (target.InfiniteFuel)
        {
            return FuelTargetStatus.InfiniteFuel;
        }

        if (!target.CanRefill)
        {
            return FuelTargetStatus.CannotRefill;
        }

        if (string.IsNullOrEmpty(target.FuelItemName))
        {
            // The fire did not say what it burns. Vanilla's acceptance test is a
            // comparison against that name, so without it there is no way to
            // choose an item that would be accepted.
            return FuelTargetStatus.Unavailable;
        }

        if (stockedFuelNames == null || !Contains(stockedFuelNames, target.FuelItemName))
        {
            return FuelTargetStatus.WrongFuel;
        }

        if (!FuelMath.AcceptsOneUnit(target.Fuel, target.MaxFuel))
        {
            return FuelTargetStatus.AlreadyFuelled;
        }

        return FuelTargetStatus.Eligible;
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (string candidate in names)
        {
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int Compare(
        in FuelTargetObservation left, in FuelTargetObservation right, SitePoint depot)
    {
        int byFuel = left.Fuel.CompareTo(right.Fuel);
        if (byFuel != 0)
        {
            return byFuel;
        }

        int byDepot = depot.HorizontalDistanceTo(left.Position)
            .CompareTo(depot.HorizontalDistanceTo(right.Position));
        if (byDepot != 0)
        {
            return byDepot;
        }

        return string.CompareOrdinal(left.Key.Value, right.Key.Value);
    }
}
