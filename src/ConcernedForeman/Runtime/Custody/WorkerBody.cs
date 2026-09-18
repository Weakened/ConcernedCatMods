using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TheConcernedCat.ConcernedForeman.Runtime.Custody;

/// <summary>One item a dying worker put on the ground.</summary>
internal readonly struct DroppedItem
{
    public DroppedItem(string prefabName, string sharedName, int quality, int variant, int count, ItemDrop.ItemData item)
    {
        PrefabName = prefabName;
        SharedName = sharedName;
        Quality = quality;
        Variant = variant;
        Count = count;
        Item = item;
    }

    public string PrefabName { get; }

    public string SharedName { get; }

    public int Quality { get; }

    public int Variant { get; }

    public int Count { get; }

    /// <summary>The item as it was in his inventory, for classifying tools.
    /// </summary>
    public ItemDrop.ItemData Item { get; }
}

/// <summary>The worker body's own persistence (DECISIONS.md D9, CONTRACTS.md
/// §5.6): its identity, its inventory and a revision, in its <b>own</b> network
/// object, never in a vanilla one.
///
/// <b>Why the body has to do this at all.</b> Vanilla never saves a non-player
/// humanoid's inventory: <c>Humanoid.m_inventory</c> is a plain field with no
/// save or load, rebuilt on every instantiation (FOREMAN_RUNTIME_AUDIT §0 item
/// 3). A real axe handed to the worker, or stone he carried, was destroyed by a
/// relog, a zone unload or a despawn while the journal said it was still held.
///
/// <b>The same synchronous call.</b> The inventory is written to the body's
/// ZDO from the inventory's own change callback — the exact mechanism
/// <c>Container</c> uses — so every add, remove, pickup or deposit is persisted
/// inside the call that made it, before any receipt is recorded, and the pick,
/// the carry and the deposit share one world snapshot. Nothing is saved before
/// the stored inventory has been loaded, so an empty inventory can never be
/// written over a carried one.
///
/// <b>Format.</b> <c>tcc.worker.key</c> is the <c>WorkerKey</c> text;
/// <c>tcc.worker.inventory</c> is vanilla's own <c>Inventory.Save</c> package,
/// the format a chest stores; <c>tcc.worker.revision</c> counts writes.</summary>
internal sealed class WorkerBody : MonoBehaviour
{
    internal const string KeyField = "tcc.worker.key";
    internal const string InventoryField = "tcc.worker.inventory";
    internal const string RevisionField = "tcc.worker.revision";

    private static readonly List<WorkerBody> s_live = new List<WorkerBody>();

    private ZNetView? _view;
    private Humanoid? _humanoid;
    private Inventory? _inventory;
    private bool _loading;
    private Action? _onChanged;
    private Action? _onDeath;

    /// <summary>Raised once a body has loaded its stored inventory and is ready
    /// to be bound: at spawn, at world load, and whenever its ground loads
    /// again.</summary>
    internal static Action<WorkerBody>? Loaded { get; set; }

    /// <summary>Raised on death, after every carried item was put on the
    /// ground, before the body is destroyed.</summary>
    internal static Action<WorkerBody, IReadOnlyList<DroppedItem>, Vector3>? Died { get; set; }

    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>Bodies alive in this scene that finished loading.</summary>
    internal static IReadOnlyList<WorkerBody> Live
    {
        get
        {
            s_live.RemoveAll(body => body == null);
            return s_live;
        }
    }

    internal static WorkerBody? FindLive(string key)
    {
        foreach (WorkerBody body in Live)
        {
            if (string.Equals(body.Key, key, StringComparison.Ordinal))
            {
                return body;
            }
        }

        return null;
    }

    internal string Key { get; private set; } = string.Empty;

    internal bool IsLoaded { get; private set; }

    /// <summary>Why this body is inert, or null.</summary>
    internal string? Fault { get; private set; }

    /// <summary>False when the most recent inventory change could not be
    /// written to the body's own object. A transfer involving the worker is
    /// then uncertain, never completed.</summary>
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

            RestoreEquipment();

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
            // Inert, never half-loaded: a body that could not read what it
            // carries must not work, and must not save over what it carries.
            IsLoaded = false;
            Fault = "its stored inventory could not be loaded (" + exception.GetType().Name + ")";
            ErrorLog?.Invoke("Worker body \"" + Key + "\" is inert: " + Fault + ". " + exception);
        }
    }

    private void OnDestroy()
    {
        s_live.Remove(this);

        if (_inventory != null && _onChanged != null)
        {
            _inventory.m_onChanged = (Action)Delegate.Remove(_inventory.m_onChanged, _onChanged);
        }

        if (_humanoid != null && _onDeath != null)
        {
            _humanoid.m_onDeath = (Action)Delegate.Remove(_humanoid.m_onDeath, _onDeath);
        }
    }

    /// <summary>Stamps a freshly spawned body with its identity, before its
    /// first <c>Start</c>.</summary>
    internal static bool TryStamp(GameObject instance, string key)
    {
        ZNetView view = instance.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return false;
        }

        ZDO zdo = view.GetZDO();
        zdo.Set(KeyField, key);
        zdo.Set(RevisionField, 0);
        return true;
    }

    /// <summary>The number of items this body carries, tools included.</summary>
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
            ErrorLog?.Invoke("Worker body \"" + Key + "\" could not write its inventory: " + exception.GetType().Name +
                ": " + exception.Message);
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

    /// <summary>Items saved as equipped come back unequipped in a fresh body;
    /// this puts a carried tool back in his hands. Presentation only, and a
    /// failure here changes nothing he holds.</summary>
    private void RestoreEquipment()
    {
        if (_inventory == null || _humanoid == null)
        {
            return;
        }

        foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(_inventory.GetAllItems()))
        {
            if (!item.m_equipped)
            {
                continue;
            }

            try
            {
                item.m_equipped = false;
                _humanoid.EquipItem(item, triggerEquipEffects: false);
            }
            catch (Exception)
            {
                // Presentation.
            }
        }
    }

    /// <summary>An unavoidable death: every carried item goes on the ground in
    /// the same frame, through vanilla's own drop, so nothing is destroyed with
    /// the body; the runtime records where.
    ///
    /// <b>One item at a time, and the report always happens.</b> Vanilla's
    /// <c>DropItem</c> touches the animator, the drop effects and the visual
    /// equipment, and this body is a clone: one null on one stack must not take
    /// the rest of his inventory down with it, and must not cost the runtime the
    /// record of what did reach the ground.</summary>
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
                string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty;
                try
                {
                    if (_humanoid.DropItem(_inventory, item, count))
                    {
                        dropped.Add(new DroppedItem(prefab, item.m_shared.m_name, item.m_quality, item.m_variant, count, item));
                    }
                }
                catch (Exception exception)
                {
                    ErrorLog?.Invoke("Worker body \"" + Key + "\" died and its " + prefab +
                        " could not be dropped, so it is lost with the body: " + exception);
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
                ErrorLog?.Invoke("Worker body \"" + Key + "\" died and the loss could not all be recorded: " + exception);
            }
        }
    }

    public override string ToString() =>
        (string.IsNullOrEmpty(Key) ? "<unidentified worker>" : Key) + " rev " +
        Revision.ToString(CultureInfo.InvariantCulture);
}
