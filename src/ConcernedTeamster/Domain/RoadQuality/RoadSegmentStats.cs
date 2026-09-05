using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.RoadQuality;

/// <summary>Additive per-segment accumulators (CT-017). Everything stored
/// is a sum, count, or max, so adding trips one at a time produces exactly
/// the same state as adding them all at once — incremental equals batch by
/// construction, and the equivalence is still proven by test. Derived
/// scores divide on read:
///
/// - Roughness = sumAbsGradeDelta / pairCount — mean absolute grade change
///   between consecutive in-segment samples (grade jitter, the terrain
///   bumpiness proxy; height is not recorded, so this is slope noise, not
///   literal height noise — documented limit).
/// - MeanGrade / MaxAbsGrade over samples with a finite grade.
/// - DragProxySpeed = sumLevelSpeed / levelCount — mean speed on
///   near-level ground (|grade| &lt; 3%); lower means something slows carts
///   there. Mass-agnostic in this version (documented limit).</summary>
public sealed class RoadSegmentStats
{
    /// <summary>|grade| below this counts as level for the drag proxy.</summary>
    public const float LevelGradeMaxPercent = 3f;

    public int SampleCount { get; private set; }

    public int PairCount { get; private set; }

    public float SumAbsGradeDelta { get; private set; }

    public int GradeCount { get; private set; }

    public float SumGrade { get; private set; }

    public float MaxAbsGrade { get; private set; }

    public int LevelCount { get; private set; }

    public float SumLevelSpeed { get; private set; }

    public float RoughnessGradeJitter => PairCount > 0 ? SumAbsGradeDelta / PairCount : float.NaN;

    public float MeanGradePercent => GradeCount > 0 ? SumGrade / GradeCount : float.NaN;

    public float DragProxySpeed => LevelCount > 0 ? SumLevelSpeed / LevelCount : float.NaN;

    /// <summary>Adds one sample (and its grade delta to the previous
    /// in-segment sample when both grades are finite).</summary>
    public void AddSample(float gradePercent, float speedMetersPerSecond, float? previousGradePercent)
    {
        SampleCount++;

        if (!float.IsNaN(gradePercent))
        {
            GradeCount++;
            SumGrade += gradePercent;
            float magnitude = Math.Abs(gradePercent);
            if (magnitude > MaxAbsGrade)
            {
                MaxAbsGrade = magnitude;
            }

            if (previousGradePercent is { } previous && !float.IsNaN(previous))
            {
                PairCount++;
                SumAbsGradeDelta += Math.Abs(gradePercent - previous);
            }

            if (magnitude < LevelGradeMaxPercent && !float.IsNaN(speedMetersPerSecond))
            {
                LevelCount++;
                SumLevelSpeed += speedMetersPerSecond;
            }
        }
    }

    /// <summary>Restores persisted accumulator state (sidecar load).</summary>
    public void Restore(
        int sampleCount, int pairCount, float sumAbsGradeDelta,
        int gradeCount, float sumGrade, float maxAbsGrade,
        int levelCount, float sumLevelSpeed)
    {
        SampleCount = sampleCount;
        PairCount = pairCount;
        SumAbsGradeDelta = sumAbsGradeDelta;
        GradeCount = gradeCount;
        SumGrade = sumGrade;
        MaxAbsGrade = maxAbsGrade;
        LevelCount = levelCount;
        SumLevelSpeed = sumLevelSpeed;
    }
}
