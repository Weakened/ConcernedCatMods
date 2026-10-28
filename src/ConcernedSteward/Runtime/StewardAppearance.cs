using System;
using TheConcernedCat.ConcernedSteward.Domain.Appearance;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Putting Sunniva's look on her own body.
///
/// <b>Everything here writes into the Steward's OWN network object and nothing
/// else.</b> That is what a worker body is for — `CLAUDE.md` allows a worker to
/// keep its identity and its own state in its own object — and it is the same
/// object <c>StewardBody</c> already writes the identity and the pack into.
/// Nothing in this file touches a vanilla object, a player, a piece or the
/// world.
///
/// <b>And it writes through vanilla's own <c>VisEquipment</c>, never through the
/// ZDO.</b> That matters for a reason beyond taste: <c>VisEquipment</c> owns the
/// keys it writes, keeps the visual in step with them, and refuses an index its
/// own model array does not have. Writing the same keys by hand would be this
/// product taking ownership of vanilla's format, which is the thing the whole
/// repository is careful not to do.
///
/// <b>Every part of it fails closed and independently.</b> A hair prefab this
/// build does not have does not stop the clothing; clothing that does not
/// resolve does not stop the colour; none of it stops the Steward existing. The
/// worst case is a Steward who looks like the base creature, which is what every
/// Steward has looked like until now.</summary>
internal sealed class StewardAppearance
{
    private readonly Func<StewardLook> _look;
    private readonly Action<string>? _log;

    // Each distinct complaint once, rather than the first complaint only: a
    // body that is not player-style and a hair name that does not exist are
    // two different things to fix.
    private readonly System.Collections.Generic.HashSet<string> _complained =
        new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

    internal StewardAppearance(Func<StewardLook> look, Action<string>? log = null)
    {
        _look = look ?? throw new ArgumentNullException(nameof(look));
        _log = log;
    }

    /// <summary>What she was actually dressed in, for the status command and for
    /// a bug report. Empty until a body has been dressed.</summary>
    internal string Wearing { get; private set; } = string.Empty;

    /// <summary>Dresses one body. Called once when a body binds; safe to call
    /// again, because every write here is idempotent and vanilla's own setters
    /// return early when nothing changed.</summary>
    /// <returns>True when anything at all was applied.</returns>
    internal bool Apply(GameObject? body)
    {
        if (body == null)
        {
            return false;
        }

        StewardLook look;
        try
        {
            look = _look();
        }
        catch (Exception)
        {
            return false;
        }

        if (!look.SaysAnything)
        {
            return false;
        }

        VisEquipment? vis;
        ZNetView? view;
        try
        {
            vis = body.GetComponent<VisEquipment>();
            view = body.GetComponent<ZNetView>();
        }
        catch (Exception)
        {
            return false;
        }

        if (vis == null || view == null || !view.IsValid())
        {
            Complain(
                "The Steward's body has nothing to dress, so she looks like whatever she was " +
                "cloned from.");
            return false;
        }

        if (!view.IsOwner())
        {
            // A write by a non-owner is discarded without telling anybody. The
            // same rule the fire adapter follows, for the same reason.
            return false;
        }

        // #382, from the decompiled 1.0.14 VisEquipment: the model index, the
        // hair, the beard and both colours are only ever drawn when the
        // component is a player-style one (m_isPlayer). On any other base
        // creature they are written and silently ignored, so the player is told
        // once, in the log, that only the clothing will show - instead of being
        // left to wonder why she is not blonde.
        if (!IsPlayerStyle(vis))
        {
            Complain(
                "The Steward's base creature is not a player-style body, so vanilla will not draw " +
                "her model choice, hair or colouring; only her clothing shows. Choosing a different " +
                "[Steward] BaseCreature is an owner decision and needs looking at in game.");
        }

        bool applied = false;
        applied |= TrySetModel(vis, look.ModelIndex);
        applied |= TrySetHair(vis, look.HairItem);
        applied |= TrySetColours(vis, look);
        applied |= TryDress(vis, look);
        return applied;
    }

    // ------------------------------------------------------------------

    private bool TrySetModel(VisEquipment vis, int index)
    {
        if (index <= 0)
        {
            return false;
        }

        try
        {
            // Vanilla refuses an index outside its own model array, so a base
            // creature with only one model simply stays as it is rather than
            // throwing.
            vis.SetModel(index);
            return true;
        }
        catch (Exception exception)
        {
            Complain("The Steward's model could not be set: " + exception.GetType().Name);
            return false;
        }
    }

