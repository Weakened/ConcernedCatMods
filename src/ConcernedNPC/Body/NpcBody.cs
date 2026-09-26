using System;
using System.Collections.Generic;
using System.Globalization;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;
using TheConcernedCat.Diagnostics;

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
public sealed class NpcBody : MonoBehaviour
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
    /// bound: at spawn, at world load, and whenever its ground loads again.
    ///
    /// <b>An event and not a settable property, because several products load
    /// this library into one process.</b> A settable property invites
    /// <c>Loaded = OnLoaded;</c> - the obvious reading of a setter - and
    /// whichever product loads second then silently unhooks the first, whose
    /// NPC never learns its bodies finished loading, never binds them, and
    /// stands inert in a world where its body demonstrably exists. <c>+=</c>
    /// happens to combine, so that was a hazard rather than a certainty: it
    /// works in testing with one product installed and fails for the player
    /// with two, which is the worst shape a defect can have. An event makes
    /// <c>=</c> a compile error, leaves <c>+=</c> and <c>-=</c> as the only
    /// operations, and gives a role a way to detach at teardown - which there
    /// was no safe way to do at all.
    ///
    /// <b>Every subscriber is raised, whatever the one before it did.</b> A
    /// multicast delegate invoked as a single call stops at the first handler
    /// that throws, so with one product per handler, one product's defect costs
    /// another product its event. Each handler is invoked on its own, and one
    /// that throws is reported through <see cref="ErrorLog"/> and skipped. It
    /// also no longer makes the body itself inert, which is what a throw out of
    /// this used to do.</summary>
    public static event Action<NpcBody>? Loaded;

    /// <summary>Raised on death, after every carried item is on the ground and
    /// before the body goes, so a ledger can say what left its hands rather
    /// than quietly balancing. An event for the reason
    /// <see cref="Loaded"/> is, and raised the same way.</summary>
    public static event Action<NpcBody, IReadOnlyList<NpcDroppedItem>, Vector3>? Died;

    /// <summary>Errors, reported whatever the diagnostic settings say: an inert
    /// body is exactly the thing a player needs told. An event for the reason
    /// <see cref="Loaded"/> is - a second product taking the error channel away
    /// from the first is how an inert body becomes a silent one.</summary>
    public static event Action<string>? ErrorLog;

    /// <summary>Bodies alive in this scene that finished loading, of every
    /// role.
    ///
    /// <b>Internal, and it is the change that matters.</b> Handing every role's
    /// bodies across an assembly boundary and asking each consumer in a doc
    /// comment to filter by its own prefab made the one rule the arbiter exists
    /// to enforce into advice a role could ignore. A consumer asks
    /// <see cref="LiveFor"/>, which does that filtering itself.</summary>
    internal static IReadOnlyList<NpcBody> Live
    {
        get
        {
            LiveBodies.RemoveAll(body => body == null);
            return LiveBodies;
        }
    }

    /// <summary>The loaded bodies of one role's prefab, and nobody else's.
    ///
    /// The prefab is what separates two roles that share a key prefix - Foreman
    /// and Teamster do today - so the filter is applied here rather than asked
    /// for. A contract that is not the one its prefab was registered under
    /// names no role, and a role that does not exist has no bodies.</summary>
    public static IReadOnlyList<NpcBody> LiveFor(NpcBodyContract contract)
    {
        if (!NpcBodyContracts.IsRegistered(contract))
        {
            return Array.Empty<NpcBody>();
        }

        var mine = new List<NpcBody>();
        foreach (NpcBody body in Live)
        {
            if (string.Equals(body.PrefabName, contract.PrefabName, StringComparison.Ordinal))
            {
                mine.Add(body);
            }
        }

        return mine;
    }

    /// <summary>The one loaded body carrying this identity, or null. Filtered
    /// by prefab <b>and then</b> key: two roles may share a key prefix, and a
    /// search by identity alone would hand one role's runtime the other's body.
    /// </summary>
    public static NpcBody? FindLive(NpcBodyContract contract, NpcIdentity identity)
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

    /// <summary>Drops the tracked bodies of one role's prefab. Called when a
    /// world goes away, so a second world in the same session does not inherit
    /// the first one's.
    ///
    /// <b>Why it takes a contract.</b> It used to clear every role's bodies at
    /// once, so one product's world teardown blinded every other product's
    /// count of what is standing - and a runtime that has forgotten a body
    /// still in the world is a runtime that will permit a second body for the
    /// same identity. Each role forgets its own; a contract nobody registered
    /// forgets nothing.</summary>
    public static void ForgetAll(NpcBodyContract contract)
    {
        if (!NpcBodyContracts.IsRegistered(contract))
        {
            return;
        }

        LiveBodies.RemoveAll(body =>
            body == null || string.Equals(body.PrefabName, contract.PrefabName, StringComparison.Ordinal));
    }

    /// <summary>The prefab this body is an instance of.</summary>
    public string PrefabName => _setup.Contract.PrefabName;

    /// <summary>Who this body is, read from its own object.</summary>
    public NpcIdentity Identity { get; private set; }

    /// <summary>The identity text exactly as it is stored, which is not always
    /// a well-formed identity: a body stamped by an older build, or damaged,
    /// keeps whatever it has and is reported rather than adopted.</summary>
    public string StoredKey { get; private set; } = string.Empty;

    public bool IsLoaded { get; private set; }

    /// <summary>Why this body is inert, or null.</summary>
    public string? Fault { get; private set; }

    /// <summary>False when the most recent inventory change could not be
    /// written to the body's own object. Any step involving this body is then
    /// uncertain, never completed.</summary>
    public bool LastChangePersisted { get; private set; } = true;

    public int Revision { get; private set; }

    public Humanoid? Humanoid => _humanoid;

    public Inventory? Inventory => _inventory;

    /// <summary>The body's own network object.
    ///
    /// <b>Internal on purpose, while nearly everything else here is public.</b>
    /// A role needs to read what this body is and what it carries; it does not
    /// need the handle that would let it write into the object, and the rule
    /// that only a body writes its own object is worth more than the
    /// convenience. <see cref="IsOwned"/> answers the question a role actually
    /// has.</summary>
    internal ZNetView? View => _view;

    public bool IsOwned => _view != null && _view.IsValid() && _view.IsOwner();

    /// <summary>What this body stores, as its role declared at registration.
    /// </summary>
    public NpcBodyKeeps Keeps => _setup.Keeps;

    /// <summary>The number of items this body carries, tools included.</summary>
    public int ItemCount => _inventory == null ? 0 : _inventory.NrOfItems();

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

    /// <summary>Gives one subscriber at a time its turn at
    /// <see cref="Loaded"/>, so a handler that throws costs only itself.
    ///
    /// A role binding a body is arbitrary consumer code, and before this was an
    /// event it ran inside the body's own load with nothing between it and the
    /// failure path: a subscriber that threw made the body report itself inert
    /// and save nothing. Now a failure is one product's, it is said out loud,
    /// and the body is still loaded.</summary>
    private static void RaiseLoaded(NpcBody body)
    {
        Delegate[]? handlers = Loaded?.GetInvocationList();
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<NpcBody>)handler)(body);
            }
            catch (Exception exception)
            {
                RaiseErrorLog("NPC body \"" + body.StoredKey + "\" finished loading and a subscriber threw, "
                    + "so that subscriber has not bound it: " + SafeFailure.Describe(exception));
            }
        }
    }

    /// <summary>The same, for the one report a ledger cannot afford to miss:
    /// one subscriber failing to record what hit the ground must not stop the
    /// next one recording it.</summary>
    private static void RaiseDied(NpcBody body, IReadOnlyList<NpcDroppedItem> dropped, Vector3 where)
    {
        Delegate[]? handlers = Died?.GetInvocationList();
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<NpcBody, IReadOnlyList<NpcDroppedItem>, Vector3>)handler)(body, dropped, where);
            }
            catch (Exception exception)
            {
                RaiseErrorLog("NPC body \"" + body.StoredKey
                    + "\" died and a subscriber could not record the loss: " + SafeFailure.Describe(exception));
            }
        }
    }

    /// <summary>The same again, and the one that may never throw: it is where
    /// the other two send their failures.</summary>
    private static void RaiseErrorLog(string message)
    {
        Delegate[]? handlers = ErrorLog?.GetInvocationList();
        if (handlers == null)
        {
            return;
        }

        foreach (Delegate handler in handlers)
        {
            try
            {
                ((Action<string>)handler)(message);
            }
            catch (Exception)
            {
                // Reporting a failure must never become one, and must never
                // cost the next subscriber the report either.
            }
        }
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
                RaiseErrorLog("An NPC body is inert: " + Fault + ".");
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
            RaiseLoaded(this);
        }
        catch (Exception exception)
        {
            // Inert, never half loaded: a body that could not read what it
            // carries must not work, and must not save over what it carries.
            IsLoaded = false;
            Fault = "its stored inventory could not be loaded (" + exception.GetType().Name + ")";
            RaiseErrorLog("NPC body \"" + StoredKey + "\" is inert: " + Fault + ". " + SafeFailure.Describe(exception));
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
    public bool TryPersist()
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
            RaiseErrorLog("NPC body \"" + StoredKey + "\" could not write its inventory: "
                + SafeFailure.Brief(exception));
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
                    RaiseErrorLog("NPC body \"" + StoredKey + "\" died and its " + prefab
                        + " could not be dropped, so it is lost with the body: " + SafeFailure.Describe(exception));
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
                    + "\" died and the loss could not all be recorded: " + SafeFailure.Describe(exception));
            }
        }
    }

    public override string ToString() =>
        (StoredKey.Length == 0 ? "<unidentified body>" : StoredKey) + " rev "
        + Revision.ToString(CultureInfo.InvariantCulture);
}
