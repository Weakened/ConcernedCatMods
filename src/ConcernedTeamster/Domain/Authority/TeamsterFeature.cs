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

    /// <summary>The explicit, reversible parking brake — a mutation of cart
    /// state (the root body's constraints) under live local authority.</summary>
    ParkingBrake,

    /// <summary>Gunnar's opt-in worker runtime (#313): attaches and detaches a
    /// player-assigned cart through the cart's own attach and detach, and
    /// moves only his own body through the vanilla motor. A mutation under
    /// live local authority, additionally gated by the work authority rule
    /// (docs/settlement/cart-and-collection/DECISIONS.md D3, D4).</summary>
    GunnarHauling,

    /// <summary>Gunnar's opt-in collection runtime (#381, owner decision
    /// 2026-09-19): picks up a loose stone or a fallen branch the player points
    /// at, through the source's own vanilla pickup, and gathers what it drops
    /// into his own inventory. Off by default, under its own switch
    /// (<c>Workers/GunnarCollectionEnabled</c>) rather than hauling's, because
    /// it is a separate capability under a separate decision.
    ///
    /// <b>It is a Mutation that never touches a cart.</b> It changes world
    /// state - a picked source - so classifying it as observation would be
    /// false, and the matrix's <c>MayMutate</c> is about cart authority, which
    /// this feature simply never asks. What actually gates it is the shared
    /// work-authority rule (opted in, a loaded world, the host, not dedicated,
    /// nobody else connected), re-asked every frame, plus the source being one
    /// this client already owns - never one it claims.</summary>
    GunnarCollection,
}

/// <summary>What a feature does to the cart. Only <see cref="Mutation"/>
/// features are authority-gated for the right to act; observation features
/// only ever read (and may need remote-data labeling).</summary>
public enum FeatureClass
{
    Observation,
    Mutation,
}