    private bool TrySetHair(VisEquipment vis, string hair)
    {
        if (hair.Length == 0)
        {
            return false;
        }

        // A "$customization_..." token is looked up among the game's own hair
        // items (#382: the owner chose the style the character screen calls
        // Matronly, which is $customization_hair12 in this build's own
        // localization table). Anything else is taken as a prefab name and
        // must exist. Either way a miss substitutes nothing.
        string? prefab = ResolveHairPrefab(hair);
        if (prefab == null)
        {
            Complain("'" + hair + "' is not hair this game build has, so the Steward's hair was " +
                "left as it is. Check [Appearance] HairStyle.");
            return false;
        }

        try
        {
            vis.SetHairItem(Hash(prefab));
            return true;
        }
        catch (Exception exception)
        {
            Complain("'" + hair + "' is not hair this game build has: " + exception.GetType().Name);
            return false;
        }
    }

    private bool TrySetColours(VisEquipment vis, in StewardLook look)
    {
        bool applied = false;
        try
        {
            if (look.HairColour != null)
            {
                vis.SetHairColor(ToVector(look.HairColour.Value));
                applied = true;
            }

            if (look.SkinColour != null)
            {
                vis.SetSkinColor(ToVector(look.SkinColour.Value));
                applied = true;
            }
        }
        catch (Exception exception)
        {
            Complain("The Steward's colouring could not be set: " + exception.GetType().Name);
        }

        return applied;
    }

    /// <summary>The priority #382 sets, resolved against the game rather than
    /// assumed: the first outfit this build actually has, and the leather that
    /// is always last.</summary>
    private bool TryDress(VisEquipment vis, in StewardLook look)
    {
        foreach (Outfit outfit in look.Clothing)
        {
            if (!Exists(outfit.Chest) || (outfit.Legs.Length != 0 && !Exists(outfit.Legs)))
            {
                continue;
            }

            try
            {
                vis.SetChestItem(Hash(outfit.Chest));
                if (outfit.Legs.Length != 0)
                {
                    vis.SetLegItem(Hash(outfit.Legs));
                }

                Wearing = outfit.Describe;
                return true;
            }
            catch (Exception exception)
            {
                Complain(
                    "The Steward could not be dressed in " + outfit.Describe + ": " +
                    exception.GetType().Name);
                return false;
            }
        }

        Complain(
            "None of the clothing the Steward was to wear exists in this game build, so she is " +
            "wearing whatever she was cloned from. Check [Appearance] Garment.");
        return false;
    }

    /// <summary>Whether this game build has an item by that name.
    ///
    /// Asked of the game's own item database, never assumed. An item prefab name
    /// is asset-bundle data, so this is the only honest way to find out, and
    /// "no" is an ordinary answer rather than a failure.</summary>
    private static bool Exists(string prefabName)
    {
        if (prefabName.Length == 0)
        {
            return false;
        }

        try
        {
            ObjectDB? database = ObjectDB.instance;
            return database != null && database.GetItemPrefab(prefabName) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A prefab name as the equipment slots take it.
    ///
    /// <c>VisEquipment</c>'s setters take a hash rather than a name, and the
    /// hash is vanilla's own <c>GetStableHashCode</c> - the same function the
    /// game uses to turn a name into the number it stores in a ZDO. Computing
    /// it any other way would produce a number nothing recognises, and an
    /// invisible garment rather than an error.</summary>
    private static int Hash(string prefabName) => prefabName.GetStableHashCode();

    private static bool IsPlayerStyle(VisEquipment vis)
    {
        try
        {
            return vis.m_isPlayer;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The prefab name for a hair setting: a customization token is
    /// matched against the shared name of every customization item the game
    /// has; a plain name must be an item the game has. Null when neither.
    /// </summary>
    private static string? ResolveHairPrefab(string hair)
    {
        try
        {
            ObjectDB? database = ObjectDB.instance;
            if (database == null)
            {
                return null;
            }

            if (!hair.StartsWith("$", StringComparison.Ordinal))
            {
                return database.GetItemPrefab(hair) != null ? hair : null;
            }

            foreach (GameObject? item in database.m_items)
            {
                if (item == null)
                {
                    continue;
                }

                ItemDrop? drop = item.GetComponent<ItemDrop>();
                ItemDrop.ItemData.SharedData? shared = drop?.m_itemData?.m_shared;
                if (shared != null
                    && shared.m_itemType == ItemDrop.ItemData.ItemType.Customization
                    && string.Equals(shared.m_name, hair, StringComparison.Ordinal))
                {
                    return item.name;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Vector3 ToVector(in LookColour colour) =>
        new Vector3(colour.Red, colour.Green, colour.Blue);

    /// <summary>Once, not once a frame. This is reached from the tick that binds
    /// a body, and a base creature with no hair would otherwise write the same
    /// line into a player's log for the length of the session.</summary>
    private void Complain(string message)
    {
        if (_complained.Count > 32 || !_complained.Add(message))
        {
            return;
        }

        _log?.Invoke(message);
    }
}
