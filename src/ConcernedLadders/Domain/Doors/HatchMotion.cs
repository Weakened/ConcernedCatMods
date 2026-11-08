using System;

namespace TheConcernedCat.ConcernedLadders.Domain.Doors;

/// <summary>The lid's motion and its obstruction policy (CL-004 #485). Game-free: the runtime adapter feeds it the
/// native door's authoritative state and one occupancy answer per frame, and applies the returned progress to the
/// lid's transform and collider together, so motion and collision cannot disagree.
///
/// <para>State. The native <c>Door</c> component on the clone owns the open/closed state: its own <c>UseDoor</c> RPC
/// writes the ZDO, every client's <c>Door.UpdateState</c> reads it, and its animator's <c>state</c> integer follows.
/// The hatch reads that integer and nothing else. Native doors open to +1 or -1 depending on which side the user
/// stands; a hatch has one swing direction, so both signs mean open.</para>
///
/// <para>Obstruction. The lid never moves into a character. Each frame the runtime asks whether the lid's pose at the
/// proposed progress would overlap a character; if it would, the lid holds where it is and tries again next frame.
/// So a lid with someone standing on it stays closed until they step off, a lid closing over a head waits, and
/// nothing is launched, pushed through a wall, or shut in: a held lid moves only toward its target and only into
/// empty space. The native state (and so the hover text) still says what was asked for, and every client applies
/// the same rule to the same synchronized character positions.</para></summary>
internal static class HatchMotion
{
    /// <summary>Shortest and longest swing the policy accepts; a donor outside this range is a census error.</summary>
    internal const float MinimumSwingSeconds = 0.1f;
    internal const float MaximumSwingSeconds = 5f;

    /// <summary>Longest frame step applied at once, seconds, so a hitch cannot jump the lid through a body in one step.</summary>
    internal const float MaximumStepSeconds = 0.05f;

    /// <summary>The native door state integer means open when it is not zero, whatever its sign.</summary>
    internal static bool IsOpenState(int nativeState) => nativeState != 0;

    /// <summary>Ease-in-out over the swing: progress 0..1 to the fraction of the open angle.</summary>
    internal static float Ease(float progress)
    {
        float p = Clamp01(progress);
        return p * p * (3f - 2f * p);
    }

    /// <summary>The hinge angle for a progress, degrees.</summary>
    internal static float Angle(float progress) => HatchGeometry.OpenAngleDegrees * Ease(progress);

    /// <summary>Where the lid would be after this frame if nothing were in the way. Linear in time, so a full swing
    /// takes exactly <paramref name="swingSeconds"/>; a reversal continues from the current progress.</summary>
    internal static float Proposed(float progress, bool open, float seconds, float swingSeconds)
    {
        float current = Clamp01(progress);
        float step = StepSeconds(seconds) / ClampSwing(swingSeconds);
        return open ? Math.Min(1f, current + step) : Math.Max(0f, current - step);
    }

    /// <summary>The progress after this frame: the proposal, unless the lid's pose there would overlap a character,
    /// in which case the lid holds at its current progress.</summary>
    internal static float Advance(float progress, float proposed, bool proposedPoseOccupied)
    {
        float current = Clamp01(progress);
        return proposedPoseOccupied ? current : Clamp01(proposed);
    }

    /// <summary>True while the lid has somewhere to go.</summary>
    internal static bool IsMoving(float progress, bool open)
    {
        float p = Clamp01(progress);
        return open ? p < 1f : p > 0f;
    }

    /// <summary>The opening is a passage only once the lid is fully open: a climber may go up through it, and the
    /// ladder's top exit may route through it.</summary>
    internal static bool IsPassage(float progress) => Clamp01(progress) >= 1f;

    /// <summary>The lid is a floor only while closed and staying closed. A moving, held or open lid is never a
    /// landing the top exit may choose; it must find a real floor instead.</summary>
    internal static bool IsLanding(float progress, bool open) => !open && Clamp01(progress) <= 0f;

    private static float StepSeconds(float seconds) =>
        float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f ? 0f : Math.Min(seconds, MaximumStepSeconds);

    private static float ClampSwing(float swingSeconds) =>
        float.IsNaN(swingSeconds) || float.IsInfinity(swingSeconds)
            ? MaximumSwingSeconds
            : Math.Max(MinimumSwingSeconds, Math.Min(MaximumSwingSeconds, swingSeconds));

    private static float Clamp01(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
}
