using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What a dig costs (#551): scaled by the ground it removes, so the old fixed holes cost about what they did
/// (a 2x2x2 m hole, about 18 m³, near 15 stamina and 3 durability; a 4x4x4 m one, about 100 m³, 40 and 16). Lead
/// defaults. Stamina is capped so any dig can be paid by a fed character; durability is not, so a big dig wears the
/// shovel. An undo costs nothing and refunds nothing (owner decision 2026-11-10).</summary>
internal static class DigCost
{
    internal const float BaseStamina = 10f;

    internal const float StaminaPerCubicMetre = 0.3f;

    internal const float MaximumStamina = 60f;

    internal const float DurabilityPerCubicMetre = 0.16f;

    /// <summary>Stamina before the world's modifier (the game's UseStamina applies it).</summary>
    internal static float Stamina(float volume) =>
        LadderRules.Finite(volume) && volume > 0f ? Math.Min(MaximumStamina, BaseStamina + StaminaPerCubicMetre * volume) : float.NaN;

    /// <summary>Durability before the world's modifier.</summary>
    internal static float Durability(float volume) =>
        LadderRules.Finite(volume) && volume > 0f ? DurabilityPerCubicMetre * volume : float.NaN;

    /// <summary>The stamina the player must have, after the world's modifier; NaN (refused) for a bad modifier.</summary>
    internal static float StaminaNeeded(float volume, float staminaRate) =>
        LadderRules.Finite(staminaRate) && staminaRate >= 0f ? Stamina(volume) * staminaRate : float.NaN;

    /// <summary>The durability the dig takes, after the world's modifier; NaN (refused) for a bad modifier.</summary>
    internal static float DurabilityCost(float volume, float durabilityRate) =>
        LadderRules.Finite(durabilityRate) && durabilityRate >= 0f ? Durability(volume) * durabilityRate : float.NaN;
}

/// <summary>What is in flight: a dig lowering vertices, or an undo raising them back.</summary>
internal enum DigFlightKind
{
    Dig = 0,
    Undo = 1,
}

/// <summary>The one batch allowed in flight (#551; EXCAVATION.md claim 9, unchanged). A second batch before the terrain
/// has rebuilt would act on stale heights, so a commit is refused while one is active, and an active one ends only on a
/// result read back, a timeout, or the world going away. Nothing here issues an operation; there is no retry path.</summary>
internal sealed class DigInFlight
{
    /// <summary>How long a batch may stay pending before it is reported as not confirmed.</summary>
    internal const float TimeoutSeconds = 10f;

    internal bool IsActive { get; private set; }

    internal DigFlightKind Kind { get; private set; }

    internal DigRecord? Record { get; private set; }

    internal float StartedAt { get; private set; }

    /// <summary>Takes the slot. Refuses (false) while one is active, without a record, or for a time that is not finite.</summary>
    internal bool TryStart(DigFlightKind kind, DigRecord record, float now)
    {
        if (IsActive || record == null || !LadderRules.Finite(now)) return false;
        Kind = kind;
        Record = record;
        StartedAt = now;
        IsActive = true;
        return true;
    }

    /// <summary>The game's own per-operation re-check refused part of the batch: only <paramref name="placed"/> was handed
    /// to the tile owners, so only it is read back (and charged, and recorded for undo). Refused for a record that is not
    /// a subset of the one in flight.</summary>
    internal bool Narrow(DigRecord placed)
    {
        if (!IsActive || Record == null || placed == null || placed.Vertices.Count > Record.Vertices.Count) return false;
        foreach (DigVertex vertex in placed.Vertices)
        {
            bool found = false;
            foreach (DigVertex original in Record.Vertices)
            {
                if (original.X == vertex.X && original.Z == vertex.Z && original.Before == vertex.Before && original.After == vertex.After) { found = true; break; }
            }

            if (!found) return false;
        }

        Record = placed;
        return true;
    }

    /// <summary>One read-back: Complete, Overshot and Unreadable end the flight at once; Pending waits until the timeout.
    /// A read that found a rebuild in progress, or tiles that disagree at a shared edge, is Pending: the result is on its
    /// way. Any other read fault is Unreadable.</summary>
    internal ExcavationOutcome Observe(ExcavationReadFault fault, ExcavationProgress progress, float now)
    {
        if (!IsActive) throw new InvalidOperationException("Nothing in flight.");
        ExcavationOutcome outcome;
        if (fault == ExcavationReadFault.Rebuilding || fault == ExcavationReadFault.TilesDisagree) outcome = ExcavationOutcome.Waiting;
        else if (fault != ExcavationReadFault.None) outcome = ExcavationOutcome.Unreadable;
        else
        {
            outcome = progress switch
            {
                ExcavationProgress.Complete => ExcavationOutcome.Complete,
                ExcavationProgress.Overshot => ExcavationOutcome.Overshot,
                ExcavationProgress.Pending => ExcavationOutcome.Waiting,
                _ => ExcavationOutcome.Unreadable,
            };
        }

        if (outcome == ExcavationOutcome.Waiting && (!LadderRules.Finite(now) || now - StartedAt > TimeoutSeconds)) outcome = ExcavationOutcome.TimedOut;
        if (outcome != ExcavationOutcome.Waiting) IsActive = false;
        return outcome;
    }

    /// <summary>The world went away: the slot is freed without a read-back. Returns whether something was in flight.</summary>
    internal bool Abandon()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}

/// <summary>The commit gate's planner-side decisions for a dig (#551), in the order the adapter asks them. The adapter
/// answers the world questions only for a plan that passed every one, so a refused or empty plan never flashes a ward or
/// scans the world.</summary>
internal static class DigGate
{
    internal static ExcavationCommitRefusal BeforeWorldChecks(bool enabled, bool available, bool registered,
        bool terrainOpsDisabled, bool busy, ExcavationReadFault fault, in DigPlan plan)
    {
        if (!enabled) return ExcavationCommitRefusal.Disabled;
        if (!available) return ExcavationCommitRefusal.Unavailable;
        if (!registered) return ExcavationCommitRefusal.NotRegistered;
        if (terrainOpsDisabled) return ExcavationCommitRefusal.TerrainOpsDisabled;
        if (busy) return ExcavationCommitRefusal.Busy;
        if (fault != ExcavationReadFault.None) return ExcavationCommitRefusal.TerrainUnreadable;
        return plan.Status switch
        {
            // A default (never planned) value reads as Ready with nothing lowered and must never pass.
            ExcavationStatus.Ready => plan.LoweredVertices > 0 && plan.LoweredVolume > 0f ? ExcavationCommitRefusal.None : ExcavationCommitRefusal.PlanRefused,
            ExcavationStatus.NothingToDig => ExcavationCommitRefusal.NothingToDig,
            _ => ExcavationCommitRefusal.PlanRefused,
        };
    }

    /// <summary>The world checks' verdict, in order: ward, no-build area, a scan that could not finish, anything in the way.</summary>
    internal static ExcavationCommitRefusal WorldChecks(bool wardAllows, bool noBuildArea, int obstructions, bool scanOverflow)
    {
        if (!wardAllows) return ExcavationCommitRefusal.Protected;
        if (noBuildArea) return ExcavationCommitRefusal.NoBuildArea;
        if (scanOverflow) return ExcavationCommitRefusal.ScanOverflow;
        if (obstructions > 0) return ExcavationCommitRefusal.Obstructed;
        return ExcavationCommitRefusal.None;
    }
}
