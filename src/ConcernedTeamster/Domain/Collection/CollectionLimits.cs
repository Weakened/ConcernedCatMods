using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Every budget and threshold Gunnar's collection uses that is
/// <b>his</b>, each with the reason it is that number (#381).
///
/// <b>What is deliberately not here.</b> How many targets go in one trip, how
/// many trips a plan writes out and how many rounds a job may take. Those are
/// the shared runtime's - a job order carries its own round count and the tour
/// partitioner its own ceiling - and a second set of numbers for the same
/// questions would be two things to change and one of them forgotten. What is
/// left is what only this product can answer: how far he is allowed to look, how
/// close he has to be, and how much of the game's carry limit he may use.
///
/// <b>None of these is ever written to disk.</b> Nothing here is persisted or
/// derived from anything persisted, so changing one changes pacing and never a
/// save.</summary>
internal sealed class CollectionLimits
{
    private CollectionLimits(
        int mostCandidatesPerSurvey,
        float surveyRadiusMetres,
        float pickupReachMetres,
        float arrivalToleranceMetres,
        float carrySafetyMarginKilograms)
    {
        MostCandidatesPerSurvey = mostCandidatesPerSurvey;
        SurveyRadiusMetres = surveyRadiusMetres;
        PickupReachMetres = pickupReachMetres;
        ArrivalToleranceMetres = arrivalToleranceMetres;
        CarrySafetyMarginKilograms = carrySafetyMarginKilograms;
    }

    /// <summary>The shipped numbers.</summary>
    public static CollectionLimits Default { get; } = new CollectionLimits(
        mostCandidatesPerSurvey: 256,
        surveyRadiusMetres: 48f,
        pickupReachMetres: 2.2f,
        arrivalToleranceMetres: 1.5f,
        carrySafetyMarginKilograms: 0f);

    /// <summary>How many candidates one survey may consider. A bound, not a
    /// target: an area with more is reported as truncated and worked in rounds
    /// rather than refused.</summary>
    public int MostCandidatesPerSurvey { get; }

    /// <summary>The largest work-area radius this slice accepts. There is no
    /// unlimited-radius search: an area is a boundary handed in, and a request
    /// for a bigger one is refused rather than clamped, because a clamp is a
    /// silent substitution of an area the player did not ask for.</summary>
    public float SurveyRadiusMetres { get; }

    /// <summary>How close he must be to take something.</summary>
    public float PickupReachMetres { get; }

    /// <summary>How close counts as arrived at a stop. Vanilla's move answers
    /// "stopped", never "arrived", so arrival is decided from distance - the
    /// same rule the shipped haul runtime already uses.</summary>
    public float ArrivalToleranceMetres { get; }

    /// <summary>Weight held back from his carry limit. Zero: the game's own
    /// limit is the limit, and a margin here would be an invented number
    /// standing between the player and the semantics they already know.</summary>
    public float CarrySafetyMarginKilograms { get; }

    /// <summary>Refuses a set of limits that cannot work rather than clamping it
    /// into one that can. A clamp hides a configuration mistake behind
    /// behaviour nobody asked for.</summary>
    public CollectionLimits Validate()
    {
        Require(MostCandidatesPerSurvey >= 1, nameof(MostCandidatesPerSurvey), "a survey must consider something");
        Require(SurveyRadiusMetres > 0f, nameof(SurveyRadiusMetres), "an area has a size");
        Require(PickupReachMetres > 0f, nameof(PickupReachMetres), "he has to be able to reach something");
        Require(ArrivalToleranceMetres > 0f, nameof(ArrivalToleranceMetres),
            "a tolerance of nothing is reachable only on exact float equality");
        Require(CarrySafetyMarginKilograms >= 0f, nameof(CarrySafetyMarginKilograms),
            "a negative margin would raise his carry limit above the game's");
        return this;
    }

    /// <summary>A copy with different numbers, for tests that need a small
    /// ceiling to reach in a few steps. Production uses
    /// <see cref="Default"/>.</summary>
    internal CollectionLimits With(
        int? mostCandidatesPerSurvey = null,
        float? surveyRadiusMetres = null,
        float? pickupReachMetres = null,
        float? arrivalToleranceMetres = null,
        float? carrySafetyMarginKilograms = null) =>
        new CollectionLimits(
            mostCandidatesPerSurvey ?? MostCandidatesPerSurvey,
            surveyRadiusMetres ?? SurveyRadiusMetres,
            pickupReachMetres ?? PickupReachMetres,
            arrivalToleranceMetres ?? ArrivalToleranceMetres,
            carrySafetyMarginKilograms ?? CarrySafetyMarginKilograms);

    private static void Require(bool condition, string name, string why)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, why);
        }
    }
}
