using System.Globalization;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>Where the player is in a dig (#551): aiming for the first corner, stretching to the second, or setting the
/// depth before digging.</summary>
internal enum DigStage
{
    FirstCorner = 0,
    SecondCorner = 1,
    Depth = 2,
}

/// <summary>Why the world refuses a dig, as the adapter's checks find it (#551). Every one fails closed.</summary>
internal enum DigWorldRefusal
{
    None = 0,
    Ward = 1,
    NoBuild = 2,
    Blocked = 3,
    Stamina = 4,
    Durability = 5,
    Busy = 6,
}

/// <summary>Every line the shovel shows (#551, owner playtest 2026-11-10: "a lot of warnings ... littering the screen,
/// too long error messages"). One short line each, never more than <see cref="MaximumLength"/> characters, saying what
/// to do next or the one reason it cannot; the detail goes to the log, not the screen. English; game-free so every line
/// is tested.</summary>
internal static class DigText
{
    internal const int MaximumLength = 64;

    /// <summary>The prompt for a stage, with the size so far.</summary>
    internal static string Prompt(DigStage stage, DigFootprint footprint) => stage switch
    {
        DigStage.FirstCorner => "Click to set a corner of the hole.",
        DigStage.SecondCorner => Size(footprint) + " m: click the opposite corner.",
        _ => Size(footprint) + "x" + N(footprint.Depth) + " m: scroll for depth, click to dig.",
    };

    /// <summary>The line for a plan, when it is not simply ready (null when the prompt should show).</summary>
    internal static string? Plan(in DigPlan plan)
    {
        if (plan.Status == ExcavationStatus.NothingToDig) return "Already dug this deep here.";
        if (plan.Status == ExcavationStatus.Refused)
        {
            return plan.Refusal switch
            {
                DigRefusal.Unloaded => "Too far away: move closer.",
                DigRefusal.NotFinite => "The ground here cannot be read.",
                DigRefusal.DigLimit => "Too deep: the game stops 8 m below natural ground.",
                _ => "Cannot dig here.",
            };
        }

        return plan.Floods ? Size(plan.Footprint) + "x" + N(plan.Footprint.Depth) + " m: below water, it will flood." : null;
    }

    internal static string World(DigWorldRefusal refusal, string? blocker = null) => refusal switch
    {
        DigWorldRefusal.Ward => "A ward protects this ground.",
        DigWorldRefusal.NoBuild => "You cannot dig here.",
        DigWorldRefusal.Blocked => Clip(string.IsNullOrEmpty(blocker) ? "Something" : blocker!) + " is in the way.",
        DigWorldRefusal.Stamina => "Too tired: rest first.",
        DigWorldRefusal.Durability => "The shovel needs repair.",
        DigWorldRefusal.Busy => "Still digging.",
        _ => string.Empty,
    };

    internal static string Dug(DigFootprint footprint) => "Dug " + Size(footprint) + "x" + N(footprint.Depth) + " m.";

    internal static string Undo(UndoRefusal refusal) => refusal switch
    {
        UndoRefusal.None => "Filled the last hole back in.",
        UndoRefusal.Nothing => "Nothing to undo.",
        UndoRefusal.Unloaded => "Too far away to undo: move closer.",
        UndoRefusal.Changed => "Cannot undo: the ground changed since.",
        UndoRefusal.PieceInside => "Cannot undo: something is built in the hole.",
        UndoRefusal.Busy => "Still digging.",
        _ => "Cannot undo.",
    };

    private static string Size(DigFootprint footprint) => N(footprint.Width) + "x" + N(footprint.Length);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A blocker's name short enough that its line stays one short line.</summary>
    private static string Clip(string name) => name.Length <= 32 ? name : name.Substring(0, 31) + "…";
}
