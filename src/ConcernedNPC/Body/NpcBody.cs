using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>A worker body's own persistence: its identity, what it carries and
/// a revision, in its <b>own</b> network object and never in a vanilla one.
///
/// <b>Why a body has to do this at all.</b> Vanilla never saves a non-player
/// humanoid's inventory - the field is plain, with no save and no load, rebuilt
/// on every instantiation - so a real axe handed to a worker, or stone he was
/// carrying, is destroyed by a relog, a zone unload or a despawn while the
/// runtime's own record still says it is held. A record that disagrees with the
/// world about a player's materials is the failure these products exist to
/// avoid.
///
/// <b>Inside the same synchronous call.</b> The inventory is written from the
/// inventory's own change callback - the exact mechanism a vanilla container
/// uses - so every add, remove, pick-up and deposit is persisted inside the
/// call that made it, before any receipt is recorded, and the pick, the carry
/// and the deposit share one snapshot of the world.
/// <see cref="LastChangePersisted"/> going false is what makes a transfer
/// uncertain rather than completed.
///
/// <b>Nothing is loaded over.</b> A body that could not read what it carries is
/// inert: it does not work, and it does not save an empty inventory over a full
/// one.
///
/// <b>Where the key names come from.</b> Not from here. This component wakes
/// inside an object the host created out of a save, with nothing passed to it,
/// and asks <see cref="NpcBodyContracts"/> which role's prefab it is an
/// instance of. The three names it then uses are that role's prefix with this
/// library's three suffixes - byte for byte the names the two shipped products
/// wrote, which is what makes an existing world load without a migration. A
/// prefab nobody registered yields no names, and a body with no names is inert
/// rather than reading somebody else's.</summary>
internal sealed class NpcBody : MonoBehaviour
{
    private static readonly List<NpcBody> LiveBodies = new List<NpcBody>();

    private NpcBodySetup _setup;
    private ZNetView? _view;
    private Humanoid? _humanoid;
    private Inventory? _inventory;
    private bool _loading;
    private Action? _onChanged;
    private Action? _onDeath;

    /// <summary>Raised once a body has loaded what it stores and is ready to be
    /// bound: at spawn, at world load, and whenever its ground loads
    /// again.</summary>
    internal static Action<NpcBody>? Loaded { get; set; }

    /// <summary>Raised on death, after every carried item is on the ground and
    /// before the body goes, so a ledger can say what left its hands rather
    /// than quietly balancing.</summary>
    internal static Action<NpcBody, IReadOnlyList<NpcDroppedItem>, Vector3>? Died { get; set; }

    /// <summary>Errors, reported whatever the diagnostic settings say: an inert
    /// body is exactly the thing a player needs told.</summary>
    internal static Action<string>? ErrorLog { get; set; }

    /// <summary>Bodies alive in this scene that finished loading.</summary>
    internal static IReadOnlyList<NpcBody> Live
    {
        get
        {
            LiveBodies.RemoveAll(body => body == null);
            return LiveBodies;
        }
    }

    /// <summary>The one loaded body carrying this identity, or null. Filtered
    /// by prefab <b>and then</b> key: two roles may share a key prefix, and a
    /// search by identity alone would hand one role's runtime the other's body.
    /// </summary>
    internal static NpcBody? FindLive(NpcBodyContract contract, NpcIdentity identity)
    {
        foreach (NpcBody body in Live)
        {
            if (string.Equals(body.PrefabName, contract.PrefabName, StringComparison.Ordinal)
                && body.Identity.Equals(identity))
            {
                return body;
            }
        }

        return null;
    }

    /// <summary>Drops every tracked body. Called when a world goes away, so a
    /// second world in the same session does not inherit the first one's.
    /// </summary>
    internal static void ForgetAll() => LiveBodies.Clear();

    /// <summary>The prefab this body is an instance of.</summary>
    internal string PrefabName => _setup.Contract.PrefabName;

    /// <summary>Who this body is, read from its own object.</summary>
    internal NpcIdentity Identity { get; private set; }

    /// <summary>The identity text exactly as it is stored, which is not always
    /// a well-formed identity: a body stamped by an older build, or damaged,
    /// keeps whatever it has and is reported rather than adopted.</summary>
    internal string StoredKey { get; private set; } = string.Empty;

    internal bool IsLoaded { get; private set; }

    /// <summary>Why this body is inert, or null.</summary>
    internal string? Fault { get; private set; }

    /// <summary>False when the most recent inventory change could not be
    /// written to the body's own object. Any step involving this body is then
    /// uncertain, never completed.</summary>
    internal bool LastChangePersisted { get; private set; } = true;

    internal int Revision { get; private set; }

    internal Humanoid? Humanoid => _humanoid;

    internal Inventory? Inventory => _inventory;

    internal ZNetView? View => _view;

    internal bool IsOwned => _view != null && _view.IsValid() && _view.IsOwner();

    /// <summary>What this body stores, as its role declared at registration.
    /// </summary>
    internal NpcBodyKeeps Keeps => _setup.Keeps;

    /// <summary>The number of items this body carries, tools included.</summary>
    internal int ItemCount => _inventory == null ? 0 : _inventory.NrOfItems();

    /// <summary>Stamps a freshly built body with its identity, before its first
    /// <c>Start</c>. The one place this library writes into a network object,
    /// and it writes only into the body's own.</summary>
    internal static bool TryStamp(GameObject instance, NpcBodySetup setup, NpcIdentity identity)
    {
        if (instance == null || setup.IsEmpty || identity.IsEmpty)
        {
            return false;
        }

        ZNetView view = instance.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return false;
        }

