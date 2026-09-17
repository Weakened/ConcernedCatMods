using System;
using TheConcernedCat.ConcernedTeamster.Domain.Hauling.Execution;
using TheConcernedCat.Workers;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>Gathers the work-authority facts at the moment of asking
/// (DECISIONS.md D3) and applies the one shared rule. Asked before every
/// mutation: attach, every motor command, every lease.
///
/// Hauling is opted in only when both Teamster's master switch and
/// <c>Workers/GunnarHaulingEnabled</c> are on and the pull strength is the one
/// supported value. Anything unreadable refuses: a missing <c>ZNet</c> is "no
/// world", an unreadable peer list is "somebody might be connected".</summary>
internal sealed class GunnarWorkAuthority : IHaulAuthority
{
    private readonly TeamsterSettings _settings;

    public GunnarWorkAuthority(TeamsterSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public WorkAuthorityVerdict Evaluate() => WorkAuthorityPolicy.Evaluate(ReadFacts());

    internal WorkAuthorityFacts ReadFacts()
    {
        bool enabled;
        try
        {
            enabled = _settings.Enabled.Value &&
                _settings.GunnarHaulingEnabled.Value &&
                _settings.GunnarPullStrength.Value == GunnarPullStrength.MatchPlayer;
        }
        catch
        {
            enabled = false;
        }

        try
        {
            ZNet net = ZNet.instance;
            bool world = net != null && ZNetScene.instance != null;
            bool server = net != null && net.IsServer();
            bool dedicated = net != null && net.IsDedicated();
            int peers = -1;
            if (net != null && net.GetPeers() != null)
            {
                peers = net.GetPeers().Count;
            }

            return new WorkAuthorityFacts(enabled, world, server, dedicated, peers);
        }
        catch
        {
            return new WorkAuthorityFacts(enabled, worldLoaded: false, isServer: false, isDedicated: false, connectedPeers: -1);
        }
    }
}
