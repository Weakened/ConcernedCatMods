using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Turning "what the player is doing right now" into something the
/// game-free layer can be given.
///
/// <b>A chest is chosen by looking at it</b> — the same targeting vanilla uses
/// for every interaction — and never by being the nearest one. The difference
/// is the whole of #340's rule against a nearest-global chest: "the nearest"
/// re-points itself every time somebody builds something, and a supply
/// designation that moves on its own is a designation the player did not make.
/// </summary>
internal static class StewardTargets
{
    /// <summary>The container the local player is looking at.
    ///
    /// Returns false with a sentence the player can act on. It never falls back
    /// to a search: if they are not looking at a chest, the answer is "look at
    /// one", not a guess about which one was meant.</summary>
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

        key = StewardIdentity.TryIdentify(container);
        if (key == null)
        {
            container = null;
            failure = "that container could not be identified — it may still be being placed. " +
                "Nothing was marked, because a designation that resolved by position would " +
                "follow whatever ends up standing there.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>Where the local player is standing, for marking a settlement
    /// area around them. Null when there is no player.</summary>
    internal static Vector3? LocalPlayerPosition()
    {
        Player player = Player.m_localPlayer;
        return player == null ? (Vector3?)null : player.transform.position;
    }
}
