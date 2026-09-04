using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedTeamster.Domain.Cargo;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Headless presenter for the cargo manifest panel (CT-007):
/// deterministic sorting (unknown-weight markers always last, then the
/// chosen column, ties broken by the manifest's canonical order), a
/// case-insensitive substring filter over the displayed names, and explicit
/// empty/no-match/stale states. Display names go through an optional
/// caller-supplied localizer; any localizer failure falls back to the raw
/// token per entry (cosmetic degradation, never a lost row).</summary>
public static class CargoManifestPresenter
{
    /// <summary>Capture older than this renders STALE. Three tracker
    /// refresh windows: one missed refresh is normal jitter, three means
    /// reads are failing.</summary>
    public const double StaleAfterSeconds = 3.0;

    public static CargoManifestViewModel Present(
        CargoManifest? manifest,
        CargoSortColumn sortColumn,
        bool sortDescending,
        string? filterText,
        double nowSeconds,
        Func<string, string>? localizeName)
    {
        if (manifest is null)
        {
            return new CargoManifestViewModel(
                CargoManifestState.NoManifest,
                "No cart container available.",
                Array.Empty<CargoRowViewModel>(),
                string.Empty,
                string.Empty);
        }

        string totalLine = ComposeTotalLine(manifest);
        double ageSeconds = nowSeconds - manifest.CaptureTimeSeconds;
        if (ageSeconds < 0d)
        {
            ageSeconds = 0d;
        }

        bool stale = ageSeconds > StaleAfterSeconds;
        string freshnessLine = (stale ? "STALE — captured " : "Captured ") +
            ageSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s ago";

        if (manifest.Entries.Count == 0)
        {
            return new CargoManifestViewModel(
                CargoManifestState.Empty, "Cart is empty.",
                Array.Empty<CargoRowViewModel>(), totalLine, freshnessLine);
        }

        List<DisplayEntry> displayEntries = BuildDisplayEntries(manifest, localizeName);

        string filter = filterText?.Trim() ?? string.Empty;
        if (filter.Length > 0)
        {
            displayEntries.RemoveAll(entry =>
                entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0);
            if (displayEntries.Count == 0)
            {
                return new CargoManifestViewModel(
                    CargoManifestState.NoMatch,
                    "No items match \"" + filter + "\".",
                    Array.Empty<CargoRowViewModel>(), totalLine, freshnessLine);
            }
        }

        SortEntries(displayEntries, sortColumn, sortDescending);

        var rows = new CargoRowViewModel[displayEntries.Count];
        for (int index = 0; index < displayEntries.Count; index++)
        {
            DisplayEntry entry = displayEntries[index];
            rows[index] = new CargoRowViewModel(
                entry.Name,
                entry.Entry.Count.ToString(CultureInfo.InvariantCulture),
                entry.Entry.WeightKnown
                    ? entry.Entry.UnitWeight.ToString("F1", CultureInfo.InvariantCulture)
                    : "?",
                entry.Entry.WeightKnown
                    ? entry.Entry.LineWeight.ToString("F1", CultureInfo.InvariantCulture)
                    : "?");
        }

        return new CargoManifestViewModel(
            stale ? CargoManifestState.Stale : CargoManifestState.Live,
            string.Empty, rows, totalLine, freshnessLine);
    }

    private readonly struct DisplayEntry
    {
        public DisplayEntry(CargoEntry entry, string name, int canonicalIndex)
        {
            Entry = entry;
            Name = name;
            CanonicalIndex = canonicalIndex;
        }

        public CargoEntry Entry { get; }

        public string Name { get; }

        public int CanonicalIndex { get; }
    }

    private static List<DisplayEntry> BuildDisplayEntries(
        CargoManifest manifest, Func<string, string>? localizeName)
    {
        var displayEntries = new List<DisplayEntry>(manifest.Entries.Count);
        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            CargoEntry entry = manifest.Entries[index];
            string name = entry.EffectiveDisplayName;
            if (localizeName is not null)
            {
                try
                {
                    string localized = localizeName(name);
                    if (!string.IsNullOrEmpty(localized))
                    {
                        name = localized;
                    }
                }
                catch
                {
                    // Raw token beats a lost row.
                }
            }

            displayEntries.Add(new DisplayEntry(entry, name, index));
        }

        return displayEntries;
    }

    private static void SortEntries(
        List<DisplayEntry> entries, CargoSortColumn column, bool descending)
    {
        entries.Sort((left, right) =>
        {
            // Unknown-weight markers stay last under every sort — they are
            // annotations, not data points.
            if (left.Entry.WeightKnown != right.Entry.WeightKnown)
            {
                return left.Entry.WeightKnown ? -1 : 1;
            }

            int comparison = column switch
            {
                CargoSortColumn.Name => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase),
                CargoSortColumn.Count => left.Entry.Count.CompareTo(right.Entry.Count),
                CargoSortColumn.UnitWeight => left.Entry.UnitWeight.CompareTo(right.Entry.UnitWeight),
                _ => left.Entry.LineWeight.CompareTo(right.Entry.LineWeight),
            };
            if (descending)
            {
                comparison = -comparison;
            }

            if (comparison != 0)
            {
                return comparison;
            }

            // Stable secondary order: the manifest's canonical position.
            return left.CanonicalIndex.CompareTo(right.CanonicalIndex);
        });
    }

    private static string ComposeTotalLine(CargoManifest manifest)
    {
        string total = "Total weight: " +
            manifest.TotalKnownWeight.ToString("F1", CultureInfo.InvariantCulture);
        if (manifest.HasUnknownWeights)
        {
            total += " (+" +
                manifest.UnknownWeightEntryCount.ToString(CultureInfo.InvariantCulture) +
                " unknown)";
        }

        return total + " · " +
            manifest.TotalItemCount.ToString(CultureInfo.InvariantCulture) + " items";
    }
}
