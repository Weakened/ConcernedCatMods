using System;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Domain;

/// <summary>One deliberate ladder action per gameplay input tick (N07).</summary>
internal enum ClimbAction
{
    None,
    Jump,
    Drop,
    Use,
}

/// <summary>Input routing while attached (owner addenda N07/N08). Game-free so the rules are tested directly.</summary>
internal static class ClimbControls
{
    /// <summary>Jump &gt; Drop &gt; Use, resolved once. Without gameplay focus nothing is a ladder action: a focused
    /// menu or console owns its own input. A native hammer action owns the shared Use binding (snap cycling), so
    /// Use is never the ladder's while one is in build mode; Jump and Drop stay explicit departures.</summary>
    internal static ClimbAction Resolve(bool gameplayFocus, bool jump, bool drop, bool use, bool buildModeOwnsUse)
    {
        if (!gameplayFocus) { return ClimbAction.None; }
        if (jump) { return ClimbAction.Jump; }
        if (drop) { return ClimbAction.Drop; }
        if (use && !buildModeOwnsUse) { return ClimbAction.Use; }
        return ClimbAction.None;
    }

    /// <summary>An attached climber may stay attached through a native building action: build mode is active,
    /// the right hand holds a building tool and the animator is in its tool swing. In build mode the game never
    /// starts a combat attack (PlayerAttackInput returns at once), so this exemption cannot cover combat.</summary>
    internal static bool IsNativeBuildAction(bool placeMode, bool holdsBuildTool, bool inAttack) =>
        placeMode && holdsBuildTool && inAttack;

    /// <summary>A refreshed connected run may replace the session's run only if it is the same shaft: the same
    /// foot line and standing side, width and rung pitch, and the climber's current height is still on it (from
    /// the lowest hang to the top). A different alignment, a removed section under the climber or a run that
    /// now ends below the feet ends the climb instead of moving the climber onto other geometry.</summary>
    internal static bool CanAdoptRun(in LadderGeometry current, in LadderGeometry refreshed, float feetHeight, float lowestHang)
    {
        if (!refreshed.IsClimbable || !ClimbPolicy.Finite(feetHeight) || !ClimbPolicy.Finite(lowestHang)) { return false; }
        if (current.Bottom.HorizontalDistanceTo(refreshed.Bottom) > SameLine) { return false; }
        if (current.StandingSide.X * refreshed.StandingSide.X + current.StandingSide.Z * refreshed.StandingSide.Z < SameHeading) { return false; }
        if (Math.Abs(current.Width - refreshed.Width) > 0.01f || Math.Abs(current.RungPitch - refreshed.RungPitch) > 0.001f) { return false; }
        return feetHeight >= lowestHang - Slack && feetHeight <= refreshed.Top.Y + Slack;
    }

    /// <summary>Horizontal tolerance for "the same foot line", metres.</summary>
    internal const float SameLine = 0.02f;

    /// <summary>Cosine tolerance for "the same standing side" (about 2.5 degrees).</summary>
    internal const float SameHeading = 0.999f;

    /// <summary>Height tolerance at the ends of a refreshed run, metres.</summary>
    internal const float Slack = 0.05f;
}

/// <summary>A fresh native Crouch press that began with gameplay focus. A press held through a mount, a menu,
/// the inventory or the console does not count when focus returns, and pressing again re-arms it: no buffered
/// drop. The native crouch edge is computed by the game from the held state, so this only filters it.</summary>
internal sealed class DropTrigger
{
    private bool _armed;

    /// <summary>Called on mount: a Crouch already held then is not a request to drop.</summary>
    internal void Arm(bool crouchHeld) => _armed = !crouchHeld;

    /// <summary>Called every frame while attached with the raw held state and whether gameplay has focus.</summary>
    internal void Observe(bool crouchHeld, bool gameplayFocus)
    {
        if (!crouchHeld) { _armed = true; }
        else if (!gameplayFocus) { _armed = false; }
    }

    /// <summary>The native crouch edge for this input tick; true only for an armed press.</summary>
    internal bool Take(bool nativeCrouchEdge)
    {
        if (!nativeCrouchEdge || !_armed) { return false; }
        _armed = false;
        return true;
    }

    internal bool Armed => _armed;
}
