using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Risk;

/// <summary>Bounded lookahead configuration (CT-011). The point count is
/// the only knob (0 disables lookahead entirely); spacing is a constant.
/// The height-query budget per evaluation is exactly
/// <see cref="MaxHeightQueriesPerEvaluation"/> — points plus the base
/// sample — and the offsets are precomputed once, so the sampling cost is
/// fixed and provable.</summary>
public sealed class LookaheadOptions
{
    public const int DefaultPoints = 3;
    public const int MinPoints = 0;
    public const int MaxPoints = 5;

    /// <summary>Distance between lookahead samples along the heading.</summary>
    public const float SpacingMeters = 4f;

    private LookaheadOptions(int points)
    {
        Points = points;
        var offsets = new float[points];
        for (int index = 0; index < points; index++)
        {
            offsets[index] = SpacingMeters * (index + 1);
        }

        OffsetsMeters = offsets;
    }

    public int Points { get; }

    /// <summary>Precomputed forward offsets (4, 8, 12, … m); length equals
    /// <see cref="Points"/>.</summary>
    public float[] OffsetsMeters { get; }

    /// <summary>The hard per-evaluation budget of ground-height queries:
    /// one per lookahead point plus the base sample under the cart. Zero
    /// when lookahead is disabled.</summary>
    public int MaxHeightQueriesPerEvaluation => Points == 0 ? 0 : Points + 1;

    public static LookaheadOptions CreateClamped(int points)
    {
        return new LookaheadOptions(Math.Min(MaxPoints, Math.Max(MinPoints, points)));
    }
}
