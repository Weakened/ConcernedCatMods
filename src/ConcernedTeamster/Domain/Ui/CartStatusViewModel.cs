namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Immutable, fully-formatted content of the Cart Status panel
/// (CT-005). Every string is composed headlessly in the presenter so tests
/// prove the exact text; the panel only assigns them to text rows. Empty
/// lines render as blank rows.</summary>
public sealed class CartStatusViewModel
{
    public CartStatusViewModel(
        CartStatusState state,
        string sourceLine,
        string massLine,
        string breakdownLine,
        string gradeLine,
        string surfaceLine,
        string pullLine,
        string freshnessLine,
        string selectedCartId)
    {
        State = state;
        SourceLine = sourceLine;
        MassLine = massLine;
        BreakdownLine = breakdownLine;
        GradeLine = gradeLine;
        SurfaceLine = surfaceLine;
        PullLine = pullLine;
        FreshnessLine = freshnessLine;
        SelectedCartId = selectedCartId;
    }

    public CartStatusState State { get; }

    /// <summary>Which cart the numbers describe ("Pulling this cart" /
    /// "Nearby cart"), or the whole-panel message for empty states.</summary>
    public string SourceLine { get; }

    public string MassLine { get; }

    public string BreakdownLine { get; }

    public string GradeLine { get; }

    public string SurfaceLine { get; }

    public string PullLine { get; }

    public string FreshnessLine { get; }

    /// <summary>Sticky-selection token the caller passes back on the next
    /// refresh; empty when nothing is selected.</summary>
    public string SelectedCartId { get; }
}
