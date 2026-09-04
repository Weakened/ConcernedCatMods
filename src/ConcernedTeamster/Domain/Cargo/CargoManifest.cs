using System;
using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Cargo;

/// <summary>Immutable snapshot of a cart container's contents (CT-006).
/// Entries are copied and deterministically ordered at creation (known
/// weights first by descending line weight, then ordinal name, then id;
/// unknown-weight lines last), totals are computed once, and the source
/// collection can be mutated freely afterwards without affecting the
/// manifest. An empty manifest is a valid "empty cart" — distinct from no
/// manifest at all (no container).</summary>
public sealed class CargoManifest
{
    private static readonly CargoEntry[] NoEntries = Array.Empty<CargoEntry>();

    private CargoManifest(
        CargoEntry[] entries,
        float totalKnownWeight,
        int totalItemCount,
        int unknownWeightEntryCount,
        double captureTimeSeconds)
    {
        Entries = entries;
        TotalKnownWeight = totalKnownWeight;
        TotalItemCount = totalItemCount;
        UnknownWeightEntryCount = unknownWeightEntryCount;
        CaptureTimeSeconds = captureTimeSeconds;
    }

    public IReadOnlyList<CargoEntry> Entries { get; }

    /// <summary>Sum of the line weights of every weight-known entry.
    /// Unknown lines contribute nothing here and are surfaced through
    /// <see cref="UnknownWeightEntryCount"/> instead of skewing the sum.</summary>
    public float TotalKnownWeight { get; }

    /// <summary>Sum of entry counts (items, not stacks).</summary>
    public int TotalItemCount { get; }

    public int UnknownWeightEntryCount { get; }

    public bool HasUnknownWeights => UnknownWeightEntryCount > 0;

    public double CaptureTimeSeconds { get; }

    public static CargoManifest Create(IEnumerable<CargoEntry> entries, double captureTimeSeconds)
    {
        var copied = new List<CargoEntry>(entries ?? NoEntries);
        copied.Sort(CompareEntries);

        float totalKnownWeight = 0f;
        int totalItemCount = 0;
        int unknownWeightEntryCount = 0;
        foreach (CargoEntry entry in copied)
        {
            totalItemCount += entry.Count;
            if (entry.WeightKnown)
            {
                totalKnownWeight += entry.LineWeight;
            }
            else
            {
                unknownWeightEntryCount++;
            }
        }

        return new CargoManifest(
            copied.ToArray(),
            totalKnownWeight,
            totalItemCount,
            unknownWeightEntryCount,
            captureTimeSeconds);
    }

    public static CargoManifest CreateEmpty(double captureTimeSeconds)
    {
        return new CargoManifest(NoEntries, 0f, 0, 0, captureTimeSeconds);
    }

    private static int CompareEntries(CargoEntry left, CargoEntry right)
    {
        // Known-weight lines come before unknown ones.
        if (left.WeightKnown != right.WeightKnown)
        {
            return left.WeightKnown ? -1 : 1;
        }

        // Heaviest lines first — the panel's default "what is weighing my
        // cart down" order.
        int byWeight = right.LineWeight.CompareTo(left.LineWeight);
        if (byWeight != 0)
        {
            return byWeight;
        }

        int byName = string.CompareOrdinal(left.EffectiveDisplayName, right.EffectiveDisplayName);
        if (byName != 0)
        {
            return byName;
        }

        return string.CompareOrdinal(left.ItemId, right.ItemId);
    }
}
