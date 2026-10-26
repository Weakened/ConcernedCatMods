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

    /// <summary>The section the hands hold: the hand height clamped onto the current run (a climber near the top
    /// reaches above it), then the section whose span contains it. Returns -1 when no listed section contains it,
    /// i.e. the supporting section is gone.</summary>
    internal static int SupportingSection(float handHeight, float runBottom, float runTop, float[] sectionBottoms, float[] sectionTops)
    {
        if (!ClimbPolicy.Finite(handHeight) || sectionBottoms.Length != sectionTops.Length) { return -1; }
        float clamped = Math.Min(Math.Max(handHeight, runBottom), runTop);
        int found = -1;
        for (int index = 0; index < sectionBottoms.Length; index++)
        {
            if (clamped >= sectionBottoms[index] - 0.001f && clamped <= sectionTops[index] + 0.001f)
            {
                // At a seam the upper section holds the hands.
                if (found < 0 || sectionBottoms[index] > sectionBottoms[found]) { found = index; }
            }
        }

        return found;
    }

    /// <summary>The part of the motor's last commanded velocity that physics actually kept, along the command's
    /// direction and never beyond it. A Drop removes only this: a command that a landing or collision already
    /// cancelled is never subtracted again (which would launch the body the other way).</summary>
    internal static void SurvivingCommand(float vx, float vy, float vz, float cx, float cy, float cz,
        out float sx, out float sy, out float sz)
    {
        sx = sy = sz = 0f;
        float length = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
        if (!(length > 1e-5f) || !ClimbPolicy.Finite(vx) || !ClimbPolicy.Finite(vy) || !ClimbPolicy.Finite(vz)) { return; }
        float dx = cx / length, dy = cy / length, dz = cz / length;
        float along = Math.Min(Math.Max(vx * dx + vy * dy + vz * dz, 0f), length);
        sx = dx * along; sy = dy * along; sz = dz * along;
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
    internal void Arm(bool crouchHeld) { _armed = !crouchHeld; _pending = false; }

    /// <summary>Called every frame while attached with the raw held state and whether gameplay has focus.</summary>
    internal void Observe(bool crouchHeld, bool gameplayFocus)
    {
        if (!gameplayFocus) { _pending = false; } // A press never survives a menu taking focus.
        if (!crouchHeld) { _armed = true; }
        else if (!gameplayFocus) { _armed = false; }
    }

    /// <summary>A Crouch button-down seen with gameplay focus on a rendered frame. It counts even when the game's own
    /// crouch edge is missing: the native edge is stale after a console or menu had focus (the controller does not
    /// update its last-crouch state then), so a fresh press there would otherwise be lost.</summary>
    internal void Press(bool pressedDown, bool gameplayFocus)
    {
        if (pressedDown && gameplayFocus && _armed) { _pending = true; }
    }

    /// <summary>The native crouch edge for this input tick, or a pending fresh press; true once per armed press.</summary>
    internal bool Take(bool nativeCrouchEdge)
    {
        bool wanted = nativeCrouchEdge || _pending;
        _pending = false;
        if (!wanted || !_armed) { return false; }
        _armed = false;
        return true;
    }

    /// <summary>A drop is pending or the trigger is armed and a press is arriving this frame.</summary>
    internal bool Pending => _pending;

    internal bool Armed => _armed;

    private bool _pending;
}
