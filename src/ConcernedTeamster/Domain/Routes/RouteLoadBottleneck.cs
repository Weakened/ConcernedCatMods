using TheConcernedCat.ConcernedTeamster.Domain.Load;

namespace TheConcernedCat.ConcernedTeamster.Domain.Routes;

/// <summary>Binds a route profile to the calibrated load model (CT-023).
/// The bottleneck grade is the profile's steepest sampled section treated
/// as a climb — routes are hauled both ways, so the steepest stretch is a
/// climb in one of them. Verdicts are exactly LoadModel's answers for that
/// grade (test-asserted equality); nothing here re-derives physics.</summary>
public static class RouteLoadBottleneck
{
    public sealed class Result
    {
        public Result(
            bool hasGradeData,
            float bottleneckGradePercent,
            LoadRecommendation? provenMaxMass,
            LoadVerdict? verdict,
            float queriedMass,
            bool massAdviceReliable = true)
        {
            HasGradeData = hasGradeData;
            BottleneckGradePercent = bottleneckGradePercent;
            ProvenMaxMass = provenMaxMass;
            Verdict = verdict;
            QueriedMass = queriedMass;
            MassAdviceReliable = massAdviceReliable;
        }

        /// <summary>False when the profile produced no sampled grade at all
        /// (fully unsampled route) — then nothing else is meaningful.</summary>
        public bool HasGradeData { get; }

        /// <summary>The steepest sampled |grade| (NaN without grade data).
        /// Terrain fact, not mass-calibrated — still valid even when
        /// <see cref="MassAdviceReliable"/> is false.</summary>
        public float BottleneckGradePercent { get; }

        /// <summary>Heaviest total mass with a proven Climbs row at the
        /// bottleneck grade or steeper; null when nothing is proven there or
        /// when <see cref="MassAdviceReliable"/> is false.</summary>
        public LoadRecommendation? ProvenMaxMass { get; }

        /// <summary>LoadModel's verdict for the chosen mass at the
        /// bottleneck grade; null when no mass was chosen or when
        /// <see cref="MassAdviceReliable"/> is false.</summary>
        public LoadVerdict? Verdict { get; }

        /// <summary>The mass the verdict answered, NaN when none was chosen.</summary>
        public float QueriedMass { get; }

        /// <summary>CT-037: false when a registered mod affecting cart mass
        /// or physics is detected, in which case <see cref="ProvenMaxMass"/>
        /// and <see cref="Verdict"/> are always null even with grade data —
        /// callers must show "unavailable", never fall back to
        /// "nothing proven here" (a different, terrain-caused meaning).</summary>
        public bool MassAdviceReliable { get; }
    }

    public static Result Evaluate(
        RouteProfile profile, LoadModel model, float? chosenTotalMass, bool massAdviceReliable = true)
    {
        if (float.IsNaN(profile.MaxAbsGradePercent))
        {
            return new Result(false, float.NaN, null, null, float.NaN, massAdviceReliable);
        }

        float grade = profile.MaxAbsGradePercent;
        if (!massAdviceReliable)
        {
            return new Result(true, grade, null, null, chosenTotalMass ?? float.NaN, massAdviceReliable: false);
        }

        LoadRecommendation? proven = model.RecommendedMaxMass(grade);
        LoadVerdict? verdict = chosenTotalMass.HasValue ? model.Query(grade, chosenTotalMass.Value) : null;
        return new Result(true, grade, proven, verdict, chosenTotalMass ?? float.NaN);
    }
}
