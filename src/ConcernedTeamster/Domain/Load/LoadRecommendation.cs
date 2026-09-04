namespace TheConcernedCat.ConcernedTeamster.Domain.Load;

/// <summary>The highest proven-climbable total cart mass at a grade
/// (CT-008), with the basis of the proving row.</summary>
public sealed class LoadRecommendation
{
    public LoadRecommendation(float totalMass, CalibrationBasis basis)
    {
        TotalMass = totalMass;
        Basis = basis;
    }

    public float TotalMass { get; }

    public CalibrationBasis Basis { get; }
}
