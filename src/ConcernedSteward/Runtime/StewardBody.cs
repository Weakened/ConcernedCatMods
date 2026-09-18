using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheConcernedCat.ConcernedSteward.Runtime;

/// <summary>One item a dying Steward put on the ground.</summary>
internal readonly struct DroppedItem
{
    internal DroppedItem(string sharedName, int count)
    {
        SharedName = sharedName ?? string.Empty;
        Count = count;
    }

    public string SharedName { get; }

    public int Count { get; }
}

/// <summary>The Steward's body, and the only thing in this product that writes
/// to a network object.
///
/// <b>Why the body has to persist itself at all.</b> Vanilla never saves a
/// non-player humanoid's inventory: <c>Humanoid.m_inventory</c> is a plain
/// field with no save or load, rebuilt on every instantiation. A worker's
/// carried wood would be destroyed by a relog, a zone unload or a despawn,
/// while the upkeep record still said he was holding it — and a record that
/// disagrees with the world about a player's materials is the one failure this
/// product exists to avoid.
///
/// <b>It writes to its own object and never to a vanilla one.</b> The three
/// fields below live on the Steward's own prefab's ZDO. Nothing is written into
/// a chest, a fire, a piece or a player (`CLAUDE.md`: mod data is never written
/// into a vanilla object).
///
/// <b>Inside the same synchronous call.</b> The inventory is written from the
/// inventory's own change callback — the exact mechanism <c>Container</c> uses
/// — so every add and remove is persisted inside the call that made it, before
/// any receipt is recorded. A withdrawal, the walk and the feed therefore share
/// one snapshot, and <see cref="LastChangePersisted"/> going false is what makes
/// a transfer uncertain rather than completed.
///
/// <b>Nothing is loaded over.</b> A body that could not read what it carries is
/// inert: it does not work, and it does not save an empty inventory over a full
/// one.</summary>
internal sealed class StewardBody : MonoBehaviour
{
    internal const string KeyField = "tcc.steward.key";
    internal const string InventoryField = "tcc.steward.inventory";
    internal const string RevisionField = "tcc.steward.revision";

    private static readonly List<StewardBody> s_live = new List<StewardBody>();

    private ZNetView? _view;
    private Humanoid? _humanoid;
    private Inventory? _inventory;
    private bool _loading;
    private Action? _onChanged;
    private Action? _onDeath;

    /// <summary>Raised once a body has loaded its stored inventory and is ready
    /// to be bound: at spawn, at world load, and whenever its ground loads
    /// again.</summary>
    internal static Action<StewardBody>? Loaded { get; set; }

    /// <summary>Raised on death, after every carried item is on the ground and
    /// before the body goes. The runtime records where, so the upkeep ledger
    /// can say what left his hands rather than quietly balancing.</summary>
    internal static Action<StewardBody, IReadOnlyList<DroppedItem>, Vector3>? Died { get; set; }

    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>Bodies alive in this scene that finished loading.</summary>
    internal static IReadOnlyList<StewardBody> Live
    {
        get
        {
            s_live.RemoveAll(body => body == null);
            return s_live;
        }
    }

    internal static StewardBody? FindLive(string key)
    {
        foreach (StewardBody body in Live)
        {
            if (string.Equals(body.Key, key, StringComparison.Ordinal))
            {
                return body;
            }
        }

        return null;
    }

    /// <summary>Drops every tracked body. Called when a world goes away, so a
    /// second world in the same session does not inherit the first one's.
    /// </summary>
    internal static void ForgetAll() => s_live.Clear();

    internal string Key { get; private set; } = string.Empty;

    internal bool IsLoaded { get; private set; }

    /// <summary>Why this body is inert, or null.</summary>
    internal string? Fault { get; private set; }

    /// <summary>False when the most recent inventory change could not be
    /// written to the body's own object. Any step involving the Steward is then
    /// uncertain, never completed.</summary>
    internal bool LastChangePersisted { get; private set; } = true;

    internal int Revision { get; private set; }

    internal Humanoid? Humanoid => _humanoid;

    internal Inventory? Inventory => _inventory;

    internal ZNetView? View => _view;

    internal bool IsOwned => _view != null && _view.IsValid() && _view.IsOwner();

