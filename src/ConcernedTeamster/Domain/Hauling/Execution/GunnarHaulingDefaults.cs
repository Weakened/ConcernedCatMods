namespace TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;

/// <summary>How strongly Gunnar pulls (DECISIONS.md D5). Zero is unspecified.
///
/// The vanilla motor restores up to 20 m/s of velocity per physics step
/// whatever the body weighs, so a puller's <c>Rigidbody.mass</c> is its
/// pulling strength. There is one supported value in this slice: Gunnar's body
/// weighs what the local player's body weighs, and the cart's own attach adds
/// its player pull mass on top of that, exactly as it does for the player.
/// Anything else would be a strength vanilla never gives anyone.</summary>
internal enum GunnarPullStrength
{
    Unspecified = 0,

    /// <summary>Gunnar's base body mass equals the local player's base body
    /// mass, measured in game, never assumed.</summary>
    MatchPlayer = 1,
}

/// <summary>Every default of the Gunnar worker runtime in one place, so the
/// settings binding only reads them and a test can lock them
/// (<c>HaulingExecutionSettingsTests</c>).
///
/// Hauling is <b>off</b> by default (DECISIONS.md D3): installing Teamster for its
/// telemetry must never enrol a player in a feature that moves a real cart.
/// </summary>
internal static class GunnarHaulingDefaults
{
    /// <summary><c>Workers/GunnarHaulingEnabled</c>. Off until a person turns it
    /// on.</summary>
    public const bool HaulingEnabled = false;

    /// <summary><c>Workers/GunnarPullStrength</c>, the only supported value.
    /// </summary>
    public const GunnarPullStrength PullStrength = GunnarPullStrength.MatchPlayer;

    /// <summary><c>Workers/WorkerBaseCreature</c>. A creature prefab name is data
    /// in the game's asset bundles, not an API, so it is configurable and
    /// resolved at run time, failing closed when it does not exist.</summary>
    public const string WorkerBaseCreature = "Dverger";

    /// <summary>The name Gunnar's worker prefab is registered under. It is part
    /// of every saved world that contains his body, so it never changes.
    /// </summary>
    public const string WorkerPrefabName = "CT_TeamsterWorker";

    /// <summary>The field in the worker body's <b>own</b> network object that
    /// carries its identity (CONTRACTS.md §2.6, DECISIONS.md D9). The only
    /// network-object field this product ever writes.</summary>
    public const string WorkerKeyField = "tcc.worker.key";

    /// <summary>The prefix of every key Gunnar's body stores itself beneath in
    /// its own network object, trailing dot included.
    ///
    /// <b>Durable, and shared with Concerned Foreman by design.</b> Two products
    /// may use one key prefix - they already do - because the prefab name is
    /// what separates their bodies before a key is ever read. Changing this
    /// leaves a saved body standing and empties it, with no evidence anywhere,
    /// so it never changes. <c>WorkerKeyPrefixIsTheFieldsOwnPrefix</c> pins that
    /// it and <see cref="WorkerKeyField"/> cannot drift apart.</summary>
    public const string WorkerKeyPrefix = "tcc.worker.";

    /// <summary>The name shown over his body.</summary>
    public const string WorkerDisplayName = "Gunnar";
}
