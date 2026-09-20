using System;
using System.Collections.Generic;
using TheConcernedCat.ConcernedSteward.Domain.Quest;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>Counting the local player's resin, over the real game.
///
/// <b>Reads only.</b> There is nothing in this file that writes anything to
/// anything: no inventory call that adds or removes, no ZDO write, no patch, no
/// RPC. It asks three questions and answers with two numbers and a flag.
///
/// <b>The item identity comes from the game, not from this file.</b> Only the
/// prefab id is ours, and it is a setting rather than a constant for the same
/// reason <c>StewardSettings.BaseCreature</c> is: prefab names live in the
/// game's asset bundles, so no amount of reading the installed assembly can
/// prove one exists. The item's real name - the string vanilla itself compares
/// inventory entries against - is read off that prefab's own
/// <c>ItemDrop</c>. A wrong or renamed id is then a config edit and a logged
/// line, never a guess that compiles.
///
/// <b>Counted the way vanilla matches.</b> <c>Inventory.CountItems</c> filters
/// on <c>m_worldLevel</c> as well as the name, and the Steward's own audit
/// forbids this product calling it for exactly that reason: measuring with one
/// predicate and acting on another is how a count and the thing it counts stop
/// describing the same items. So the stacks are walked and matched on
/// <c>m_shared.m_name</c>, which is what <c>WorldFuelTargets</c> already
/// does.</summary>
internal sealed class ResinSightings
{
    private readonly Func<string> _prefabId;
    private readonly Action<string>? _log;

    private string _itemName = string.Empty;
    private string _resolvedFrom = string.Empty;
    private bool _complained;

    internal ResinSightings(Func<string> prefabId, Action<string>? log = null)
    {
        _prefabId = prefabId ?? throw new ArgumentNullException(nameof(prefabId));
        _log = log;
    }

    /// <summary>The item name resolved from the game, or empty while it could
    /// not be. Reported by the status command so a player with a renamed or
    /// missing prefab is told which of the two it is.</summary>
    internal string ItemName => _itemName;

    /// <summary>How much resin the local player is carrying, or <c>-1</c> when
    /// it could not be established - no player, no item db, no inventory.
    /// Negative is "I could not see", which the watch treats as forgetting
    /// rather than as none.</summary>
    internal int CountCarried()
    {
        string name = ResolveItemName();
        if (name.Length == 0)
        {
            return -1;
        }

        try
        {
            Player? player = Player.m_localPlayer;
            if (player == null)
            {
                return -1;
            }

            Inventory? inventory = player.GetInventory();
            if (inventory == null)
            {
                return -1;
            }

            int total = 0;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            if (items == null)
            {
                return -1;
            }

            foreach (ItemDrop.ItemData item in items)
            {
                if (item != null && item.m_shared != null && item.m_stack > 0
                    && string.Equals(item.m_shared.m_name, name, StringComparison.Ordinal))
                {
                    total += item.m_stack;
                }
            }

            return total;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Whether a window is open through which items move between
    /// containers.
    ///
    /// <b>Unreadable counts as open</b>, which is the fail-closed direction
    /// here: an unanswerable reading suppresses the beat rather than firing it
    /// on a withdrawal somebody made while this could not tell.</summary>
    internal bool AWindowIsOpen()
    {
        try
        {
            return InventoryGui.IsVisible();
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>The name vanilla itself matches on, read off the prefab the
    /// setting names. Resolved once and cached, and re-tried on every call until
    /// it succeeds, because the item database is not up at the main menu.
    /// </summary>
    private string ResolveItemName()
    {
        string id = SafeId();
        if (id.Length == 0)
        {
            return string.Empty;
        }

        if (_itemName.Length != 0 && string.Equals(_resolvedFrom, id, StringComparison.Ordinal))
        {
            return _itemName;
        }

        try
        {
            ObjectDB? database = ObjectDB.instance;
            if (database == null)
            {
                return string.Empty;
            }

            GameObject? prefab = database.GetItemPrefab(id);
            if (prefab == null)
            {
                Complain("There is no item called '" + id + "' in this game build, so " +
                    "Sunniva's flint and steel can never be found. Set [Steward] " +
                    "QuestPickupItem to the right prefab name.");
                return string.Empty;
            }

            ItemDrop? drop = prefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                Complain("'" + id + "' is not an item, so Sunniva's flint and steel can never " +
                    "be found. Set [Steward] QuestPickupItem to the right prefab name.");
                return string.Empty;
            }

            string name = drop.m_itemData.m_shared.m_name ?? string.Empty;
            if (name.Length == 0)
            {
                return string.Empty;
            }

            _itemName = name;
            _resolvedFrom = id;
            _complained = false;
            return name;
        }
        catch (Exception exception)
        {
            _log?.Invoke("Sunniva's quest item could not be looked up: " + exception.GetType().Name);
            return string.Empty;
        }
    }

    /// <summary>Once per configured id, not once per frame. This is asked from
    /// the tick.</summary>
    private void Complain(string message)
    {
        if (_complained)
        {
            return;
        }

        _complained = true;
        _log?.Invoke(message);
    }

    private string SafeId()
    {
        try
        {
            return _prefabId() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
