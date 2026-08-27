using System;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>Decides whether a road-painted terrain cell looks like part of a
/// narrow path rather than a broad cleared area, so chunk recovery does not
/// turn plazas, leveled bases, and wide pads into road tangles. Purely
/// geometric and unit-tested; the game adapter supplies the paint lookup.</summary>
internal static class RecoveryShapeHeuristic
{
    /// <summary>Cells examined on each side of the candidate (a 5×5 window
    /// at radius 2, roughly ±2 m at standard terrain scale).</summary>
    public const int WindowRadius = 2;

    /// <summary>A candidate whose window is more than half road paint sits
    /// inside a broad painted area, not on a path. Hoe and stonecutter
    /// brushes are ~2 m wide, so genuine paths fill at most ~2 columns of
    /// the window (≤10 of 25 cells); a plaza boundary fills ≥15. Wide paved
    /// areas are deliberately not auto-recovered — traversal still records
    /// them when walked.</summary>
    public const float MaximumPaintedFraction = 0.5f;

    public static bool IsPathLike(Func<int, int, bool> isRoadPainted, int x, int y)
    {
        int window = (2 * WindowRadius) + 1;
        int total = window * window;
        int painted = 0;

        for (int dy = -WindowRadius; dy <= WindowRadius; dy++)
        {
            for (int dx = -WindowRadius; dx <= WindowRadius; dx++)
            {
                if (isRoadPainted(x + dx, y + dy))
                {
                    painted++;
                }
            }
        }

        return painted <= (int)(total * MaximumPaintedFraction);
    }
}
