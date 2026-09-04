namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>One fully-formatted manifest row (CT-007). Unknown weights show
/// "?" — a marker, never a number that could skew mental math.</summary>
public sealed class CargoRowViewModel
{
    public CargoRowViewModel(string name, string countText, string unitWeightText, string lineWeightText)
    {
        Name = name;
        CountText = countText;
        UnitWeightText = unitWeightText;
        LineWeightText = lineWeightText;
    }

    public string Name { get; }

    public string CountText { get; }

    public string UnitWeightText { get; }

    public string LineWeightText { get; }
}
