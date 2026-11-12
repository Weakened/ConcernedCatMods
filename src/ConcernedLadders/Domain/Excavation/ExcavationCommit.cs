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

    /// <summary>The floor did not arrive within <see cref="DigInFlight.TimeoutSeconds"/>. Reported; the next
    /// commit is a fresh plan from freshly read terrain, never an automatic retry.</summary>
    TimedOut = 4,

    /// <summary>The world went away before the result could be read (unload, logout, plugin teardown).</summary>
    Abandoned = 5,
}
