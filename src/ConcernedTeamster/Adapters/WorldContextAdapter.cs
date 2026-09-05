using System.Runtime.CompilerServices;

namespace TheConcernedCat.ConcernedTeamster.Adapters;

/// <summary>Read-only world identity (CT-016): the world UID that keys
/// every Teamster sidecar, from the same verified surface Cartographer
/// ships (<c>ZNet.instance.GetWorldUID()</c>, probed at startup). Fails
/// closed to false — persistence simply waits until the world is known.</summary>
public static class WorldContextAdapter
{
    public static bool TryGetWorldUid(out long worldUid)
    {
        worldUid = 0L;
        if (!CartAdapter.CapabilityEnabled)
        {
            return false;
        }

        try
        {
            return GetWorldUidCore(out worldUid);
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool GetWorldUidCore(out long worldUid)
    {
        worldUid = 0L;
        ZNet net = ZNet.instance;
        if (net == null)
        {
            return false;
        }

        worldUid = net.GetWorldUID();
        return worldUid != 0L;
    }
}
