using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Naming a placed object, for as long as this run of the world lasts
/// and no longer.
///
/// <b>Why there is an epoch at all.</b> The obvious design — remember a chest's
/// network id and look it up again later — is wrong in this game, and quietly
/// so. <c>ZDO.Load</c> opens with <c>m_uid.SetID(++ZDOID.m_loadID)</c>: every
/// persisted object is handed a <i>fresh</i> id in load order, and
/// <c>SetID</c> also forces the user half to a constant. A key written before a
/// reload therefore names nothing afterwards — and because the new ids are
/// dense from one, it is <i>likely</i> to name some other object of the same
/// kind. A worker drawing wood out of whichever chest inherited id 37 is a
/// worse outcome than a worker who refuses, so a key carries the epoch it was
/// minted in and a key from another epoch resolves to nothing.
///
/// The format matches the one Concerned Foreman settled on
/// (<c>x16 user:x8 id</c>) because both were derived from the same decompile
/// and agreeing is free. Nothing is shared between the products: this is the
/// Steward's own copy, in the Steward's own assembly, and neither can change
/// the other's.</summary>
internal static class StewardIdentity
{
    /// <summary>A world-load epoch: minted once per load, opaque, and compared
    /// only for equality. Never persisted as anything but a comparison value.
    /// </summary>
    internal static string NewEpoch() => Guid.NewGuid().ToString("N");

    /// <summary>The object's identity, or null when it has none yet.
    ///
    /// Honours <c>m_rootObjectOverride</c> the way <c>Container.Awake</c> does,
    /// so a chest on a wagon is identified by the object vanilla considers to
    /// be it.</summary>
    internal static string? TryIdentify(Container? container)
    {
        if (container == null)
        {
            return null;
        }

        ZNetView? view = container.m_rootObjectOverride != null
            ? container.m_rootObjectOverride.GetComponent<ZNetView>()
            : container.GetComponent<ZNetView>();

        return TryIdentify(view);
    }

    internal static string? TryIdentify(Fireplace? fireplace) =>
        fireplace == null ? null : TryIdentify(fireplace.GetComponent<ZNetView>());

    internal static string? TryIdentify(ZNetView? view)
    {
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

    /// <summary>The loaded object with this identity, or null.
    ///
    /// <b>Found by scanning the scene's instances, not by constructing a
    /// <c>ZDOID</c> and looking it up.</b> That constructor registers an unknown
    /// user id in a game-wide table as a side effect, and a stale or mistyped
    /// key must not be able to change game state just by being asked about. The
    /// scan is over objects currently loaded, which in a settlement is small.
    /// </summary>
    internal static ZNetView? FindLoaded(string? key)
    {
        if (!TryParse(key, out long user, out uint id))
        {
            return null;
        }

        ZNetScene? scene = ZNetScene.instance;
        if (scene == null)
        {
            return null;
        }

        foreach (KeyValuePair<ZDO, ZNetView> instance in scene.m_instances)
        {
            ZDO zdo = instance.Key;
            if (zdo != null && zdo.m_uid.ID == id && zdo.m_uid.UserID == user && instance.Value != null)
            {
                return instance.Value;
            }
        }

        return null;
    }

    /// <summary>The loaded fire with this identity, re-identified rather than
    /// assumed: the key that found the object must still be the key the object
    /// produces, or it is not the same object.</summary>
    internal static Fireplace? FindFireplace(string? key)
    {
        ZNetView? view = FindLoaded(key);
        if (view == null)
        {
            return null;
        }

        Fireplace? fireplace = view.GetComponent<Fireplace>();
        return fireplace != null
            && string.Equals(TryIdentify(fireplace), key, StringComparison.Ordinal)
                ? fireplace
                : null;
    }

    /// <summary>The loaded container with this identity, re-identified the same
    /// way.</summary>
    internal static Container? FindContainer(string? key)
    {
        ZNetView? view = FindLoaded(key);
        if (view == null)
        {
            return null;
        }

        Container? container = view.GetComponent<Container>();
        return container != null
            && string.Equals(TryIdentify(container), key, StringComparison.Ordinal)
                ? container
                : null;
    }

    private static bool TryParse(string? key, out long user, out uint id)
    {
        user = 0;
        id = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        int colon = key!.IndexOf(':');
        if (colon <= 0
            || !ulong.TryParse(
                key.Substring(0, colon), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out ulong unsignedUser)
            || !uint.TryParse(
                key.Substring(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        user = unchecked((long)unsignedUser);
        return true;
    }
}
