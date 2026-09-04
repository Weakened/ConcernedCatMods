using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Immutable, fully-formatted content of the cargo manifest panel
/// (CT-007). Rows are already sorted and filtered; totals always describe
/// the whole cart (the filter narrows the view, never the truth).</summary>
public sealed class CargoManifestViewModel
{
    public CargoManifestViewModel(
        CargoManifestState state,
        string message,
        IReadOnlyList<CargoRowViewModel> rows,
        string totalLine,
        string freshnessLine)
    {
        State = state;
        Message = message;
        Rows = rows;
        TotalLine = totalLine;
        FreshnessLine = freshnessLine;
    }

    public CargoManifestState State { get; }

    /// <summary>The whole-panel message for NoManifest/Empty/NoMatch;
    /// empty otherwise.</summary>
    public string Message { get; }

    public IReadOnlyList<CargoRowViewModel> Rows { get; }

    /// <summary>Whole-cart totals: known weight, unknown-line count when
    /// present, and item count. Empty for NoManifest.</summary>
    public string TotalLine { get; }

    /// <summary>Capture age; STALE-prefixed when old. Empty for NoManifest.</summary>
    public string FreshnessLine { get; }
}
