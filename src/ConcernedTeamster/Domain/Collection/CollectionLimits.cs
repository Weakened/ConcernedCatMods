using System;

namespace TheConcernedCat.ConcernedTeamster.Domain.Collection;

/// <summary>Every budget, ceiling and threshold Gunnar's collection uses, in one
/// place, each with the reason it is that number (#381).
///
/// <b>None of these is ever written to disk.</b> The wave-two review's rule for
/// wave three, and it is not stylistic: a cap that reaches a save file becomes a
/// migration the day somebody wants to change it. Nothing here is persisted,
/// nothing here is derived from a persisted value, and the round loop is written
/// so that changing any of them changes only how many rounds a job takes, never
/// whether the job is finished.
///
/// <b>They are first guesses and they are meant to be observed, not defended.</b>
/// Nobody has watched Gunnar work in a real base. The two that a player will
/// actually feel are <see cref="MostTargetsPerBatch"/> and
/// <see cref="MostRoundsPerJob"/>: the first decides how much he carries in one
/// go, the second how long he will keep going before he stops and says he is not
/// finished. Both are observable in the first play session with no instrumentation
/// - count how often a round comes back with work left over on a job a player
/// would call small.</summary>
internal sealed class CollectionLimits
{
    private CollectionLimits(
        int mostTargetsPerBatch,
        int mostRoundsPerJob,
        int mostCandidatesPerSurvey,
        float surveyRadiusMetres,
        float pickupReachMetres,
        float arrivalToleranceMetres,
        float carrySafetyMarginKilograms,
        int mostToursPerManifest)
    {
        MostTargetsPerBatch = mostTargetsPerBatch;
        MostRoundsPerJob = mostRoundsPerJob;
        MostCandidatesPerSurvey = mostCandidatesPerSurvey;
        SurveyRadiusMetres = surveyRadiusMetres;
        PickupReachMetres = pickupReachMetres;
        ArrivalToleranceMetres = arrivalToleranceMetres;
        CarrySafetyMarginKilograms = carrySafetyMarginKilograms;
        MostToursPerManifest = mostToursPerManifest;
    }

    /// <summary>The shipped numbers.</summary>
    public static CollectionLimits Default { get; } = new CollectionLimits(
        mostTargetsPerBatch: 24,
        mostRoundsPerJob: 16,
        mostCandidatesPerSurvey: 256,
        surveyRadiusMetres: 48f,
        pickupReachMetres: 2.2f,
        arrivalToleranceMetres: 1.5f,
        carrySafetyMarginKilograms: 0f,
        mostToursPerManifest: 8);

    /// <summary>How many targets one batch may hold, whatever his capacity says.
    ///
    /// Capacity is the real limit and this is the ceiling behind it: a batch of
    /// three hundred feather-light targets is a route nobody can watch and a
    /// quadratic ordering pass that is no longer free. Twenty-four matches the
    /// route ceiling the shared runtime's own sequencer uses, so the day this
    /// batching is replaced by that sequencer the number does not change.</summary>
    public int MostTargetsPerBatch { get; }

    /// <summary>How many rounds one job may take before he stops and reports.
    ///
    /// The round loop exists because a plan that covers part of a job must say
    /// so and be asked again. Without a ceiling, a job whose targets keep coming
    /// back unserviced is an NPC walking in a circle for as long as the player
    /// watches. Sixteen is enough for any batch size against any work area this
    /// slice allows and small enough that the loop visibly ends.</summary>
    public int MostRoundsPerJob { get; }

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

    /// <summary>How many tours one manual manifest may be worked out into. A
    /// manifest needing more is planned to this many and reports the remainder,
    /// exactly as a batch does: partly planned is a real answer, refusing a job
    /// the player can see is doable is not.</summary>
    public int MostToursPerManifest { get; }

    /// <summary>Refuses a set of limits that cannot work rather than clamping it
    /// into one that can. A clamp hides a configuration mistake behind
    /// behaviour nobody asked for.</summary>
    public CollectionLimits Validate()
    {
        Require(MostTargetsPerBatch >= 1, nameof(MostTargetsPerBatch), "a batch must be able to hold one target");
        Require(MostRoundsPerJob >= 1, nameof(MostRoundsPerJob), "a job must be able to run one round");
        Require(MostCandidatesPerSurvey >= 1, nameof(MostCandidatesPerSurvey), "a survey must consider something");
        Require(SurveyRadiusMetres > 0f, nameof(SurveyRadiusMetres), "an area has a size");
        Require(PickupReachMetres > 0f, nameof(PickupReachMetres), "he has to be able to reach something");
        Require(ArrivalToleranceMetres > 0f, nameof(ArrivalToleranceMetres),
            "a tolerance of nothing is reachable only on exact float equality");
        Require(CarrySafetyMarginKilograms >= 0f, nameof(CarrySafetyMarginKilograms),
            "a negative margin would raise his carry limit above the game's");
        Require(MostToursPerManifest >= 1, nameof(MostToursPerManifest), "a manifest must be able to run one tour");
        return this;
    }

    /// <summary>A copy with different numbers, for tests that need a small
    /// ceiling to reach in a few steps. Production uses
    /// <see cref="Default"/>.</summary>
    internal CollectionLimits With(
        int? mostTargetsPerBatch = null,
        int? mostRoundsPerJob = null,
        int? mostCandidatesPerSurvey = null,
        float? surveyRadiusMetres = null,
        float? pickupReachMetres = null,
        float? arrivalToleranceMetres = null,
        float? carrySafetyMarginKilograms = null,
        int? mostToursPerManifest = null) =>
        new CollectionLimits(
            mostTargetsPerBatch ?? MostTargetsPerBatch,
            mostRoundsPerJob ?? MostRoundsPerJob,
            mostCandidatesPerSurvey ?? MostCandidatesPerSurvey,
            surveyRadiusMetres ?? SurveyRadiusMetres,
            pickupReachMetres ?? PickupReachMetres,
            arrivalToleranceMetres ?? ArrivalToleranceMetres,
            carrySafetyMarginKilograms ?? CarrySafetyMarginKilograms,
            mostToursPerManifest ?? MostToursPerManifest);

    private static void Require(bool condition, string name, string why)
    {
        if (!condition)
        {
            throw new ArgumentOutOfRangeException(name, why);
        }
    }
}
