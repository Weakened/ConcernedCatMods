using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>Why a commit was refused (#487 slice 2). Every value means nothing was committed and nothing may be
/// charged.</summary>
internal enum ExcavationCommitRefusal
{
    None = 0,

    /// <summary>The off-by-default switch is off.</summary>
    Disabled = 1,

    /// <summary>The start-up probe could not find a game member the adapter reads; this build is not one it knows.</summary>
    Unavailable = 2,

    /// <summary>The mode's terrain operation is not in this game's terrain-operation registry, so the tile owner
    /// could not look it up.</summary>
    NotRegistered = 3,

    /// <summary>The game has switched every terrain operation off (TerrainOp.m_forceDisableTerrainOps).</summary>
    TerrainOpsDisabled = 4,

    /// <summary>An earlier operation has not been confirmed yet. One in flight at a time (EXCAVATION.md claim 9).</summary>
    Busy = 5,

    /// <summary>The terrain adapter found a fault; see the read fault.</summary>
    TerrainUnreadable = 6,

    /// <summary>The planner refused; see the plan's refusal.</summary>
    PlanRefused = 7,

    /// <summary>The planned floor is already there. Free, and no operation is issued.</summary>
    NothingToDig = 8,

    /// <summary>A ward the player has no access to covers part of the changed square (checked at the wider radius,
    /// claim 10).</summary>
    Protected = 9,

    /// <summary>Part of the changed square is inside a location where building is not allowed.</summary>
    NoBuildArea = 10,

    /// <summary>A rock, tree, item, cart or other object that is not a building piece occupies the changed square.
    /// The operation would leave it floating, and the mod never removes it.</summary>
    Obstructed = 11,

    /// <summary>More objects overlap the square than the scan can hold, so the scan could not finish.</summary>
    ScanOverflow = 12,

    /// <summary>The game's own terrain-tool path declined to place the operation.</summary>
    SpawnRefused = 13,

    /// <summary>The Iron Shovel is not the equipped weapon (#487 slice 3).</summary>
    NoShovel = 14,

    /// <summary>The player is mid-swing, dodging, staggered or knocked back.</summary>
    NotReady = 15,

    /// <summary>The shovel has less durability left than this dig costs. Repair it at a forge.</summary>
    ShovelWorn = 16,

    /// <summary>The player has less stamina than this dig costs.</summary>
    NotEnoughStamina = 17,

    /// <summary>The player is on a ladder (#487 slice 4). A ladder lets only the building hammer be used while attached,
    /// so the shovel does not dig from one.</summary>
    OnLadder = 18,

    /// <summary>The aim found no square: nothing looked at, something other than terrain in the way, or too far.</summary>
    NotAimed = 19,

    /// <summary>The press came before the preview had been shown long enough, or when it was not shown at all (just
    /// equipped, a menu or the inventory just closed, or the size just switched).</summary>
    NoPreview = 20,

    /// <summary>The terrain read at the press no longer gives the plan the preview showed as ready.</summary>
    PreviewChanged = 21,
}

/// <summary>How a committed operation ended.</summary>
internal enum ExcavationOutcome
{
    /// <summary>Still pending; keep reading.</summary>
    Waiting = 0,

    /// <summary>Every floor vertex is at the planned floor.</summary>
    Complete = 1,

    /// <summary>Some floor vertex is below the planned floor. Reported, never repaired.</summary>
    Overshot = 2,

    /// <summary>The terrain could not be read. The outcome is unknown; reported, never retried.</summary>
    Unreadable = 3,

    /// <summary>The floor did not arrive within <see cref="ExcavationInFlight.TimeoutSeconds"/>. Reported; the next
    /// commit is a fresh plan from freshly read terrain, never an automatic retry.</summary>
    TimedOut = 4,

    /// <summary>The world went away before the result could be read (unload, logout, plugin teardown).</summary>
    Abandoned = 5,
}

/// <summary>The commit gate's decisions, in the order the adapter asks them (#487 slice 2). The adapter answers the
/// world questions (ward, no-build area, objects) only for a plan that passed every earlier one, so a refused or
/// empty plan never flashes a ward or scans the world.</summary>
internal static class ExcavationGate
{
    internal static ExcavationCommitRefusal BeforeWorldChecks(bool enabled, bool available, bool registered,
        bool terrainOpsDisabled, bool busy, ExcavationReadFault fault, in ExcavationPlan plan)
    {
        if (!enabled) return ExcavationCommitRefusal.Disabled;
        if (!available) return ExcavationCommitRefusal.Unavailable;
        if (!registered) return ExcavationCommitRefusal.NotRegistered;
        if (terrainOpsDisabled) return ExcavationCommitRefusal.TerrainOpsDisabled;
        if (busy) return ExcavationCommitRefusal.Busy;
        if (fault != ExcavationReadFault.None) return ExcavationCommitRefusal.TerrainUnreadable;
        switch (plan.Status)
        {
            // A real Ready plan always lowers something; a default (never planned) value reads as Ready with nothing
            // lowered and must never pass.
            case ExcavationStatus.Ready: return plan.LoweredVertices > 0 ? ExcavationCommitRefusal.None : ExcavationCommitRefusal.PlanRefused;
            case ExcavationStatus.NothingToDig: return ExcavationCommitRefusal.NothingToDig;
            default: return ExcavationCommitRefusal.PlanRefused;
        }
    }

    internal static ExcavationCommitRefusal WorldChecks(bool wardAllows, bool noBuildArea, int obstructions, bool scanOverflow)
    {
        if (!wardAllows) return ExcavationCommitRefusal.Protected;
        if (noBuildArea) return ExcavationCommitRefusal.NoBuildArea;
        if (scanOverflow) return ExcavationCommitRefusal.ScanOverflow;
        if (obstructions > 0) return ExcavationCommitRefusal.Obstructed;
        return ExcavationCommitRefusal.None;
    }
}

/// <summary>The one terrain operation allowed in flight (EXCAVATION.md claim 9). A second dig before the terrain has
/// rebuilt would lower it again from stale heights, so a commit is refused while one is active, and an active one
/// ends only on a result read back through <see cref="ExcavationPlanner.Check"/>, a timeout, or the world going
/// away. Nothing here ever issues an operation; there is no retry path to take.</summary>
internal sealed class ExcavationInFlight
{
    /// <summary>How long a commit may stay Pending before it is reported as not confirmed.</summary>
    internal const float TimeoutSeconds = 10f;

    private ExcavationPlan _plan;
    private float _startedAt;

    internal bool IsActive { get; private set; }

    /// <summary>The plan in flight. Its datum and floor stay fixed until it resolves.</summary>
    internal ExcavationPlan Plan => _plan;

    internal float StartedAt => _startedAt;

    /// <summary>Takes the single slot for a Ready plan. Refuses (false) while one is active, for anything but a Ready
    /// plan that lowers something, and for a time that is not finite.</summary>
    internal bool TryStart(in ExcavationPlan plan, float now)
    {
        if (IsActive || plan.Status != ExcavationStatus.Ready || plan.LoweredVertices <= 0 || !LadderRules.Finite(now)) return false;
        _plan = plan;
        _startedAt = now;
        IsActive = true;
        return true;
    }

    /// <summary>Feeds one read-back. Complete, Overshot and Unreadable end the flight at once; Pending waits until the
    /// timeout. A terrain read that found a rebuild in progress, or tiles that disagree at a shared edge (one has
    /// rebuilt, the other not yet), counts as Pending: the result is on its way. Any other read fault is Unreadable.</summary>
    internal ExcavationOutcome Observe(ExcavationReadFault fault, ExcavationProgress progress, float now)
    {
        if (!IsActive) throw new InvalidOperationException("No operation in flight.");
        ExcavationOutcome outcome;
        if (fault == ExcavationReadFault.Rebuilding || fault == ExcavationReadFault.TilesDisagree) outcome = ExcavationOutcome.Waiting;
        else if (fault != ExcavationReadFault.None) outcome = ExcavationOutcome.Unreadable;
        else
        {
            switch (progress)
            {
                case ExcavationProgress.Complete: outcome = ExcavationOutcome.Complete; break;
                case ExcavationProgress.Overshot: outcome = ExcavationOutcome.Overshot; break;
                case ExcavationProgress.Pending: outcome = ExcavationOutcome.Waiting; break;
                default: outcome = ExcavationOutcome.Unreadable; break;
            }
        }
        if (outcome == ExcavationOutcome.Waiting && (!LadderRules.Finite(now) || now - _startedAt > TimeoutSeconds))
        {
            outcome = ExcavationOutcome.TimedOut;
        }
        if (outcome != ExcavationOutcome.Waiting) IsActive = false;
        return outcome;
    }

    /// <summary>The world went away. The slot is freed without a read-back, and the result is unknown
    /// (<see cref="ExcavationOutcome.Abandoned"/>). Returns whether an operation was in flight.</summary>
    internal bool Abandon()
    {
        if (!IsActive) return false;
        IsActive = false;
        return true;
    }
}
