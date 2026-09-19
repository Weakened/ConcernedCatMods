namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>The per-role choices the prefab build cannot make for everybody.
///
/// Everything else about the build is unified deliberately - the strip list,
/// the empty gear arrays, the faction, the persistence flag, the registration
/// call and the order they happen in. What is here is what differs between the
/// shipped copies for a reason rather than by drift.</summary>
internal readonly struct NpcWorkerPrefabOptions
{
    private readonly string? _displayName;

    internal NpcWorkerPrefabOptions(NpcBodyKeeps keeps, string? displayName)
    {
        Keeps = keeps;
        _displayName = displayName;
    }

    /// <summary>What this role's body stores in its own object. No default: see
    /// <see cref="NpcBodyKeeps"/>.</summary>
    internal NpcBodyKeeps Keeps { get; }

    /// <summary>The name the game shows for this body, or empty to keep the
    /// base creature's.
    ///
    /// One of the three shipped builds sets it and two do not, which is a real
    /// difference rather than drift: a role with a name the player knows should
    /// be called it, and a role whose body is anonymous should not be given a
    /// name this library invented. Empty is therefore "leave it alone", never
    /// "pick something".</summary>
    internal string DisplayName => _displayName ?? string.Empty;

    internal static NpcWorkerPrefabOptions Keeping(NpcBodyKeeps keeps) =>
        new NpcWorkerPrefabOptions(keeps, null);

    internal NpcWorkerPrefabOptions Named(string displayName) =>
        new NpcWorkerPrefabOptions(Keeps, displayName);
}
