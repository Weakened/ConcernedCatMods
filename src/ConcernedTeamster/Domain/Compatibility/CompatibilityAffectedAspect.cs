namespace TheConcernedCat.ConcernedTeamster.Domain.Compatibility;

/// <summary>Which of Teamster's own readings a known mod's presence calls
/// into question (CT-037). Feature code queries this through
/// <see cref="CompatibilityAdvisoryGate"/> — never a mod's GUID or name
/// directly — so the precedence policy stays data-driven.</summary>
public enum CompatibilityAffectedAspect
{
    /// <summary>No Teamster reading is called into question.</summary>
    None,

    /// <summary>The mod changes cart mass, weight limits, or pulling
    /// physics — every LoadModel- or RiskModel-derived verdict (warnings,
    /// stuck diagnostics, recovery guidance, route bottlenecks, descent
    /// risk) was calibrated against vanilla physics and can no longer be
    /// trusted as vanilla truth while such a mod is present.</summary>
    CartMassOrPhysics,
}
