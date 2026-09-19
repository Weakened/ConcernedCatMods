using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What a player has allowed a helper to do with one container.
/// <b>Off is zero</b>, so a container nobody has spoken about, an unparsable
/// record and a defaulted value all mean the same thing: he does not touch it.
/// </summary>
[Flags]
internal enum ContainerUse
{
    /// <summary>Not enabled. The default, and the answer for every container in
    /// the world until a player says otherwise.</summary>
    Off = 0,

    /// <summary>He may take out of it.</summary>
    Take = 1,

    /// <summary>He may put into it.</summary>
    Deposit = 2,

    /// <summary>Both.</summary>
    Both = Take | Deposit,
}

/// <summary>Why a manual haul was refused. Zero means it was not.</summary>
internal enum ManifestRefusal
{
    Unspecified = 0,

    /// <summary>No source was named. There is no such thing as the nearest
    /// chest: a manual haul names its source.</summary>
    NoSource = 1,

    /// <summary>No destination was named.</summary>
    NoDestination = 2,

    /// <summary>Source and destination are the same container.</summary>
    SameContainer = 3,

    /// <summary>The source is not enabled for taking.</summary>
    SourceNotEnabledForTaking = 4,

    /// <summary>The destination is not enabled for depositing.</summary>
    DestinationNotEnabledForDepositing = 5,

    /// <summary>The requested filter matched nothing the source holds.</summary>
    NothingMatchesTheFilter = 6,

    /// <summary>He can carry nothing at all - no room on his back and no cart -
    /// so no tour can be built.</summary>
    NoRoomAnywhere = 7,
}

/// <summary>One item the manifest wants moved.</summary>
internal readonly struct ManifestLine
{
    public ManifestLine(string itemPrefab, int units, float unitKilograms)
    {
        ItemPrefab = itemPrefab ?? string.Empty;
        Units = units > 0 ? units : 0;
        UnitKilograms = unitKilograms > 0f && !float.IsNaN(unitKilograms) ? unitKilograms : 0f;
    }

    public string ItemPrefab { get; }

    public int Units { get; }

    public float UnitKilograms { get; }

    public float Kilograms => Units * UnitKilograms;

    public override string ToString() => ItemPrefab + " x" + Units;
}

/// <summary>One trip: provision at the source, travel, unload at the
/// destination, reconcile. The four phases are the vocabulary the shipped haul
/// executor already uses for a leg, so a tour maps onto one leg of it.</summary>
internal sealed class HaulTour
{
    internal HaulTour(int number, IReadOnlyList<ManifestLine> load, float kilograms, bool usesCart)
    {
        Number = number;
        Load = load ?? Array.Empty<ManifestLine>();
        Kilograms = kilograms;
        UsesCart = usesCart;
    }

    /// <summary>One-based, so a log line reads "tour 2 of 5".</summary>
    public int Number { get; }

    /// <summary>What to take out of the source, in loading order - heaviest
    /// first, then by item name, so the same manifest loads the same way twice
    /// and a half-finished tour is recognisable.</summary>
    public IReadOnlyList<ManifestLine> Load { get; }

    public float Kilograms { get; }

    /// <summary>Whether this tour needs the cart. A tour that fits on his back
    /// says so, because hitching a cart for two stones is a thing a player
    /// notices.</summary>
    public bool UsesCart { get; }

    public int Units
    {
        get
        {
            int total = 0;
            for (int index = 0; index < Load.Count; index++)
            {
                total += Load[index].Units;
            }

            return total;
        }
    }
}

/// <summary>A manual chest-to-chest haul, worked out before a step is taken.
/// </summary>
internal sealed class HaulManifestPlan
{
    internal HaulManifestPlan(
        ManifestRefusal refusal,
        string detail,
        IReadOnlyList<HaulTour> tours,
        IReadOnlyList<ManifestLine> wanted,
        int leftForAnotherRound)
    {
        Refusal = refusal;
        Detail = detail ?? string.Empty;
        Tours = tours ?? Array.Empty<HaulTour>();
        Wanted = wanted ?? Array.Empty<ManifestLine>();
        LeftForAnotherRound = leftForAnotherRound > 0 ? leftForAnotherRound : 0;
    }

