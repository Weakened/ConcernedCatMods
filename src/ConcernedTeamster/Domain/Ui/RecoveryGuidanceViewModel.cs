using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Ui;

/// <summary>Immutable content of the recovery guidance panel (CT-014):
/// a title naming the diagnosis and numbered vanilla-legal steps. Advisory
/// text only — the guidance layer cannot move, unload, or alter anything by
/// construction (it references no adapter surface at all).</summary>
public sealed class RecoveryGuidanceViewModel
{
    public RecoveryGuidanceViewModel(bool hasGuidance, string title, IReadOnlyList<string> steps)
    {
        HasGuidance = hasGuidance;
        Title = title;
        Steps = steps;
    }

    /// <summary>False when there is no active diagnosis; the panel shows
    /// the title as an explanatory message instead of steps.</summary>
    public bool HasGuidance { get; }

    public string Title { get; }

    public IReadOnlyList<string> Steps { get; }
}
