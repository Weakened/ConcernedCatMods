namespace TheConcernedCat.Workers;

/// <summary>Whether this process may run a worker that changes real resources
/// or moves a real cart. Zero is the unspecified value: a verdict nobody
/// computed is never a grant.</summary>
internal enum WorkAuthorityVerdict
{
    Unspecified = 0,
    Granted = 1,

    /// <summary>The product's worker setting is off (the default).</summary>
    RuntimeDisabled = 2,

    /// <summary>No world is loaded.</summary>
    NoWorld = 3,

    /// <summary>This process is a client of someone else's world.</summary>
    NotHost = 4,

    /// <summary>A dedicated server has no local player to order or watch work;
    /// unsupported in this slice.</summary>
    DedicatedServer = 5,

    /// <summary>Other players are connected. Shared work needs a compatible-peer
    /// handshake and an ownership policy that do not exist yet, so it refuses
    /// rather than letting ownership of a worker or cart migrate to a client.
    /// </summary>
    OtherPeersConnected = 6,
}

/// <summary>What the game said, gathered by a product adapter at the moment of
/// asking. A negative peer count means "could not tell".</summary>
internal readonly struct WorkAuthorityFacts
{
    public WorkAuthorityFacts(bool runtimeEnabled, bool worldLoaded, bool isServer, bool isDedicated, int connectedPeers)
    {
        RuntimeEnabled = runtimeEnabled;
        WorldLoaded = worldLoaded;
        IsServer = isServer;
        IsDedicated = isDedicated;
        ConnectedPeers = connectedPeers;
    }

    public bool RuntimeEnabled { get; }

    public bool WorldLoaded { get; }

    public bool IsServer { get; }

    public bool IsDedicated { get; }

    public int ConnectedPeers { get; }
}

/// <summary>The single authority rule of the first cart and collection proof
/// (DECISIONS.md D3): opted in, a loaded world, the host, not dedicated, and
/// nobody else connected. Asked before every mutation, not once at order
/// creation.</summary>
internal static class WorkAuthorityPolicy
{
    public static WorkAuthorityVerdict Evaluate(WorkAuthorityFacts facts)
    {
        if (!facts.RuntimeEnabled)
        {
            return WorkAuthorityVerdict.RuntimeDisabled;
        }

        if (!facts.WorldLoaded)
        {
            return WorkAuthorityVerdict.NoWorld;
        }

        if (!facts.IsServer)
        {
            return WorkAuthorityVerdict.NotHost;
        }

        if (facts.IsDedicated)
        {
            return WorkAuthorityVerdict.DedicatedServer;
        }

        // Unknown is not zero.
        if (facts.ConnectedPeers != 0)
        {
            return WorkAuthorityVerdict.OtherPeersConnected;
        }

        return WorkAuthorityVerdict.Granted;
    }

    /// <summary>One sentence a player can act on.</summary>
    public static string Describe(WorkAuthorityVerdict verdict)
    {
        switch (verdict)
        {
            case WorkAuthorityVerdict.Granted:
                return "Work is allowed here.";
            case WorkAuthorityVerdict.RuntimeDisabled:
                return "Workers are turned off in this mod's settings.";
            case WorkAuthorityVerdict.NoWorld:
                return "No world is loaded.";
            case WorkAuthorityVerdict.NotHost:
                return "Only the host of this world can give workers orders.";
            case WorkAuthorityVerdict.DedicatedServer:
                return "Workers do not run on a dedicated server yet.";
            case WorkAuthorityVerdict.OtherPeersConnected:
                return "Workers only work in single player or while nobody else is connected, for now.";
            default:
                return "Work authority could not be established.";
        }
    }
}