    private void Start()
    {
        try
        {
            _view = GetComponent<ZNetView>();
            _humanoid = GetComponent<Humanoid>();
            if (_view == null || !_view.IsValid() || _humanoid == null)
            {
                Fault = "the body has no valid network object or no humanoid";
                return;
            }

            ZDO zdo = _view.GetZDO();
            Key = zdo.GetString(KeyField, string.Empty);
            Revision = zdo.GetInt(RevisionField, 0);
            _inventory = _humanoid.GetInventory();

            byte[]? stored = zdo.GetByteArray(InventoryField, null);
            _loading = true;
            try
            {
                if (stored != null && stored.Length > 0)
                {
                    _inventory.Load(new ZPackage(stored));
                }
            }
            finally
            {
                _loading = false;
            }

            _onChanged = OnInventoryChanged;
            _inventory.m_onChanged = (Action)Delegate.Combine(_inventory.m_onChanged, _onChanged);
            _onDeath = OnDeath;
            _humanoid.m_onDeath = (Action)Delegate.Combine(_humanoid.m_onDeath, _onDeath);

            IsLoaded = true;
            s_live.Add(this);
            Loaded?.Invoke(this);
        }
        catch (Exception exception)
        {
            // Inert, never half-loaded. A body that could not read what it
            // carries must not work, and must not save over what it carries.
            IsLoaded = false;
            Fault = "its stored inventory could not be loaded (" + exception.GetType().Name + ")";
            ErrorLog?.Invoke("The Steward's body is inert: " + Fault + ". " + exception);
        }
    }

    private void OnDestroy()
    {
        s_live.Remove(this);

        if (_inventory != null && _onChanged != null)
        {
            _inventory.m_onChanged = (Action?)Delegate.Remove(_inventory.m_onChanged, _onChanged);
        }

        if (_humanoid != null && _onDeath != null)
        {
            _humanoid.m_onDeath = (Action?)Delegate.Remove(_humanoid.m_onDeath, _onDeath);
        }
    }

    /// <summary>Stamps a freshly spawned body with its identity, before its
    /// first <c>Start</c>.</summary>
    internal static bool TryStamp(GameObject instance, string key)
    {
        ZNetView? view = instance.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return false;
        }

        ZDO zdo = view.GetZDO();
        zdo.Set(KeyField, key);
        zdo.Set(RevisionField, 0);
        return true;
    }

    internal int ItemCount => _inventory == null ? 0 : _inventory.NrOfItems();

    /// <summary>Writes the inventory to the body's own object now. Called from
    /// the change callback; also usable to retry a failed write.</summary>
    internal bool TryPersist()
    {
        try
        {
            if (!IsLoaded || _inventory == null || !IsOwned)
            {
                return false;
            }

            var package = new ZPackage();
            _inventory.Save(package);
            ZDO zdo = _view!.GetZDO();
            zdo.Set(InventoryField, package.GetArray());
            Revision++;
            zdo.Set(RevisionField, Revision);
            return true;
        }
        catch (Exception exception)
        {
            ErrorLog?.Invoke(
                "The Steward could not write down what he is carrying: " +
                exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }

    private void OnInventoryChanged()
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        LastChangePersisted = TryPersist();
    }

    /// <summary>An unavoidable death: everything he carried goes on the ground
    /// in the same frame, through vanilla's own drop, so a player's wood is not
    /// destroyed with the body. The runtime records what reached the ground.
    ///
    /// <b>One item at a time, and the report always happens.</b> Vanilla's
    /// <c>DropItem</c> touches the animator, the drop effects and the visual
    /// equipment, and this body is a clone: one null on one stack must not take
    /// the rest of his pack down with it, and must not cost the record of what
    /// did land.</summary>
    private void OnDeath()
    {
        if (_inventory == null || _humanoid == null)
        {
            return;
        }

        Vector3 where = transform.position;
        var dropped = new List<DroppedItem>();
        try
        {
            foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(_inventory.GetAllItems()))
            {
                int count = item.m_stack;
                string named = item.m_shared == null ? string.Empty : item.m_shared.m_name;
                try
                {
                    if (_humanoid.DropItem(_inventory, item, count))
                    {
                        dropped.Add(new DroppedItem(named, count));
                    }
                }
                catch (Exception exception)
                {
                    ErrorLog?.Invoke(
                        "The Steward died and his " + named + " could not be dropped, so it is " +
                        "lost with the body: " + exception);
                }
            }
        }
        finally
        {
            try
            {
                Died?.Invoke(this, dropped, where);
            }
            catch (Exception exception)
            {
                ErrorLog?.Invoke("The Steward died and the loss could not all be recorded: " + exception);
            }
        }
    }

    public override string ToString() =>
        (string.IsNullOrEmpty(Key) ? "<unidentified steward>" : Key) + " rev " +
        Revision.ToString(CultureInfo.InvariantCulture);
}
