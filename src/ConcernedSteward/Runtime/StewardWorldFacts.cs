using System;
using TheConcernedCat.Settlement.Designations;
using TheConcernedCat.Settlement.Worker;
using TheConcernedCat.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>The questions the Steward has to ask the game before he may act,
/// each answered in one place and each failing closed.</summary>
internal static class StewardWorldFacts
{
    /// <summary>What the game says about who is running this world.
    ///
    /// <b>Unknown is not zero.</b> A peer count that could not be read comes
    /// back as <c>-1</c>, and <c>WorkAuthorityPolicy</c> refuses on anything but
    /// exactly zero — so a failure to count other players stops the Steward
    /// rather than convincing him he is alone.</summary>
    internal static WorkAuthorityFacts ReadAuthority(bool runtimeEnabled)
    {
        ZNet? net = ZNet.instance;
        if (net == null || ZNetScene.instance == null)
        {
            return new WorkAuthorityFacts(runtimeEnabled, false, false, false, 0);
        }

        int peers;
        try
        {
            peers = net.GetPeers().Count;
        }
        catch (Exception)
        {
            peers = -1;
        }

        bool server;
        bool dedicated;
        try
        {
            server = net.IsServer();
            dedicated = net.IsDedicated();
        }
        catch (Exception)
        {
            return new WorkAuthorityFacts(runtimeEnabled, true, false, true, -1);
        }

        return new WorkAuthorityFacts(runtimeEnabled, true, server, dedicated, peers);
    }

    internal static WorkAuthorityVerdict EvaluateAuthority(bool runtimeEnabled)
    {
        try
        {
            return WorkAuthorityPolicy.Evaluate(ReadAuthority(runtimeEnabled));
        }
        catch (Exception)
        {
            return WorkAuthorityVerdict.Unspecified;
        }
    }

    /// <summary>Which world this is, for addressing the Steward's record.
    ///
    /// Zero when there is no world, which every caller treats as "do not read
    /// or write anything": a record keyed to world zero would be shared by
    /// every world the player has.</summary>
    internal static long WorldId()
    {
        try
        {
            ZNet? net = ZNet.instance;
            return net == null ? 0L : net.GetWorldUID();
        }
        catch (Exception)
        {
            return 0L;
        }
    }

    internal static bool WorldIsUp => ZNet.instance != null && ZNetScene.instance != null;
}

/// <summary>Vanilla's ward check, behind the settlement layer's seam.
///
/// One question — does this ground belong to somebody else — and three possible
/// answers, of which two refuse. An exception, a world that is not up, or ground
/// the check could not see all of is <see cref="AreaAccess.Unavailable"/>, never
/// <see cref="AreaAccess.Granted"/>: the safe answer is the one you get by
/// default, not the one you have to remember to write.</summary>
internal sealed class StewardDesignationSite : IDesignationSite
{
    public AreaAccess CheckAccess(SitePoint centre, float radius)
    {
        if (!StewardWorldFacts.WorldIsUp)
        {
            return AreaAccess.Unavailable;
        }

        try
        {
            ZoneSystem? zones = ZoneSystem.instance;
            var point = new Vector3(centre.X, centre.Y, centre.Z);
            if (zones == null || !zones.IsZoneLoaded(point))
            {
                // A ward check over ground the game has not loaded is not a
                // check. Saying so is more useful than answering it anyway.
                return AreaAccess.Unavailable;
            }

            return PrivateArea.CheckAccess(point, radius, false, false)
                ? AreaAccess.Granted
                : AreaAccess.Denied;
        }
        catch (Exception)
        {
            return AreaAccess.Unavailable;
        }
    }
}
