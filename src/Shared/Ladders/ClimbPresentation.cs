using System;

namespace TheConcernedCat.Ladders;

/// <summary>Small, game-free decisions shared by the ladder pose and sound.
/// Keeping these here makes the presentation timing testable without Unity.</summary>
internal static class ClimbPresentation
{
    public const float MinimumContactIntervalSeconds = 0.22f;

    /// <summary>How much of the ladder lean is visible. Alignment is already
    /// eased by ClimbSession; this only makes the value safe for presentation.</summary>
    public static float PoseBlend(in ClimbTelemetry telemetry)
    {
        if (!telemetry.IsClimbing)
        {
            return 0f;
        }

        float value = telemetry.AlignFraction;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Min(Math.Max(value, 0f), 1f);
    }

    /// <summary>Valheim's locomotion controller consumes forward speed in
    /// metres per second. Signed velocity therefore gives us upward and
    /// downward motion without inventing another animation clock.</summary>
    public static float AnimatorForwardSpeed(in ClimbTelemetry telemetry)
    {
        if (!telemetry.IsClimbing)
        {
            return 0f;
        }

        float value = telemetry.Velocity;
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    /// <summary>One quiet contact when a moving climber crosses a new rung,
    /// rate limited so a fast climb never becomes a machine gun of footsteps.</summary>
    public static bool ShouldPlayRungContact(
        int previousRung,
        int currentRung,
        float velocity,
        float secondsSinceLast)
    {
        if (previousRung < 0 || currentRung < 0 || previousRung == currentRung)
        {
            return false;
        }

        if (float.IsNaN(velocity) || float.IsInfinity(velocity) || Math.Abs(velocity) < 0.01f)
        {
            return false;
        }

        return !float.IsNaN(secondsSinceLast) &&
               secondsSinceLast >= MinimumContactIntervalSeconds;
    }
}
