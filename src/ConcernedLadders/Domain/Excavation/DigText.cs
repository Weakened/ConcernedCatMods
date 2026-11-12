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

/// <summary>Every line the shovel shows (#551, owner playtest 2026-11-10: "a lot of warnings ... littering the screen,
/// too long error messages"). One short line each, never more than <see cref="MaximumLength"/> characters, saying what
/// to do next or the one reason it cannot; the detail goes to the log, never the screen. The buttons are on the game's
/// own key-hint panel, from the player's bindings, so a line names no key. English; game-free so every line is
/// tested.</summary>
internal static class DigText
{
    internal const int MaximumLength = 64;

    /// <summary>The prompt for a stage, with the size so far.</summary>
    internal static string Prompt(DigStage stage, DigFootprint footprint) => stage switch
    {
        DigStage.FirstCorner => "Aim at the ground and set a corner.",
        DigStage.SecondCorner => Size(footprint) + " m: set the opposite corner.",
        _ => Volume(footprint) + " m: scroll for depth, then dig.",
    };

    /// <summary>The line for a refused or idle plan, or a ready one that will flood; null when the prompt should show.</summary>
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
                DigRefusal.UndoLimit => "Too much at one spot: dig shallower here.",
                _ => "Cannot dig here.",
            };
        }

        return plan.Floods ? Volume(plan.Footprint) + " m: below water, it will flood." : null;
    }

    /// <summary>The line for a commit refusal. <paramref name="blocker"/> names what is in the way, where something is.</summary>
    internal static string Refusal(ExcavationCommitRefusal refusal, ExcavationReadFault fault, in DigPlan plan, string? blocker) => refusal switch
    {
        ExcavationCommitRefusal.None => string.Empty,
        ExcavationCommitRefusal.Disabled => "Digging is switched off.",
        ExcavationCommitRefusal.Unavailable => "Digging is unavailable this session.",
        ExcavationCommitRefusal.NotRegistered => "Digging is not ready yet.",
        ExcavationCommitRefusal.TerrainOpsDisabled => "The game has terrain editing switched off.",
        ExcavationCommitRefusal.Busy => "Still digging.",
        ExcavationCommitRefusal.TerrainUnreadable => ReadFault(fault),
        ExcavationCommitRefusal.PlanRefused => Plan(plan) ?? "Cannot dig here.",
        ExcavationCommitRefusal.NothingToDig => "Already dug this deep here.",
        ExcavationCommitRefusal.Protected => "A ward protects this ground.",
        ExcavationCommitRefusal.NoBuildArea => "You cannot dig here.",
        ExcavationCommitRefusal.Obstructed => InTheWay(blocker),
        ExcavationCommitRefusal.ScanOverflow => "Too much is in the way.",
        ExcavationCommitRefusal.SpawnRefused => "The game refused the dig here.",
        ExcavationCommitRefusal.NoShovel => "Hold the shovel to dig.",
        ExcavationCommitRefusal.NotReady => "Not now: finish what you are doing.",
        ExcavationCommitRefusal.ShovelWorn => "The shovel needs repair.",
        ExcavationCommitRefusal.NotEnoughStamina => "Too tired: rest first.",
        ExcavationCommitRefusal.OnLadder => "Not from a ladder.",
        ExcavationCommitRefusal.NotAimed => "Aim at the ground.",
        ExcavationCommitRefusal.NoPreview => "Hold the outline still, then dig.",
        ExcavationCommitRefusal.PreviewChanged => "The ground changed: check the outline.",
        _ => "Cannot dig here.",
    };

    internal static string ReadFault(ExcavationReadFault fault) => fault switch
    {
        ExcavationReadFault.Rebuilding => "The ground is still settling.",
        ExcavationReadFault.TilesDisagree => "The ground is still settling.",
        ExcavationReadFault.UnsupportedLattice => "This ground cannot be dug.",
        ExcavationReadFault.LegacyTerrainModifier => "An old terrain edit here blocks digging.",
        _ => "The ground here cannot be read.",
    };

    internal static string Aim(ExcavationAim aim, string? blocker) => aim switch
    {
        ExcavationAim.Blocked => InTheWay(blocker),
        ExcavationAim.TooFar => "Too far: move closer.",
        _ => "Aim at the ground.",
    };

    internal static string Dug(DigFootprint footprint) => "Dug " + Volume(footprint) + " m.";

    internal static string UndoArmed => "Press again to fill the last hole back in.";

    internal static string Undo(UndoRefusal refusal) => refusal switch
    {
        UndoRefusal.None => "Filled the last hole back in.",
        UndoRefusal.Nothing => "Nothing to fill back in.",
        UndoRefusal.Unloaded => "Too far away to fill it in: move closer.",
        UndoRefusal.Changed => "Cannot fill it in: the ground changed since.",
        UndoRefusal.PieceInside => "Cannot fill it in: something is in the hole.",
        UndoRefusal.Busy => "Still digging.",
        _ => "Cannot fill it in.",
    };

    /// <summary>How a batch ended, read back. Abandoned (the world went away) says nothing.</summary>
    internal static string Outcome(ExcavationOutcome outcome, DigFlightKind kind, DigFootprint footprint) => outcome switch
    {
        ExcavationOutcome.Complete => kind == DigFlightKind.Dig ? Dug(footprint) : Undo(UndoRefusal.None),
        ExcavationOutcome.Overshot => "The ground moved more than planned: see the log.",
        ExcavationOutcome.Unreadable => "Could not check the result: see the log.",
        ExcavationOutcome.TimedOut => "The change did not arrive in time: see the log.",
        _ => string.Empty,
    };

    /// <summary>The console's full description of a plan (<c>cl_excavate plan</c>): not a HUD line, so not held to
    /// <see cref="MaximumLength"/>.</summary>
    internal static string Detail(in DigPlan plan)
    {
        DigFootprint footprint = plan.Footprint;
        string where = Volume(footprint) + " m at (" + N(footprint.MinX) + ", " + N(footprint.MinZ) + ")-(" + N(footprint.MaxX) + ", " + N(footprint.MaxZ) + ")";
        if (plan.Status == ExcavationStatus.Refused) return where + ": refused, " + plan.Refusal + " at " + N(plan.RefusalVertices) + " point(s). " + (Plan(plan) ?? string.Empty);
        string text = where + ": floor at " + F(plan.Bottom) + " m (ground " + F(plan.Datum) + " m); lowers " + N(plan.LoweredVertices) +
                      " point(s), " + F(plan.LoweredVolume) + " m3, leaves " + N(plan.AlreadyLower) + " already lower; walls " +
                      F(plan.MinWallHeight) + " to " + F(footprint.Depth) + " m.";
        if (plan.Status == ExcavationStatus.NothingToDig) return text + " Nothing to dig.";
        text += " Costs " + F(DigCost.Stamina(plan.LoweredVolume)) + " stamina and " + F(DigCost.Durability(plan.LoweredVolume)) + " durability.";
        return plan.Floods ? text + " The floor is " + F(plan.WaterDepth) + " m below the water and will flood." : text;
    }

    private static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string InTheWay(string? blocker) => Clip(string.IsNullOrEmpty(blocker) ? "Something" : blocker!) + " is in the way.";

    private static string Size(DigFootprint footprint) => N(footprint.Width) + "x" + N(footprint.Length);

    private static string Volume(DigFootprint footprint) => Size(footprint) + "x" + N(footprint.Depth);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A blocker's name short enough that its line stays one short line.</summary>
    private static string Clip(string name) => name.Length <= 32 ? name : name.Substring(0, 31) + "…";
}