        ZDO zdo = view.GetZDO();
        zdo.Set(setup.Fields.Key, identity.Value);
        zdo.Set(setup.Fields.Revision, 0);
        return true;
    }

    private void Start()
    {
        try
        {
            string prefab = NpcBodyContracts.PrefabNameOf(gameObject == null ? string.Empty : gameObject.name);
            if (!NpcBodyContracts.TryFind(prefab, out _setup))
            {
                // No names, so nothing is read and - far more importantly -
                // nothing is ever written. A body whose prefab this library did
                // not register is not this library's to persist.
                Fault = "no role registered a body of that prefab, so it has no keys of its own";
                ErrorLog?.Invoke("An NPC body is inert: " + Fault + ".");
                return;
            }

            _view = GetComponent<ZNetView>();
            _humanoid = GetComponent<Humanoid>();
            if (_view == null || !_view.IsValid() || _humanoid == null)
            {
                Fault = "the body has no valid network object or no humanoid";
                return;
            }

            ZDO zdo = _view.GetZDO();
            StoredKey = zdo.GetString(_setup.Fields.Key, string.Empty);
            Identity = NpcIdentity.TryParse(StoredKey, out NpcIdentity parsed) ? parsed : default;
            Revision = zdo.GetInt(_setup.Fields.Revision, 0);
            _inventory = _humanoid.GetInventory();

            if (_setup.Keeps == NpcBodyKeeps.IdentityAndInventory)
            {
                LoadStoredInventory(zdo);
                RestoreEquipment();

                _onChanged = OnInventoryChanged;
                _inventory.m_onChanged = (Action)Delegate.Combine(_inventory.m_onChanged, _onChanged);
            }

            _onDeath = OnDeath;
            _humanoid.m_onDeath = (Action)Delegate.Combine(_humanoid.m_onDeath, _onDeath);

            IsLoaded = true;
            LiveBodies.Add(this);
            Loaded?.Invoke(this);
        }
        catch (Exception exception)
        {
            // Inert, never half loaded: a body that could not read what it
            // carries must not work, and must not save over what it carries.
            IsLoaded = false;
            Fault = "its stored inventory could not be loaded (" + exception.GetType().Name + ")";
            ErrorLog?.Invoke("NPC body \"" + StoredKey + "\" is inert: " + Fault + ". " + exception);
        }
    }

    private void LoadStoredInventory(ZDO zdo)
    {
        byte[]? stored = zdo.GetByteArray(_setup.Fields.Inventory, null);
        _loading = true;
        try
        {
            if (stored != null && stored.Length > 0)
            {
                _inventory!.Load(new ZPackage(stored));
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnDestroy()
    {
        LiveBodies.Remove(this);

        if (_inventory != null && _onChanged != null)
        {
            _inventory.m_onChanged = (Action)Delegate.Remove(_inventory.m_onChanged, _onChanged);
        }

        if (_humanoid != null && _onDeath != null)
        {
            _humanoid.m_onDeath = (Action)Delegate.Remove(_humanoid.m_onDeath, _onDeath);
        }
    }

    /// <summary>Writes the inventory to the body's own object now. Called from
    /// the change callback; also usable to retry a failed write.</summary>
    internal bool TryPersist()
    {
        try
        {
            if (!IsLoaded || _inventory == null || !IsOwned || _setup.Keeps != NpcBodyKeeps.IdentityAndInventory)
            {
                return false;
            }

            var package = new ZPackage();
            _inventory.Save(package);
            ZDO zdo = _view!.GetZDO();
            zdo.Set(_setup.Fields.Inventory, package.GetArray());
            Revision++;
            zdo.Set(_setup.Fields.Revision, Revision);
            return true;
        }
        catch (Exception exception)
        {
            ErrorLog?.Invoke("NPC body \"" + StoredKey + "\" could not write its inventory: "
                + exception.GetType().Name + ": " + exception.Message);
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
    /// this puts a carried tool back in its hands. Presentation only, and a
    /// failure here changes nothing it holds.</summary>
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
    /// <b>One item at a time, and the report always happens.</b> Vanilla's drop
    /// touches the animator, the drop effects and the visual equipment, and
    /// this body is a clone: one null on one stack must not take the rest of
    /// the inventory down with it, and must not cost the runtime the record of
    /// what did reach the ground.</summary>
    private void OnDeath()
    {
        if (_inventory == null || _humanoid == null)
        {
            return;
        }

        Vector3 where = transform.position;
        var dropped = new List<NpcDroppedItem>();
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
                        dropped.Add(new NpcDroppedItem(
                            prefab, item.m_shared.m_name, item.m_quality, item.m_variant, count, item));
                    }
                }
                catch (Exception exception)
                {
                    ErrorLog?.Invoke("NPC body \"" + StoredKey + "\" died and its " + prefab
                        + " could not be dropped, so it is lost with the body: " + exception);
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
                ErrorLog?.Invoke("NPC body \"" + StoredKey
                    + "\" died and the loss could not all be recorded: " + exception);
            }
        }
    }

    public override string ToString() =>
        (StoredKey.Length == 0 ? "<unidentified body>" : StoredKey) + " rev "
        + Revision.ToString(CultureInfo.InvariantCulture);
}
