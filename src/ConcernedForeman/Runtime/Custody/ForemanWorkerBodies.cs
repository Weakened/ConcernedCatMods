using System;
using TheConcernedCat.ConcernedForeman.Domain.Npc;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>Thorstein's saved worker body, as Concerned NPC's
/// <see cref="NpcBody"/> (#372, the body-inventory move).
///
/// <b>What moved.</b> Until #372 this product had its own copy of that
/// component, <c>WorkerBody</c>: the identity, carried inventory and revision
/// written into the body's own network object from the inventory's own change
/// callback, equipment restored on load, and vanilla's own drop of exactly what
/// he carried on a genuine death. The library's <see cref="NpcBody"/> was
/// derived from that copy and does the same, line for line, under the same
/// three keys - so a body saved by an older build loads unchanged, with no
/// migration code, and a body saved by this build loads in an older one.
///
/// <b>What stays here.</b> The three field names, spelled out, because this
/// product's own census and recovery read a saved body without loading it
/// (<c>WorldCustodyObjects</c>); the library composes the same names from the
/// role's key prefix, and <c>ForemanNpcAdoptionTests</c> fails if the two ever
/// disagree. And the lookup by key, filtered to Thorstein's own prefab, because
/// the library's lists and events are shared by every product that loads
/// it.</summary>
internal static class ForemanWorkerBodies
{
    /// <summary>The saved identity, exactly as shipped.</summary>
    internal const string KeyField = "tcc.worker.key";

    /// <summary>The saved inventory package, exactly as shipped.</summary>
    internal const string InventoryField = "tcc.worker.inventory";

    /// <summary>The saved revision, exactly as shipped.</summary>
    internal const string RevisionField = "tcc.worker.revision";

    // The last key asked for and the identity it parses to. FindLive is asked
    // every frame by the carried visual and the build pose, and parsing a key
    // allocates; the key itself never changes within a session.
    private static string? s_lastKey;
    private static NpcIdentity s_lastIdentity;

    /// <summary>Tells Concerned NPC what bodies of Thorstein's prefab store:
    /// identity and inventory, as they always have. Called with the prefab
    /// build, at the main menu, before any world's saved bodies start.
    /// Idempotent.</summary>
    internal static bool TryRegister(out string reason) =>
        NpcBody.TryRegister(ForemanNpcRole.Contract, NpcBodyKeeps.IdentityAndInventory, out reason);

    /// <summary>Whether <paramref name="body"/> is one of Thorstein's, rather
    /// than another product's body raised through the same shared
    /// event.</summary>
    internal static bool IsForeman(NpcBody? body) =>
        body != null
        && string.Equals(body.PrefabName, ForemanNpcRole.Contract.PrefabName, StringComparison.Ordinal);

    /// <summary>The loaded body stamped with <paramref name="key"/>, or null.
    /// Ordinal, as the copy this replaces matched; a key that is not a valid
    /// identity names no body, and this product never stamps one.</summary>
    internal static NpcBody? FindLive(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        NpcIdentity identity;
        if (string.Equals(key, s_lastKey, StringComparison.Ordinal))
        {
            identity = s_lastIdentity;
        }
        else
        {
            if (!NpcIdentity.TryParse(key, out identity))
            {
                return null;
            }

            s_lastIdentity = identity;
            s_lastKey = key;
        }

        return NpcBody.FindLive(ForemanNpcRole.Contract, identity);
    }
}
