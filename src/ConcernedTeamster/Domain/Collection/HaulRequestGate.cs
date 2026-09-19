using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>What a player has allowed a helper to do with one container, in this
/// layer's own words. <b>Off is zero</b>, so a container nobody has spoken
/// about, an unparsable record and a defaulted value all mean the same thing: he
/// does not touch it.
///
/// The shared runtime has the same four states and the adapter maps onto them;
/// this exists because the domain layer of this product is compiled without the
/// library, which is what proves it game-free.</summary>
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

/// <summary>Why a manual haul was refused before anything was planned. Zero
/// means it was not.</summary>
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
    /// so no trip can be built.</summary>
    NoRoomAnywhere = 7,
}

/// <summary>One item a manual haul wants moved.</summary>
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

/// <summary>A manual haul that may be started, or the reason it may not.
/// </summary>
internal sealed class HaulRequest
{
    internal HaulRequest(ManifestRefusal refusal, string detail, IReadOnlyList<ManifestLine> wanted)
    {
        Refusal = refusal;
        Detail = detail ?? string.Empty;
        Wanted = wanted ?? Array.Empty<ManifestLine>();
    }

    public ManifestRefusal Refusal { get; }

    public string Detail { get; }

    public bool IsRefused => Refusal != ManifestRefusal.Unspecified;

    /// <summary>The whole job, as asked for: what to move, in a deterministic
    /// order. <b>How many trips that takes and what goes in each is not decided
    /// here</b> - that is the shared runtime's tour partitioner, working from
    /// the same carry capacity, and a second answer to it in this product would
    /// be one to keep in step forever.</summary>
    public IReadOnlyList<ManifestLine> Wanted { get; }

    public int Units
    {
        get
        {
            int total = 0;
            for (int index = 0; index < Wanted.Count; index++)
            {
                total += Wanted[index].Units;
            }

            return total;
        }
    }
}

/// <summary>Whether a manual chest-to-chest haul may be started at all: an
/// explicit source, an explicit destination, a requested cargo filter, and both
/// containers permitting the operation (#381).
///
/// <b>Both containers have to permit it, and off is the default.</b> A source
/// that only permits depositing refuses the take, honestly, saying which way
/// round it is - because the fix for that is the source's own setting, and a
/// player told only "it did not work" has been told nothing.</summary>
internal static class HaulRequestGate
{
    public static HaulRequest Check(
        ContainerUse source,
        ContainerUse destination,
        string sourceKey,
        string destinationKey,
        IReadOnlyList<ManifestLine>? available,
        IReadOnlyCollection<string>? filter,
        CarryBudget carry,
        CartCapacity cart)
    {
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

        List<ManifestLine> wanted = Filtered(available, filter);
        if (wanted.Count == 0)
        {
            return Refuse(ManifestRefusal.NothingMatchesTheFilter,
                "the source holds nothing that matches what was asked for");
        }

        if (carry.FreeKilograms <= 0f && cart.Units <= 0)
        {
            return Refuse(ManifestRefusal.NoRoomAnywhere,
                "he has no room on his back and no cart with room in it");
        }

        // Heaviest first, then by name: deterministic, and the order a trip
        // fills with the fewest items.
        wanted.Sort(CompareForLoading);
        return new HaulRequest(ManifestRefusal.Unspecified, string.Empty, wanted);
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

        HashSet<string>? wanted = filter == null ? null : new HashSet<string>(filter, StringComparer.Ordinal);
        for (int index = 0; index < available.Count; index++)
        {
            ManifestLine line = available[index];
            if (line.Units <= 0 || line.UnitKilograms <= 0f || string.IsNullOrEmpty(line.ItemPrefab))
            {
                // A line the source could not be read for is skipped rather
                // than guessed at: a weight of nothing would make the trip
                // arithmetic say everything fits.
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

    private static HaulRequest Refuse(ManifestRefusal refusal, string detail) =>
        new HaulRequest(refusal, detail, Array.Empty<ManifestLine>());
}
