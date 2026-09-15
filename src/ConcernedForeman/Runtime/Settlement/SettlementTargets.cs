using System.Globalization;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Settlement;

/// <summary>Turning "what the player is doing right now" into something the
/// game-free layer can be given.
///
/// Every designation is a deliberate act, so the target of one has to be
/// unambiguous. A chest is chosen by <b>looking at it</b> — the same targeting
/// vanilla uses for every interaction — and not by being the nearest one. The
/// difference matters: "the nearest chest" is exactly the inference from
/// proximity that CF-SET-004 forbids, and it is also the behaviour that would
/// quietly re-point a settlement's supply at a chest somebody built later.</summary>
internal static class SettlementTargets
{
    /// <summary>Resolves the container the local player is looking at.
    ///
    /// Returns false with a sentence the player can act on. It never falls back
    /// to a search: if the player is not looking at a chest, the answer is "look
    /// at one", not a guess about which one was meant.</summary>
    internal static bool TryResolveHoveredContainer(
        out Container? container, out string? key, out string failure)
    {
        container = null;
        key = null;

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            failure = "there is no local player.";
            return false;
        }

        GameObject hovering = player.GetHoverObject();
        if (hovering == null)
        {
            failure = "you are not looking at anything. Stand where you can see the chest and " +
                "look straight at it.";
            return false;
        }

        container = hovering.GetComponentInParent<Container>();
        if (container == null)
        {
            failure = "that is not a container.";
            return false;
        }

        key = TryIdentify(container);
        if (key == null)
        {
            container = null;
            failure = "that container has no stable identity yet — it may still be being placed. " +
                "Nothing was marked, because a designation that resolved by position would " +
                "follow whatever ends up standing there.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>The container's own identity, as a string — for as long as this
    /// run of the world lasts, and no longer.
    ///
    /// Verified against the installed 1.0.12 build: <c>ZDO.m_uid</c> is a public
    /// <c>ZDOID</c> field, and <c>ZDOID</c> exposes <c>public long UserID</c>
    /// and <c>public uint ID</c>.
    ///
    /// <b>What is NOT true, and what this comment used to claim:</b> that a
    /// <c>ZDOID</c> is assigned at creation and travels with the object in the
    /// world save. <c>ZDO.Load</c> opens with
    /// <c>m_uid.SetID(++ZDOID.m_loadID)</c> — every persisted object is handed a
    /// fresh id in load order — and <c>SetID</c> also forces the user half to a
    /// constant. So a key produced here means nothing after the next reload,
    /// and because the new ids are dense from one it is likely to match some
    /// <i>other</i> chest.
    ///
    /// That is why a designation records the identity epoch it was made in, and
    /// why a key from an earlier epoch resolves to nothing rather than to a
    /// guess. Nothing may treat this string as durable.
    ///
    /// The view is resolved exactly the way <c>Container.Awake</c> resolves its
    /// own, honouring <c>m_rootObjectOverride</c>, so a chest on a wagon is
    /// identified by the same object vanilla considers to be it.</summary>
    internal static string? TryIdentify(Container container)
    {
        if (container == null)
        {
            return null;
        }

        ZNetView view = container.m_rootObjectOverride != null
            ? container.m_rootObjectOverride.GetComponent<ZNetView>()
            : container.GetComponent<ZNetView>();

        if (view == null || !view.IsValid())
        {
            return null;
        }

        ZDO zdo = view.GetZDO();
        if (zdo == null || zdo.m_uid == ZDOID.None)
        {
            return null;
        }

        return unchecked((ulong)zdo.m_uid.UserID).ToString("x16", CultureInfo.InvariantCulture)
            + ":" + zdo.m_uid.ID.ToString("x8", CultureInfo.InvariantCulture);
    }
}
