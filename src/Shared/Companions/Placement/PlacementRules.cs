using System;

namespace TheConcernedCat.Companions.Placement;

/// <summary>Tunable bounds for where a companion may sit.
///
/// The defaults are the starting point named in the design brief: a 3-10 metre
/// band around the anchor. Close enough to read as "lives here", far enough not
/// to stand in the doorway. The values are settings rather than constants
/// because they are expected to move once the behaviour is seen in game.</summary>
internal sealed class PlacementRules
{
    public const float DefaultMinimumRadius = 3f;
    public const float DefaultMaximumRadius = 10f;
    public const float DefaultFireComfortRadius = 5f;
    public const float DefaultMaximumHeightDelta = 3f;
    public const float DefaultAnchorMoveTolerance = 1.5f;

    public PlacementRules(
        float minimumRadius = DefaultMinimumRadius,
        float maximumRadius = DefaultMaximumRadius,
        float fireComfortRadius = DefaultFireComfortRadius,
        float maximumHeightDelta = DefaultMaximumHeightDelta,
        float anchorMoveTolerance = DefaultAnchorMoveTolerance)
    {
        // Finiteness is checked first and explicitly. Every comparison below is
        // false for NaN, so a NaN slipping through would not just survive
        // validation - it would go on to make the planner's own range checks
        // pass for every candidate, silently disabling the bounds this type
        // exists to enforce.
        RequireFinite(minimumRadius, nameof(minimumRadius));
        RequireFinite(maximumRadius, nameof(maximumRadius));
        RequireFinite(fireComfortRadius, nameof(fireComfortRadius));
        RequireFinite(maximumHeightDelta, nameof(maximumHeightDelta));
        RequireFinite(anchorMoveTolerance, nameof(anchorMoveTolerance));

        RequireNotNegative(minimumRadius, nameof(minimumRadius));
        RequireNotNegative(fireComfortRadius, nameof(fireComfortRadius));
        RequireNotNegative(maximumHeightDelta, nameof(maximumHeightDelta));
        RequireNotNegative(anchorMoveTolerance, nameof(anchorMoveTolerance));

        if (maximumRadius < minimumRadius)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRadius), "The maximum placement radius cannot be below the minimum.");
        }

        MinimumRadius = minimumRadius;
        MaximumRadius = maximumRadius;
        FireComfortRadius = fireComfortRadius;
        MaximumHeightDelta = maximumHeightDelta;
        AnchorMoveTolerance = anchorMoveTolerance;
    }

    /// <summary>Closest the companion may sit to the anchor.</summary>
    public float MinimumRadius { get; }

    /// <summary>Furthest the companion may sit from the anchor. Also bounds how
    /// much world the planner inspects: it never looks outside this band, so
    /// there is no scan of anything but the immediate surroundings.</summary>
    public float MaximumRadius { get; }

    /// <summary>How close a fire must be for the spot to count as warm.</summary>
    public float FireComfortRadius { get; }

    /// <summary>How far above or below the anchor a spot may sit. Keeps the
    /// companion off roofs and out of cellars.</summary>
    public float MaximumHeightDelta { get; }

    /// <summary>How far the anchor must move before the companion relocates.</summary>
    public float AnchorMoveTolerance { get; }

    public static PlacementRules Default { get; } = new PlacementRules();

    private static void RequireFinite(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName, "Placement bounds must be finite numbers.");
        }
    }

    private static void RequireNotNegative(float value, string parameterName)
    {
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, "Placement bounds cannot be negative.");
        }
    }
}
