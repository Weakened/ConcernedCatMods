using System.Collections.Generic;

namespace TheConcernedCat.ConcernedTeamster.Domain.Authority;

/// <summary>The multiplayer trust matrix (CT-026): the single source of
/// truth for who may read, who may act, and when a reading is remote, across
/// every shipped Teamster feature. The policy document
/// (docs/mods/concerned-teamster/AUTHORITY_POLICY.md) is written to match
/// this table and a validator tripwire fails if any feature is missing from
/// it; the brake's enforcement is bound to <see cref="MayMutate"/> by test.
///
/// The whole matrix rests on two product invariants:
/// - Teamster sends NO network messages and takes NO ownership — it only
///   reads the game's replicated/local state and writes its own sidecar
///   files. So an unmodded peer's experience is provably unchanged by
///   Teamster's presence (there is nothing to alter it with; validator
///   audited).
/// - Exactly one feature mutates cart state (the parking brake), and only
///   under live local vanilla authority; every ambiguity fails closed.</summary>
public static class CartAuthorityPolicy
{
    private sealed class Entry
    {
        public Entry(FeatureClass featureClass, bool labelWhenRemote)
        {
            FeatureClass = featureClass;
            LabelWhenRemote = labelWhenRemote;
        }

        public FeatureClass FeatureClass { get; }

        /// <summary>Observation only: true when the reading is only fresh on
        /// the owning client, so an observer must be told it is remote/stale
        /// (e.g. cart mass, which the game refreshes every ~5 s on the owner).</summary>
        public bool LabelWhenRemote { get; }
    }

    // The matrix. Observation features never gate the right to READ (client
    // reading replicated/local state is always allowed); LabelWhenRemote
    // marks the ones whose numbers are owner-fresh and must be flagged when
    // observed from another client. ParkingBrake is the sole Mutation.
    private static readonly IReadOnlyDictionary<TeamsterFeature, Entry> Matrix =
        new Dictionary<TeamsterFeature, Entry>
        {
            [TeamsterFeature.CartTelemetry] = new(FeatureClass.Observation, labelWhenRemote: true),
            [TeamsterFeature.CartStatusPanel] = new(FeatureClass.Observation, labelWhenRemote: true),
            [TeamsterFeature.CargoManifest] = new(FeatureClass.Observation, labelWhenRemote: true),
            [TeamsterFeature.LoadWarnings] = new(FeatureClass.Observation, labelWhenRemote: true),
            [TeamsterFeature.DescentRisk] = new(FeatureClass.Observation, labelWhenRemote: true),
            [TeamsterFeature.RecoveryGuidance] = new(FeatureClass.Observation, labelWhenRemote: false),
            [TeamsterFeature.TripRecording] = new(FeatureClass.Observation, labelWhenRemote: false),
            [TeamsterFeature.RouteProfiling] = new(FeatureClass.Observation, labelWhenRemote: false),
            [TeamsterFeature.ParkingBrake] = new(FeatureClass.Mutation, labelWhenRemote: false),
        };

    /// <summary>Every feature the matrix governs — used by the completeness
    /// test and the document tripwire.</summary>
    public static IEnumerable<TeamsterFeature> AllFeatures => Matrix.Keys;

    public static FeatureClass ClassOf(TeamsterFeature feature)
    {
        return Matrix[feature].FeatureClass;
    }

    public static bool IsMutation(TeamsterFeature feature)
    {
        return Matrix[feature].FeatureClass == FeatureClass.Mutation;
    }

    /// <summary>May this feature ACT (mutate cart state) under the given
    /// authority? True only for a mutation feature under live local
    /// authority; observation features never "act", and every non-local or
    /// unknown authority denies mutation (fail closed).</summary>
    public static bool MayMutate(TeamsterFeature feature, CartAuthority authority)
    {
        return Matrix[feature].FeatureClass == FeatureClass.Mutation &&
            authority == CartAuthority.Local;
    }

    /// <summary>Should an observed reading of this feature be labeled as
    /// remote/stale? True only for owner-fresh observation features viewed
    /// without local authority. Mutation features are never observation-
    /// labeled; a locally-owned cart is always fresh.</summary>
    public static bool RequiresRemoteLabel(TeamsterFeature feature, CartAuthority authority)
    {
        return Matrix[feature].FeatureClass == FeatureClass.Observation &&
            Matrix[feature].LabelWhenRemote &&
            authority != CartAuthority.Local;
    }
}