    public ManifestRefusal Refusal { get; }

    public string Detail { get; }

    public bool IsRefused => Refusal != ManifestRefusal.Unspecified;

    /// <summary>Every tour, in order, worked out in advance.</summary>
    public IReadOnlyList<HaulTour> Tours { get; }

    /// <summary>The whole job, as asked for.</summary>
    public IReadOnlyList<ManifestLine> Wanted { get; }

    /// <summary>How many units the tours do <b>not</b> move. The same word the
    /// batch planner uses, for the same reason: a plan that covers part of a job
    /// must never read as one that covers all of it.</summary>
    public int LeftForAnotherRound { get; }

    public bool CoversTheWholeJob => !IsRefused && LeftForAnotherRound == 0;
}

/// <summary>Builds a manual chest-to-chest haul: an explicit source, an explicit
/// destination, a requested cargo filter, and both containers permitting the
/// operation (#381).
///
/// <b>Everything is worked out before he moves.</b> How much fits on his back,
/// how much fits in the cart, how many tours that makes and what goes in each
/// one, in what order. A tour is then provision, travel, unload, reconcile - and
/// a player who asks what he is doing gets "tour two of five" rather than "he is
/// walking somewhere".
///
/// <b>Both containers have to permit it, and off is the default.</b> A source
/// that only permits depositing refuses the take, honestly, saying which way
/// round it is - because the fix for that is the source's own setting, and a
/// player told only "it did not work" has been told nothing.</summary>
internal static class HaulManifestPlanner
{
    public static HaulManifestPlan Plan(
        ContainerUse source,
        ContainerUse destination,
        string sourceKey,
        string destinationKey,
        IReadOnlyList<ManifestLine>? available,
        IReadOnlyCollection<string>? filter,
        CarryBudget carry,
        CartCapacity cart,
        CollectionLimits limits)
    {
        if (limits == null)
        {
            throw new ArgumentNullException(nameof(limits));
        }

        if (string.IsNullOrEmpty(sourceKey))
        {
            return Refuse(ManifestRefusal.NoSource,
                "this haul names no container to take from, and there is no such thing as the nearest one");
        }

        if (string.IsNullOrEmpty(destinationKey))
        {
            return Refuse(ManifestRefusal.NoDestination,
                "this haul names no container to put things into, so he would have nowhere to go");
        }

        if (string.Equals(sourceKey, destinationKey, StringComparison.Ordinal))
        {
            return Refuse(ManifestRefusal.SameContainer,
                "the source and the destination are the same container");
        }

        if ((source & ContainerUse.Take) == 0)
        {
            return Refuse(ManifestRefusal.SourceNotEnabledForTaking,
                (source & ContainerUse.Deposit) != 0
                    ? "the source container is enabled for putting things in, not for taking them out"
                    : "the source container is not enabled for helpers at all");
        }

        if ((destination & ContainerUse.Deposit) == 0)
        {
            return Refuse(ManifestRefusal.DestinationNotEnabledForDepositing,
                (destination & ContainerUse.Take) != 0
                    ? "the destination container is enabled for taking things out, not for putting them in"
                    : "the destination container is not enabled for helpers at all");
        }

        var wanted = Filtered(available, filter);
        if (wanted.Count == 0)
        {
            return Refuse(ManifestRefusal.NothingMatchesTheFilter,
                "the source holds nothing that matches what was asked for");
        }

        float perTour = carry.FreeKilograms;
        int cartUnits = cart.Units;
        if (perTour <= 0f && cartUnits <= 0)
        {
            return Refuse(ManifestRefusal.NoRoomAnywhere,
                "he has no room on his back and no cart with room in it");
        }

        // Heaviest first, then by name: a deterministic loading order, and the
        // one that fills a tour with the fewest items.
        var queue = new List<ManifestLine>(wanted);
        queue.Sort(CompareForLoading);

        var tours = new List<HaulTour>();
        int outstanding = Units(queue);
        var remaining = new List<ManifestLine>(queue);

        while (remaining.Count > 0 && tours.Count < limits.MostToursPerManifest)
        {
            var load = new List<ManifestLine>();
            float weight = 0f;
            int cartRoom = cartUnits;
            bool usedCart = false;

            for (int index = 0; index < remaining.Count; index++)
            {
                ManifestLine line = remaining[index];
                if (line.Units <= 0 || line.UnitKilograms <= 0f)
                {
                    remaining.RemoveAt(index);
                    index--;
                    continue;
                }

                int onBack = 0;
                if (perTour > weight)
                {
                    onBack = (int)Math.Floor((perTour - weight) / line.UnitKilograms);
                    if (onBack < 0)
                    {
                        onBack = 0;
                    }
                }

                int take = onBack < line.Units ? onBack : line.Units;
                int overflow = line.Units - take;
                if (overflow > 0 && cartRoom > 0)
                {
                    int intoCart = overflow < cartRoom ? overflow : cartRoom;
                    cartRoom -= intoCart;
                    take += intoCart;
                    usedCart = usedCart || intoCart > 0;
                }

                if (take <= 0)
                {
                    continue;
                }

                load.Add(new ManifestLine(line.ItemPrefab, take, line.UnitKilograms));
                weight += take * line.UnitKilograms;
                int leftOfThisLine = line.Units - take;
                if (leftOfThisLine <= 0)
                {
                    remaining.RemoveAt(index);
                    index--;
                }
                else
                {
                    remaining[index] = new ManifestLine(line.ItemPrefab, leftOfThisLine, line.UnitKilograms);
                }
            }

            if (load.Count == 0)
            {
                // Nothing fits in a whole tour, and another tour would be the
                // same tour. Everything still outstanding is reported rather
                // than looped over.
                break;
            }

            load.Sort(CompareForLoading);
            tours.Add(new HaulTour(tours.Count + 1, load, weight, usedCart));
            outstanding -= Units(load);
        }

        return new HaulManifestPlan(
            ManifestRefusal.Unspecified,
            string.Empty,
            tours,
            wanted,
            outstanding);
    }

