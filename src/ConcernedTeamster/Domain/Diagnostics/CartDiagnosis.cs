namespace TheConcernedCat.ConcernedTeamster.Domain.Diagnostics;

/// <summary>Why a pulled cart is not moving (CT-013), from observed
/// evidence only. Unclear is a first-class answer when signatures conflict
/// — the diagnostics never pick a plausible story over the truth.</summary>
public enum CartDiagnosis
{
    /// <summary>Not stuck (or not being pulled at all).</summary>
    None,

    /// <summary>Calibration proves this load cannot climb this grade.</summary>
    ImpossibleLoad,

    /// <summary>Calibration says this climb is marginal; stalling matches.</summary>
    MarginalLoad,

    /// <summary>Steep uncalibrated climb — the grade is the likely cause.</summary>
    SteepClimb,

    /// <summary>Near-level ground (or a proven-climbable grade) with no load
    /// explanation: something physical blocks the cart — an obstruction or
    /// grounded chassis.</summary>
    Obstruction,

    /// <summary>Signatures conflict or explain nothing; saying so beats
    /// guessing.</summary>
    Unclear,
}
