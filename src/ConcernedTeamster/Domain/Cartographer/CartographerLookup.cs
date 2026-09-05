namespace TheConcernedCat.ConcernedTeamster.Domain.Cartographer;

/// <summary>What the plugin registry said about Concerned Cartographer
/// (CT-021). Produced by the adapter from BepInEx's plugin infos and by
/// tests from fake registries; the gate never touches BepInEx itself.
/// System.Version is spelled out because Valheim ships a global Version
/// type that would otherwise capture the name in the plugin build.</summary>
public sealed class CartographerLookup
{
    private CartographerLookup(bool found, System.Version? version, object? instance)
    {
        Found = found;
        Version = version;
        Instance = instance;
    }

    /// <summary>True when a plugin with the Cartographer GUID is registered.</summary>
    public bool Found { get; }

    /// <summary>The registered plugin version, or null when the registry
    /// carried none (treated as a version mismatch, never a guess).</summary>
    public System.Version? Version { get; }

    /// <summary>The live plugin instance the contract is probed against.</summary>
    public object? Instance { get; }

    public static CartographerLookup NotFound()
    {
        return new CartographerLookup(false, null, null);
    }

    public static CartographerLookup Detected(System.Version? version, object? instance)
    {
        return new CartographerLookup(true, version, instance);
    }
}
