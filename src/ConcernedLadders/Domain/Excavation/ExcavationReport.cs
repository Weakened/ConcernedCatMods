using System.Globalization;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>The words the console and the log use for a plan, a refusal and an outcome (#487 slice 2). Every refusal
/// says what would make it go through, because a refusal that does not is indistinguishable from a broken tool.
/// Metres, one decimal place for heights, invariant culture.</summary>
internal static class ExcavationReport
{
    internal static string ModeName(ExcavationMode mode)
    {
        ExcavationGeometry geometry = ExcavationGeometry.For(mode);
        return F0(geometry.UsableFloorSize) + "x" + F0(geometry.UsableFloorSize) + "x" + F0(geometry.Depth) + " m";
    }

    /// <summary>One line describing a plan of any status.</summary>
    internal static string Plan(in ExcavationPlan plan)
    {
        string head = ModeName(plan.Mode) + " at (" + plan.CentreX.ToString(CultureInfo.InvariantCulture) + ", " +
                      plan.CentreZ.ToString(CultureInfo.InvariantCulture) + ")";
        switch (plan.Status)
        {
            case ExcavationStatus.Refused:
                return head + ": refused. " + Refusal(plan.Refusal, plan.RefusalVertices);
            case ExcavationStatus.NothingToDig:
                return head + ": nothing to dig. The floor is already level at " + F1(plan.Bottom) + " m. Nothing is changed or charged.";
        }
        string text = head + ": ready. Floor " + F0(plan.Geometry.UsableFloorSize) + "x" + F0(plan.Geometry.UsableFloorSize) +
                      " m at " + F1(plan.Bottom) + " m (datum " + F1(plan.Datum) + " m, depth " + F0(plan.Geometry.Depth) +
                      " m); changed square " + F0(plan.Geometry.BorderSize) + "x" + F0(plan.Geometry.BorderSize) +
                      " m; lowers " + plan.LoweredVertices.ToString(CultureInfo.InvariantCulture) + " vertices by up to " +
                      F1(plan.MaxLowering) + " m; walls " + F1(plan.MinWallHeight) + " to " + F1(plan.MaxWallHeight) +
                      " m; " + plan.TileCount.ToString(CultureInfo.InvariantCulture) + (plan.TileCount == 1 ? " terrain tile." : " terrain tiles.");
        if (plan.Floods) text += " The floor would be " + F1(plan.WaterDepth) + " m below the water surface and will flood.";
        return text;
    }

    internal static string Refusal(ExcavationRefusal refusal, int vertices)
    {
        string count = vertices.ToString(CultureInfo.InvariantCulture) + (vertices == 1 ? " vertex" : " vertices");
        switch (refusal)
        {
            case ExcavationRefusal.Unloaded: return "Terrain under " + count + " is not loaded at full detail. Move closer and try again.";
            case ExcavationRefusal.NotFinite: return "Terrain under " + count + " could not be read reliably (or tiles disagree, or it sits at the dig limit). Nothing is changed.";
            case ExcavationRefusal.FloorAlreadyDeeper: return "Part of the floor (" + count + ") is already lower than this hole's floor. Lowering never raises ground; pick a spot where the floor can come out level.";
            case ExcavationRefusal.DigLimit: return "The floor would pass the game's 8 m dig limit at " + count + ". Pick shallower ground.";
            default: return "Refused.";
        }
    }

    internal static string ReadFault(ExcavationReadFault fault)
    {
        switch (fault)
        {
            case ExcavationReadFault.UnsupportedLattice: return "The terrain here does not use the 1 m grid this tool measures on. Nothing is changed.";
            case ExcavationReadFault.Rebuilding: return "The terrain here is still updating from another edit. Try again in a moment.";
            case ExcavationReadFault.LegacyTerrainModifier: return "An old-style terrain edit touches this area; its dig limit cannot be measured. Pick another spot.";
            case ExcavationReadFault.CompilerUnreadable: return "This area's terrain edits could not be read. Nothing is changed.";
            case ExcavationReadFault.TilesDisagree: return "Two terrain tiles disagree at their shared edge here. If one is still updating, try again in a moment; if it stays, it is an old seam, so pick another spot.";
            default: return "The terrain could not be read.";
        }
    }

    internal static string Commit(ExcavationCommitRefusal refusal, ExcavationReadFault fault, in ExcavationPlan plan, string? obstruction)
    {
        switch (refusal)
        {
            case ExcavationCommitRefusal.None: return "Digging " + ModeName(plan.Mode) + ". Checking the result.";
            case ExcavationCommitRefusal.Disabled: return "Excavation is off. Set [Excavation] Enabled = true in the Concerned Ladders config and restart.";
            case ExcavationCommitRefusal.Unavailable: return "Excavation is unavailable on this game build (see the log). Nothing is changed.";
            case ExcavationCommitRefusal.NotRegistered: return "The excavation terrain operation is not registered in this world (see the log). Nothing is changed.";
            case ExcavationCommitRefusal.TerrainOpsDisabled: return "The game has terrain edits switched off right now. Nothing is changed.";
            case ExcavationCommitRefusal.Busy: return "The last dig has not been confirmed yet. Wait for its result.";
            case ExcavationCommitRefusal.TerrainUnreadable: return ReadFault(fault);
            case ExcavationCommitRefusal.PlanRefused: return Plan(plan);
            case ExcavationCommitRefusal.NothingToDig: return Plan(plan);
            case ExcavationCommitRefusal.Protected: return "A ward you have no access to covers part of this square. Nothing is changed.";
            case ExcavationCommitRefusal.NoBuildArea: return "Part of this square is in a place where building is not allowed. Nothing is changed.";
            case ExcavationCommitRefusal.Obstructed: return "Something that is not a building piece is in the way" + (string.IsNullOrEmpty(obstruction) ? "" : " (" + obstruction + ")") + ". Clear it by hand first; this tool never removes objects.";
            case ExcavationCommitRefusal.ScanOverflow: return "Too many objects overlap this square to check them all. Nothing is changed.";
            case ExcavationCommitRefusal.SpawnRefused: return "The game declined the terrain edit here (ward or no-build area). Nothing is changed.";
            case ExcavationCommitRefusal.NoShovel: return "Equip the Iron Shovel to dig. Nothing is changed.";
            case ExcavationCommitRefusal.NotReady: return "You cannot dig right now (mid-swing, dodging or staggered). Nothing is changed.";
            case ExcavationCommitRefusal.ShovelWorn: return "The Iron Shovel needs " + F0(ExcavationCost.For(plan.Mode).Durability) + " durability for a " + ModeName(plan.Mode) + " hole. Repair it at a forge. Nothing is changed or charged.";
            case ExcavationCommitRefusal.NotEnoughStamina: return "You need " + F0(ExcavationCost.For(plan.Mode).Stamina) + " stamina for a " + ModeName(plan.Mode) + " hole. Rest, then try again. Nothing is changed or charged.";
            case ExcavationCommitRefusal.OnLadder: return "Step off the ladder to dig. Nothing is changed or charged.";
            case ExcavationCommitRefusal.NotAimed: return "Look at open ground within " + F0(ExcavationLimits.Reach) + " m to dig. Nothing is changed or charged.";
            case ExcavationCommitRefusal.NoPreview: return "Hold the shovel and look at the ground until the outline shows, then dig. Nothing is changed or charged.";
            case ExcavationCommitRefusal.PreviewChanged: return "The ground changed since the outline was drawn. Look again, then dig. Nothing is changed or charged.";
            default: return "Refused. Nothing is changed.";
        }
    }

    /// <summary>Why the aim found no square, and what to do about it (#487 slice 4).</summary>
    internal static string Aim(ExcavationAim aim, string? blocker)
    {
        switch (aim)
        {
            case ExcavationAim.NoPlayer: return "No local player in a world.";
            case ExcavationAim.Blocked: return "Something is between you and the ground" + (string.IsNullOrEmpty(blocker) ? "" : " (" + blocker + ")") +
                                               ". The shovel digs only ground you can see; look past it, or through an opening in a floor.";
            case ExcavationAim.TooFar: return "That ground is out of reach. Come within " + F0(ExcavationLimits.Reach) + " m.";
            default: return "Look at the ground to aim.";
        }
    }

    /// <summary>The first line of the held shovel's HUD text: the selected size, in metres.</summary>
    internal static string PreviewTitle(ExcavationMode mode)
    {
        ExcavationGeometry geometry = ExcavationGeometry.For(mode);
        return "Iron Shovel: " + ModeName(mode) + " (usable floor " + F0(geometry.UsableFloorSize) + " x " + F0(geometry.UsableFloorSize) +
               " m, " + F0(geometry.Depth) + " m deep)";
    }

    /// <summary>The second line: what a press would do here, with the measured numbers or the refusal and its remedy.</summary>
    internal static string PreviewState(ExcavationPreviewState shown)
    {
        if (shown == null) throw new System.ArgumentNullException(nameof(shown));
        if (!shown.HasTarget) return Aim(shown.Aim, shown.Blocker);
        ExcavationPlan plan = shown.Plan;
        switch (shown.Refusal)
        {
            case ExcavationCommitRefusal.None:
                string text = "Ready: floor at " + F1(plan.Bottom) + " m, " + F0(plan.Geometry.Depth) + " m below the highest edge (" + F1(plan.Datum) +
                              " m); walls " + F1(plan.MinWallHeight) + " to " + F1(plan.MaxWallHeight) + " m; ground changes inside the " +
                              F0(plan.Geometry.BorderSize) + " x " + F0(plan.Geometry.BorderSize) + " m outline. " + Cost(plan.Mode);
                if (plan.Floods) text += " The floor would be " + F1(plan.WaterDepth) + " m below the water surface and will flood.";
                if (shown.Pieces > 0) text += " " + shown.Pieces.ToString(CultureInfo.InvariantCulture) + " building-piece collider(s) stand on ground that would change; the game's own support rules decide whether they hold.";
                return text;
            case ExcavationCommitRefusal.NothingToDig:
                return "Already dug: the floor is level at " + F1(plan.Bottom) + " m. Nothing to dig; a press changes and charges nothing.";
            case ExcavationCommitRefusal.PlanRefused:
                return "Cannot dig here: " + Refusal(plan.Refusal, plan.RefusalVertices);
            default:
                return "Cannot dig here: " + Commit(shown.Refusal, shown.Fault, plan, shown.Obstruction);
        }
    }

    /// <summary>The third line: the controls, from the player's current bindings (the caller reads them from the game).</summary>
    internal static string PreviewHints(ExcavationSelection selection, string digKey, string switchKey)
    {
        if (selection == null) throw new System.ArgumentNullException(nameof(selection));
        return "[" + digKey + "] Dig " + ModeName(selection.Selected) + "    [" + switchKey + "] Switch to " + ModeName(selection.Other);
    }

    internal static string Outcome(ExcavationOutcome outcome, in ExcavationPlan plan)
    {
        string head = ModeName(plan.Mode) + " at (" + plan.CentreX.ToString(CultureInfo.InvariantCulture) + ", " +
                      plan.CentreZ.ToString(CultureInfo.InvariantCulture) + ")";
        switch (outcome)
        {
            case ExcavationOutcome.Complete: return head + ": done. Floor level at " + F1(plan.Bottom) + " m.";
            case ExcavationOutcome.Overshot: return head + ": the floor went lower than planned (another edit landed at the same time). Reported only; nothing is repaired or retried.";
            case ExcavationOutcome.Unreadable: return head + ": the result could not be read. Nothing is retried; plan again to see the terrain as it is.";
            case ExcavationOutcome.TimedOut: return head + ": the floor did not arrive within " + F0(ExcavationInFlight.TimeoutSeconds) + " s (in multiplayer, the player who owns this terrain may not have this build of the mod; see their log). Nothing is retried; plan again to see the terrain as it is.";
            case ExcavationOutcome.Abandoned: return head + ": the world closed before the result could be read.";
            default: return head + ": waiting for the result.";
        }
    }

    /// <summary>The object-scan line the read-only plan adds: what would refuse, and how many building-piece colliders
    /// stand on ground that would change (their support and collapse are the game's own).</summary>
    internal static string Ground(int obstructions, string? first, int pieces, bool overflow)
    {
        if (overflow) return "Too many objects overlap this square to check them all.";
        string text = obstructions > 0
            ? "In the way: " + obstructions.ToString(CultureInfo.InvariantCulture) + " object collider(s)" + (string.IsNullOrEmpty(first) ? "" : " (first: " + first + ")") + "; a dig would refuse."
            : "Nothing in the way.";
        if (pieces > 0) text += " " + pieces.ToString(CultureInfo.InvariantCulture) + " building-piece collider(s) stand on ground that would change; the game's own support rules decide whether they hold.";
        return text;
    }

    /// <summary>What an Iron Shovel dig of this mode is charged, once, when it goes ahead (#487 slice 3).</summary>
    internal static string Cost(ExcavationMode mode)
    {
        ExcavationCost cost = ExcavationCost.For(mode);
        return "A shovel dig here costs " + F0(cost.Stamina) + " stamina and " + F0(cost.Durability) +
               " durability (before world modifiers), charged once when it goes ahead.";
    }

    private static string F0(float value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static string F1(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
