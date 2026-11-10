using System;
using TheConcernedCat.ConcernedNPC.Body;
using TheConcernedCat.ConcernedNPC.Roles;
using TheConcernedCat.ConcernedSteward.Domain.Npc;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Sunniva's saved worker body, as Concerned NPC's
/// <see cref="NpcBody"/> (#372, the body-inventory move, F2e).
///
/// <b>What moved.</b> Until #372 this product had its own copy of that
/// component, <c>StewardBody</c>: the identity, carried pack and revision
/// written into the body's own network object from the inventory's own change
/// callback, and vanilla's own drop of exactly what she carried on a genuine
/// death. The library's <see cref="NpcBody"/> does the same under the same
/// three keys, so a body saved by an older build loads unchanged, with no
/// migration code (<c>CensusAndBodyTests</c> loads a hand-written pre-#372
/// object). The other direction - a body saved by this build loading in an
/// older one - rests on the same keys, package and revision count, and is not
/// separately tested. The one
/// thing the library adds is putting an item saved as equipped back in her
/// hands on load; she never equips anything, so for her that changes nothing.
///
/// <b>What stays here.</b> The three field names, spelled out, because this
/// product's own census reads saved bodies without loading them
/// (<see cref="StewardCensus"/>); the library composes the same names from the
/// role's key prefix, and <c>SunnivaTests</c> fails if the two ever disagree.
/// And the lookup by key, filtered to her own prefab - because the library's
/// lists and events are shared by every product that loads it - and to a body
/// whose network object is still valid, which the copy this replaces did and
/// the library's own lookup does not.</summary>
internal static class StewardSavedBodies
{
    /// <summary>The saved identity, exactly as shipped.</summary>
    internal const string KeyField = "tcc.steward.key";

    /// <summary>The saved pack, exactly as shipped.</summary>
    internal const string InventoryField = "tcc.steward.inventory";

    /// <summary>The saved revision, exactly as shipped.</summary>
    internal const string RevisionField = "tcc.steward.revision";

    // The last key asked for and the identity it parses to. FindLive is asked
    // every frame by the census, and parsing a key allocates; the key itself
    // never changes within a session.
    private static string? s_lastKey;
    private static NpcIdentity s_lastIdentity;

    /// <summary>Tells Concerned NPC what bodies of her prefab store: identity
    /// and pack, as they always have. Called with the prefab build, at plugin
    /// start, before any world's saved bodies start. Idempotent.</summary>
    internal static bool TryRegister(out string reason) =>
        NpcBody.TryRegister(StewardNpcRole.Contract, NpcBodyKeeps.IdentityAndInventory, out reason);

    /// <summary>Whether <paramref name="body"/> is one of hers, rather than
    /// another product's body raised through the same shared event.</summary>
    internal static bool IsSteward(NpcBody? body) =>
        body != null
        && string.Equals(body.PrefabName, StewardNpcRole.Contract.PrefabName, StringComparison.Ordinal);

    /// <summary>The loaded body stamped with <paramref name="key"/>, or null.
    /// Ordinal, as the copy this replaces matched; a key that is not a valid
    /// identity names no body, and this product never stamps one.
    ///
    /// <b>A body whose network object has already been let go of is not a
    /// body any more.</b> Vanilla's removal resets the object at once, while
    /// Unity only destroys the component at the end of the frame. Without this
    /// the census could adopt a just-retired Sunniva for the rest of the frame
    /// and a round could be opened against a body that is gone (S1).
    ///
    /// Unlike the copy's loop, this does not go on to a second body with the
    /// same identity: two loaded bodies under one identity make the census
    /// answer <c>Duplicated</c> before this is asked, and she does not work.
    /// </summary>
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

        NpcBody? body = NpcBody.FindLive(StewardNpcRole.Contract, identity);
        if (body == null)
        {
            return null;
        }

        ZNetView? view = body.GetComponent<ZNetView>();
        return view == null || !view.IsValid() ? null : body;
    }
}
