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
            default: return "Refused. Nothing is changed.";
        }
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
            case ExcavationOutcome.TimedOut: return head + ": the floor did not arrive within " + F0(ExcavationInFlight.TimeoutSeconds) + " s. Nothing is retried; plan again to see the terrain as it is.";
            case ExcavationOutcome.Abandoned: return head + ": the world closed before the result could be read.";
            default: return head + ": waiting for the result.";
        }
    }

    private static string F0(float value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static string F1(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
