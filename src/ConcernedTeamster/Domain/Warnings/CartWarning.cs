using TheConcernedCat.ConcernedTeamster.Domain.Localization;

namespace TheConcernedCat.ConcernedTeamster.Domain.Warnings;

/// <summary>One immutable warning for one cart (CT-009). The composed line
/// carries the non-color cues — a symbol AND the level word — plus the
/// situation and the action, so no consumer can reduce it to color alone.</summary>
public sealed class CartWarning
{
    public CartWarning(string cartId, WarningLevel level, string situation, string action)
    {
        CartId = cartId;
        Level = level;
        Situation = situation;
        Action = action;
    }

    public string CartId { get; }

    public WarningLevel Level { get; }

    /// <summary>What is happening ("Steep climb (19%) with 220 mass").</summary>
    public string Situation { get; }

    /// <summary>What to do about it ("Lighten the load or find a shallower
    /// path.").</summary>
    public string Action { get; }

    /// <summary>The full display line: symbol + level word + situation +
    /// action. Symbols are ASCII-safe fallbacks rendered identically in the
    /// game font ("!" caution, "!!" danger).</summary>
    public string ComposeLine()
    {
        if (Level == WarningLevel.None)
        {
            return string.Empty;
        }

        string cue = TeamsterStrings.Get(
            Level == WarningLevel.Danger ? "warn.cueDanger" : "warn.cueCaution");
        return TeamsterStrings.Format("warn.line", cue, Situation, Action);
    }
}
