using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Excavation;

/// <summary>What one press of the held Iron Shovel asks for (#487 slice 4; the dig session's two presses since #551).</summary>
internal enum ExcavationPress
{
    /// <summary>Nothing: neither button, or both in the same input step (ambiguous).</summary>
    None = 0,

    /// <summary>The game's Attack binding: set a corner, or dig what the preview shows (<see cref="DigSession.Primary"/>).</summary>
    Primary = 1,

    /// <summary>The game's Secondary Attack binding: one stage back, or arm and confirm an undo
    /// (<see cref="DigSession.Secondary"/>). Never digs.</summary>
    Secondary = 2,
}

/// <summary>Why the aim found no target (#487 slice 4). The targeting rule is the game's own build-placement rule: the
/// camera's ray against the placement layers, the first thing it hits must be terrain, and that point must be within
/// <see cref="ExcavationLimits.Reach"/> of the player's eyes.</summary>
internal enum ExcavationAim
{
    Ok = 0,

    /// <summary>No local player or camera.</summary>
    NoPlayer = 1,

    /// <summary>The ray met nothing at all within its length.</summary>
    NoGround = 2,

    /// <summary>The first thing the ray met is not terrain (a wall, a floor, a rock): the shovel never digs through
    /// something it cannot reach.</summary>
    Blocked = 3,

    /// <summary>The ground looked at is farther than <see cref="ExcavationLimits.Reach"/> from the player's eyes.</summary>
    TooFar = 4,
}

/// <summary>The rules the preview follows (#487 slice 4), game-free and tested.</summary>
internal static class ExcavationPreviewRules
{
    /// <summary>Re-reads the terrain at least this often while the target stays put, so a change in stamina, terrain or
    /// objects reaches the preview.</summary>
    internal const float SteadyRefreshSeconds = 0.25f;

    /// <summary>Re-reads the terrain no more often than this, however fast the aim moves: at most 20 reads of one rectangle
    /// (121 vertices at most) per second, whatever the frame rate.</summary>
    internal const float MinimumRefreshSeconds = 0.05f;

    /// <summary>How long the preview must have been continuously visible before a press may dig. Closing the inventory, a
    /// menu or the map while the Attack button is held, or equipping the shovel and pressing at once, gives the game's
    /// attack edge in the first input step; the preview was hidden then, so that press digs nothing.</summary>
    internal const float ConfirmDwellSeconds = 0.2f;

    /// <summary>The preview must have been drawn in one of this many latest frames for a press to use it.</summary>
    internal const int MaximumFrameAge = 2;

    /// <summary>How far above the terrain the outlines are drawn, metres.</summary>
    internal const float LineLift = 0.05f;

    /// <summary>One press of the game's own edges (true for one input step per press) asks for one thing. Both at once is
    /// ambiguous and asks for nothing.</summary>
    internal static ExcavationPress PressFor(bool primaryPressed, bool secondaryPressed)
    {
        if (primaryPressed == secondaryPressed) return ExcavationPress.None;
        return primaryPressed ? ExcavationPress.Primary : ExcavationPress.Secondary;
    }

    /// <summary>Whether the preview re-reads the terrain this frame. A target that moved waits out the minimum interval; a
    /// steady one is re-read on the steady interval. An unknown interval (no read yet, or a clock that is not finite)
    /// reads.</summary>
    internal static bool ShouldRefresh(bool targetChanged, float secondsSinceLastRead)
    {
        if (!LadderRules.Finite(secondsSinceLastRead) || secondsSinceLastRead < 0f) return true;
        return secondsSinceLastRead >= (targetChanged ? MinimumRefreshSeconds : SteadyRefreshSeconds);
    }
}
