namespace TheConcernedCat.ConcernedTeamster.Domain.Ui.Navigation;

/// <summary>One focusable element in a panel's navigation order (CT-031):
/// a stable id, a display label, and whether it is a button. Buttons-first
/// is a product rule — every feature must be reachable by a visible button,
/// with accelerators only as extra — so the navigation catalog records
/// <see cref="IsButton"/> and a test asserts every panel offers one.</summary>
public readonly struct FocusItem
{
    public FocusItem(string id, string label, bool isButton)
    {
        Id = id;
        Label = label;
        IsButton = isButton;
    }

    public string Id { get; }

    public string Label { get; }

    /// <summary>True when this element is a clickable button (the buttons-
    /// first surface), false for a read-only focusable such as a list row.</summary>
    public bool IsButton { get; }
}
