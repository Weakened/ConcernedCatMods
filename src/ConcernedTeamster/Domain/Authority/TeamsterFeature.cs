namespace TheConcernedCat.ConcernedTeamster.Domain.Authority;

/// <summary>Every shipped Teamster feature that touches a cart or its data
/// (CT-026). The multiplayer authority policy assigns each one a
/// <see cref="FeatureClass"/> and, for observations, whether remote data
/// must be labeled. This enum is the completeness anchor: the policy must
/// carry an entry for every value, and the policy document must list every
/// value — both are test/validator asserted, so a new feature cannot ship
/// without a deliberate authority decision.</summary>
public enum TeamsterFeature
{
    /// <summary>The bounded telemetry sampler feeding all panels.</summary>
    CartTelemetry,

    /// <summary>The Cart Status panel (mass, grade, surface, pull state).</summary>
    CartStatusPanel,

    /// <summary>The cargo manifest listing.</summary>
    CargoManifest,

    /// <summary>Load/grade advisory warnings.</summary>
    LoadWarnings,

    /// <summary>Descent risk evaluation and lookahead.</summary>
    DescentRisk,

    /// <summary>Stuck-cause diagnostics and recovery guidance.</summary>
    RecoveryGuidance,

    /// <summary>Per-world trip recording and road-quality scoring.</summary>
    TripRecording,

    /// <summary>Optional Cartographer route profiling (v0.5).</summary>
    RouteProfiling,

    /// <summary>The explicit, reversible parking brake — the ONLY feature
    /// that mutates cart state.</summary>
    ParkingBrake,
}

/// <summary>What a feature does to the cart. Only <see cref="Mutation"/>
/// features are authority-gated for the right to act; observation features
/// only ever read (and may need remote-data labeling).</summary>
public enum FeatureClass
{
    Observation,
    Mutation,
}