    private static int CompareForLoading(ManifestLine left, ManifestLine right)
    {
        int byWeight = right.UnitKilograms.CompareTo(left.UnitKilograms);
        return byWeight != 0 ? byWeight : string.CompareOrdinal(left.ItemPrefab, right.ItemPrefab);
    }

    private static List<ManifestLine> Filtered(
        IReadOnlyList<ManifestLine>? available,
        IReadOnlyCollection<string>? filter)
    {
        var kept = new List<ManifestLine>();
        if (available == null)
        {
            return kept;
        }

        var wanted = filter == null ? null : new HashSet<string>(filter, StringComparer.Ordinal);
        for (int index = 0; index < available.Count; index++)
        {
            ManifestLine line = available[index];
            if (line.Units <= 0 || line.UnitKilograms <= 0f || string.IsNullOrEmpty(line.ItemPrefab))
            {
                continue;
            }

            // A filter of nothing means everything the source holds; a filter
            // with entries means exactly those. An empty set is the second
            // case, not the first, so "take nothing" is a thing a player can
            // ask for and be given.
            if (wanted != null && !wanted.Contains(line.ItemPrefab))
            {
                continue;
            }

            kept.Add(line);
        }

        return kept;
    }

    private static int Units(IReadOnlyList<ManifestLine> lines)
    {
        int total = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            total += lines[index].Units;
        }

        return total;
    }

    private static HaulManifestPlan Refuse(ManifestRefusal refusal, string detail) =>
        new HaulManifestPlan(
            refusal, detail, Array.Empty<HaulTour>(), Array.Empty<ManifestLine>(), 0);
}
