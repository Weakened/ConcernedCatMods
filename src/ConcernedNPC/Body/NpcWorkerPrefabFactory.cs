using System;
using Jotunn.Entities;
using Jotunn.Managers;
using TheConcernedCat.ConcernedNPC.Bodies;
using TheConcernedCat.ConcernedNPC.Roles;
using UnityEngine;
using TheConcernedCat.Diagnostics;

namespace TheConcernedCat.ConcernedNPC.Body;

/// <summary>Builds and registers one role's worker prefab, and is the only
/// place in this library that creates a worker body.
///
/// <b>Why the mind is swapped on a prefab and never on a live creature.</b>
/// <c>BaseAI.Awake</c> registers three RPCs through a dictionary add on each
/// name's stable hash, so registering the same name twice on one network view
/// throws - and the driver that would be running at the time does not catch.
/// Spawning a creature and then replacing its AI runs <c>Awake</c> twice on one
/// view and throws inside that driver. The prefab manager's clone lands in its
/// own inactive container, so the clone's <c>Awake</c> has not run when the
/// components are swapped and runs exactly once when a real instance is
/// created. The safety rules say the same thing as a rule: never instantiate a
/// live body and strip components afterwards.
///
/// <b>Registered at plugin start, and never unregistered.</b> The body is
/// persistent: a world saved with one in it holds that body's network record,
/// and the host destroys any saved record whose prefab is not registered when
/// the world's objects are created - it logs one line about an invalid prefab
/// and moves on. A prefab built lazily by a console command, or torn down on
/// plugin shutdown, therefore deletes every existing body and everything inside
/// it, silently and permanently. So the build is armed as soon as the game's
/// own prefabs can be cloned and the registration is one-way. <b>There is
/// deliberately no uninstall here</b>, although one of the three copies this
/// replaces had one: nothing this library can offer is worth a method whose
/// failure mode is that.
///
/// <b>Nothing is minted.</b> The base creature's loot table is removed, so a
/// death drops only what the body really carried; its default and random gear
/// is cleared, so no item is granted on spawn and the stored inventory is the
/// only thing in its hands.
///
/// <b>The base creature name is data, not an API.</b> Creature prefab names
/// live in the game's asset bundles, not in the assembly, so no amount of
/// reading the game's code can prove one exists. It is supplied by the role,
/// resolved at run time, and <b>fails closed</b>: a missing or unsuitable
/// prefab produces no body and an explanation naming exactly what was
/// missing.
///
/// <b>Public, and exactly this much of it.</b> This is a role's entry point, so
/// it has to be callable from a product - a library whose own adoption
/// instructions name an internal type is a library nobody can adopt. What a
/// role needs is <see cref="TryFor"/>, <see cref="Install"/>,
/// <see cref="TrySpawn"/>, <see cref="TrySpawnRoleBody"/> for a role whose
/// prefab predates this factory (#372), and the two facts a console command reports
/// (<see cref="IsReady"/>, <see cref="LastFailure"/>). Three members are
/// deliberately <i>not</i> part of that:
///
/// <list type="bullet">
/// <item><c>Prefab</c> - handing out the built prefab would end the rule that
/// bodies are created in one place, because the next line a caller writes is
/// the engine's own instantiation on it. The validator rule and the audit that
/// confine creation to this file would both still pass, and the lease would be
/// bypassed anyway.</item>
/// <item><c>TryCreate</c> - the timing is the dangerous half of this type.
/// <see cref="Install"/> arms the build for the one moment it is safe; a
/// caller that could force it could build after a world's objects were
/// created.</item>
/// <item><c>BaseCreature</c> - the role passed it in and already knows it.</item>
/// </list></summary>
public sealed class NpcWorkerPrefabFactory
{
    private readonly NpcBodyContract _contract;
    private readonly NpcWorkerPrefabOptions _options;
    private NpcBodySetup _setup;
    private GameObject? _prefab;
    private bool _armed;
    private string _baseCreature = string.Empty;
    private Action<string>? _log;
    private string _lastLoggedFailure = string.Empty;

    private NpcWorkerPrefabFactory(NpcBodyContract contract, NpcWorkerPrefabOptions options, NpcBodySetup setup)
    {
        _contract = contract;
        _options = options;
        _setup = setup;
    }

    /// <summary>The prefab is built and registered.</summary>
    public bool IsReady => _prefab != null;

    /// <summary>Why the last attempt failed. Never null: before the first
    /// attempt it says what is being waited for.</summary>
    public string LastFailure { get; private set; } = "waiting for the game's prefabs to load";

    /// <summary>The prefab, once built. Held so a second call is a no-op and so
    /// a diagnostic can say whether it exists; never handed to a caller that
    /// would instantiate it, which is what <see cref="TrySpawn"/> is for.
    /// </summary>
    internal GameObject? Prefab => _prefab;

    public NpcBodyContract Contract => _contract;

    internal string BaseCreature => _baseCreature;

    /// <summary>Creates a factory for one role, or refuses and says why.
    ///
    /// Refusing here rather than at build time is deliberate: a contract that
    /// cannot name a prefab or a key prefix is a role that would register a
    /// body under an empty name, and the cost of finding that out later is
    /// every existing body of that role.</summary>
    public static bool TryFor(
        NpcBodyContract contract,
        NpcWorkerPrefabOptions options,
        out NpcWorkerPrefabFactory? factory,
        out string reason)
    {
        factory = null;

        if (!NpcBodySetup.TryCompose(contract, options.Keeps, out NpcBodySetup setup, out reason))
        {
            return false;
        }

        if (!NpcBodyContracts.TryRemember(contract, options.Keeps, out reason))
        {
            return false;
        }

        factory = new NpcWorkerPrefabFactory(contract, options, setup);
        reason = string.Empty;
        return true;
    }

    /// <summary>Arms the build for the moment the game's own prefabs can be
    /// cloned, which is at the main menu - before any world's objects are
    /// created. Idempotent.</summary>
    public void Install(string? baseCreature, Action<string>? log)
    {
        _baseCreature = string.IsNullOrWhiteSpace(baseCreature) ? string.Empty : baseCreature!.Trim();
        _log = log;

        if (_prefab != null || _armed)
        {
            return;
        }

        _armed = true;
        PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
    }

    private void OnVanillaPrefabsAvailable()
    {
        try
        {
            if (_prefab != null || TryCreate(_baseCreature, _log))
            {
                PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;
                _armed = false;
            }
        }
        catch (Exception exception)
        {
            // The subscription is deliberately kept: the next time vanilla
            // prefabs become available is another chance, and staying
            // unregistered is the outcome with the permanent cost.
            Fail("building it threw " + SafeFailure.Brief(exception));
        }
    }

    /// <summary>Builds the prefab from <paramref name="baseCreature"/>.
    /// Idempotent: a second call with the prefab already built is a no-op.
    /// </summary>
    internal bool TryCreate(string? baseCreature, Action<string>? log = null)
    {
        if (_prefab != null)
        {
            return true;
        }

        _log = log ?? _log;
        string creature = string.IsNullOrWhiteSpace(baseCreature) ? _baseCreature : baseCreature!.Trim();
        _baseCreature = creature;

        if (creature.Length == 0)
        {
            return Fail("no base creature prefab is configured");
        }

        PrefabManager prefabs = PrefabManager.Instance;
        if (prefabs == null)
        {
            return Fail("the prefab manager is not available yet");
        }

        GameObject? existing = prefabs.GetPrefab(_contract.PrefabName);
        if (existing != null)
        {
            // Built earlier in this process and still registered. Adopted only
            // when it is one of ours: one of the three copies adopted anything
            // under the name, which would have bound this factory to somebody
            // else's object and then spawned bodies with no persistence in
            // them. Checking for our own component costs one call and turns a
            // silent adoption into a clear refusal.
            if (existing.GetComponent<NpcBody>() != null)
            {
                _prefab = existing;
                LastFailure = string.Empty;
                return true;
            }

            return Fail("something else is already registered under that prefab name");
        }

        GameObject? basePrefab = prefabs.GetPrefab(creature);
        if (basePrefab == null)
        {
            return Fail("the base creature prefab '" + creature
                + "' does not exist in this game build; set the role's base creature to one that does");
        }

        GameObject? clone = prefabs.CreateClonedPrefab(_contract.PrefabName, basePrefab);
        if (clone == null)
        {
            return Fail("could not clone '" + creature + "'");
        }

        // Everything below runs while the clone is inactive inside the prefab
        // manager's own container, so no Awake has executed on any of it.
        string? missing = DescribeMissingComponents(clone);
        if (missing != null)
        {
            UnityEngine.Object.DestroyImmediate(clone);
            return Fail("'" + creature + "' cannot be a worker body: " + missing
                + ". A worker needs a networked humanoid body");
        }

        // The union of the three strip lists, which is the widest one. Two of
        // the three removed only the mind, taming and the loot table; the third
        // had found, in game, that a saddle and a talk component both
        // dereference the mind that has just been removed, that breeding and
        // growing up replace the body with one carrying no identity, and that a
        // timed destruction quietly deletes it. Adding those five to the other
        // two roles is not a no-op and is not cosmetic: it is five ways their
        // bodies could already have been lost, closed.
        StripAll<BaseAI>(clone);
        StripAll<Tameable>(clone);
        StripAll<Sadle>(clone);
        StripAll<NpcTalk>(clone);
        StripAll<CharacterDrop>(clone);
        StripAll<Procreation>(clone);
        StripAll<Growup>(clone);
        StripAll<CharacterTimedDestruction>(clone);

        // Vanilla gives non-players their default and random gear on every
        // instantiation. Empty arrays, never null: the code that grants them
        // reads each array's length.
        Humanoid humanoid = clone.GetComponent<Humanoid>();
        humanoid.m_defaultItems = new GameObject[0];
        humanoid.m_randomWeapon = new GameObject[0];
        humanoid.m_randomArmor = new GameObject[0];
        humanoid.m_randomShield = new GameObject[0];
        humanoid.m_randomSets = new Humanoid.ItemSet[0];
        humanoid.m_randomItems = new Humanoid.RandomItem[0];

        clone.GetComponent<ZNetView>().m_persistent = true;

        clone.AddComponent<NpcBodyMind>();
        clone.AddComponent<NpcBody>();

        // Not an enemy of anyone, and not a boss. Faction behaviour is out of
        // scope; what is in scope is not shipping a body that inherits a
        // hostile creature's faction by accident.
        Character character = clone.GetComponent<Character>();
        character.m_faction = Character.Faction.Players;
        character.m_boss = false;
        if (_options.DisplayName.Length != 0)
        {
            character.m_name = _options.DisplayName;
        }

        // Two calls, and the reason is NOT the one an earlier version of this
        // comment gave. That version said the single-argument overload was
        // weaker - enough for a lookup by name, not enough to be in the network
        // scene's table - and that the difference was the difference between a
        // saved body coming back and being destroyed. Read against the shipped
        // framework, all of that is false, and it is worth writing down why,
        // because it is the scarier of the two stories and it was the one three
        // products half believed.
        //
        // The single-argument overload IS this one: it constructs the same
        // wrapper with the same flag and calls the same method. Neither
        // overload registers anything into the network scene. What does that is
        // a postfix the framework puts on the scene's own wake-up, which walks
        // every prefab it knows and registers all of them, on every world load,
        // forever - so the overload cannot matter. And the explicit call below
        // is a no-op on the path this factory actually runs on: it early-returns
        // when there is no scene, and this builds at the main menu, where there
        // is none.
        //
        // Both calls are kept anyway. They cost nothing, they say plainly what
        // this code intends, and the second becomes genuinely load-bearing the
        // day anybody moves the build later than the main menu - to a point
        // where a scene already exists and that load's registration would
        // otherwise be missed.
        //
        // The irreversible failure the architecture describes is real. It is
        // about the prefab NAME and the TIMING - a saved object whose prefab
        // hash is not in the scene's table when objects are created is
        // destroyed - and both of those are preserved here: the name comes from
        // the role's contract and the build is armed for the main menu.
        prefabs.AddPrefab(new CustomPrefab(clone, fixReference: false));
        prefabs.RegisterToZNetScene(clone);

        _prefab = clone;
        LastFailure = string.Empty;
        _lastLoggedFailure = string.Empty;
        _log?.Invoke("The body prefab '" + _contract.PrefabName + "' was built from '" + creature
            + "' and registered for every world.");
        return true;
    }

    /// <summary>Creates one body from this factory's own prefab. With
    /// <see cref="TrySpawnRoleBody"/> it is one of the two doors to the one
    /// build in this library that creates a saved body.
    ///
    /// <b>The lease is a parameter because permission has to be shown, not
    /// claimed.</b> The arbiter hands one out on a grant and only on a grant,
    /// so a role that was refused has nothing to pass. And
    /// <see cref="NpcBodyBuildGate"/> re-asks whether the lease is still held
    /// immediately before the body is created, because a claim proves
    /// permission existed when it was taken and this needs permission to exist
    /// now - the two are separated by a census, a zone load, or a player
    /// confirming, and the world can unload in between.
    ///
    /// <b>The body is stamped before anything can read it.</b> The identity
    /// goes into the new object in the same call that created it, so a body
    /// that exists for one frame with no identity never exists. A body that
    /// comes up invalid or unowned is destroyed again rather than left standing
    /// as an unidentified stranger the census would then count forever.
    /// </summary>
    public NpcBodyMind? TrySpawn(
        BodyLease? lease,
        NpcBodyTally tally,
        Vector3 position,
        Quaternion rotation,
        out string failure)
    {
        NpcBodySetup setup = _setup;
        GameObject? instance = BuildGated(
            lease, _contract, tally, _prefab, IsReady, LastFailure, position, rotation,
            built => built.GetComponent<NpcBodyMind>() != null && NpcBody.TryStamp(built, setup, lease!.Identity),
            out failure);
        if (instance == null)
        {
            return null;
        }

        NpcBodyMind mind = instance.GetComponent<NpcBodyMind>();
        mind.RememberIdentity(lease!.Identity);
        return mind;
    }

    /// <summary>Creates one body from a prefab the ROLE built and registered,
    /// through the same gate as <see cref="TrySpawn"/>, and is the only other
    /// path in this library that creates a saved body (#372).
    ///
    /// <b>Why this exists beside <see cref="TrySpawn"/>.</b> Three shipped roles
    /// built their worker prefabs before this factory existed, each with its own
    /// mind and its own body component, and moving a role's mind is a separate
    /// change with its own behaviour to prove. What can move without changing
    /// any of that is the one line that mattered to the never-coexist rule: a
    /// role calling the engine's own instantiation on a prefab it held, and never
    /// asking anybody. With this, a role hands its prefab in rather than
    /// instantiating it, so the lease is required and re-checked last exactly as
    /// it is for this factory's own bodies, and <c>tools/validate_repo.py</c>
    /// refuses the engine's instantiation anywhere in a product that consumes
    /// this library.
    ///
    /// <b>The prefab goes in; nothing comes out but the new body.</b> This is
    /// the opposite direction to the <c>Prefab</c> member this type refuses to
    /// expose, and it keeps that refusal's reason: no caller is handed something
    /// it could instantiate itself.
    ///
    /// <b>The census is this library's, taken here.</b> The tally is not a
    /// parameter, because a role that counted its own bodies would be the one
    /// party able to say "none" for itself. A finished walk of every saved object
    /// of the contract's prefab, keyed by the contract's own key field, is taken
    /// immediately before the gate. A loaded body is a saved object too, so that
    /// walk counts it.
    ///
    /// <b>The prefab must be the one the network scene knows by the contract's
    /// name.</b> A body built from a prefab the scene cannot name is destroyed by
    /// the next load with everything it holds, so a prefab that is missing,
    /// misnamed or unregistered is a refusal rather than a body.
    ///
    /// <b>The contract must be the lease's own role's.</b> The lease names an
    /// identity and the arbiter that granted it remembers the contract that
    /// identity registered; a contract that differs in kind, prefab name, key
    /// prefix or figure admission is refused before anything is counted, so one
    /// role's lease never builds another role's body (#372 review).
    ///
    /// <b>Stamped in the same call.</b> <paramref name="stamp"/> is the role's own
    /// identity write, run on the new object before anything else can read it.
    /// When it fails or throws, the body is destroyed again rather than left
    /// standing as a stranger the census would then count forever. It holds
    /// nothing: it was created a moment earlier by this same call.</summary>
    /// <param name="lease">The permission the arbiter granted. Null is a
    /// refusal.</param>
    /// <param name="contract">The role's body contract.</param>
    /// <param name="rolePrefab">The prefab the role built and registered under
    /// <see cref="NpcBodyContract.PrefabName"/>.</param>
    /// <param name="position">Where the body stands.</param>
    /// <param name="rotation">Which way it faces.</param>
    /// <param name="stamp">The role's identity write. True when the new body
    /// carries its identity.</param>
    /// <param name="failure">Why nothing was built. Empty on success.</param>
    public static GameObject? TrySpawnRoleBody(
        BodyLease? lease,
        NpcBodyContract contract,
        GameObject? rolePrefab,
        Vector3 position,
        Quaternion rotation,
        Func<GameObject, bool> stamp,
        out string failure)
    {
        if (lease != null && !lease.IsContractedFor(contract))
        {
            // Before the census, so nothing is counted for a contract this
            // lease's role never registered: one role's lease never builds
            // another role's prefab (#372 review). Every other refusal,
            // the lease's own liveness last, is still the gate's.
            failure = "the lease is for " + lease.Identity + ", whose registered body contract is not "
                + contract + ", so it does not permit this body";
            return null;
        }

        string? prefabFailure = DescribeUnregistered(contract, rolePrefab);
        NpcBodyTally tally = lease == null
            ? NpcBodyTally.NotYetRun(default)
            : CountSavedBodies(contract, lease.Identity);
        return BuildGated(
            lease, contract, tally, rolePrefab, prefabFailure == null, prefabFailure, position, rotation,
            stamp, out failure);
    }

    /// <summary>The one instantiation of a saved body in this library, and the
    /// gate immediately in front of it. Nothing may be added between the two:
    /// the lease is the last thing the gate asks, and it has to be the freshest
    /// thing known when the body is created. <c>tools/validate_repo.py</c> pins
    /// that adjacency.</summary>
    private static GameObject? BuildGated(
        BodyLease? lease,
        NpcBodyContract contract,
        NpcBodyTally tally,
        GameObject? prefab,
        bool prefabReady,
        string? prefabFailure,
        Vector3 position,
        Quaternion rotation,
        Func<GameObject, bool>? stamp,
        out string failure)
    {
        bool ready = prefabReady && prefab != null && stamp != null;
        bool groundLoaded = IsGroundLoaded(position);
        NpcBuildPermission permission = NpcBodyBuildGate.May(
            lease, contract, tally, ready, prefabFailure, groundLoaded);
        if (!permission.IsGranted)
        {
            failure = permission.Refusal;
            return null;
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab!, position, rotation);
        ZNetView view = instance.GetComponent<ZNetView>();
        bool stamped = false;
        string why = "the new body did not come up as a valid, owned network object";
        if (view != null && view.IsValid() && view.IsOwner())
        {
            try
            {
                stamped = stamp!(instance);
                if (!stamped)
                {
                    why = "the new body could not be given its identity";
                }
                else if (!CarriesLeasedIdentity(view, contract, lease!.Identity))
                {
                    // The census counts by the contract's key field and the
                    // lease's identity; a stamp that wrote anything else would
                    // make a body that count can never find.
                    stamped = false;
                    why = "the new body was stamped with an identity other than " + lease.Identity;
                }
            }
            catch (Exception exception)
            {
                stamped = false;
                why = "giving the new body its identity threw " + SafeFailure.Brief(exception);
            }
        }

        if (!stamped)
        {
            failure = why;
            Discard(instance, view);
            return null;
        }

        failure = string.Empty;
        return instance;
    }

    /// <summary>True when the new body's own network object carries exactly the
    /// leased identity under the contract's key field: what the census reads.
    /// </summary>
    private static bool CarriesLeasedIdentity(ZNetView view, NpcBodyContract contract, NpcIdentity identity)
    {
        if (!NpcBodyFields.TryFor(contract, out NpcBodyFields fields, out _))
        {
            return false;
        }

        ZDO? zdo = view.GetZDO();
        return zdo != null
            && string.Equals(zdo.GetString(fields.Key, string.Empty), identity.Value, StringComparison.Ordinal);
    }

    /// <summary>Why <paramref name="rolePrefab"/> cannot be built from, or null
    /// when it is the prefab the network scene knows by the contract's name.
    /// </summary>
    private static string? DescribeUnregistered(NpcBodyContract contract, GameObject? rolePrefab)
    {
        if (rolePrefab == null)
        {
            return "the role supplied no prefab";
        }

        if (!string.Equals(rolePrefab.name, contract.PrefabName, StringComparison.Ordinal))
        {
            return "the prefab is named '" + rolePrefab.name + "' and the contract names '" + contract.PrefabName + "'";
        }

        ZNetScene scene = ZNetScene.instance;
        if (scene == null)
        {
            return "there is no network scene, so no world is up to build a body in";
        }

        GameObject? registered = scene.GetPrefab(contract.PrefabName);
        if (registered == null || !ReferenceEquals(registered, rolePrefab))
        {
            return "the network scene does not know this prefab by '" + contract.PrefabName
                + "', so a body built from it would be destroyed by the next load";
        }

        return null;
    }

    /// <summary>A finished count of this identity's saved bodies, or a tally
    /// that says the count is not finished - which the gate refuses.</summary>
    private static NpcBodyTally CountSavedBodies(NpcBodyContract contract, NpcIdentity identity)
    {
        // What a body keeps changes what a census can read back, never what it
        // counts, and counting is all that is asked here.
        if (!NpcWorldBodies.TryFor(contract, NpcBodyKeeps.IdentityOnly, out NpcWorldBodies? census, out _)
            || census == null
            || !census.RunToCompletion())
        {
            return NpcBodyTally.NotYetRun(identity);
        }

        return census.TallyFor(identity, loadedBodies: 0, loadedIsFaulted: false);
    }

    private static void Discard(GameObject instance, ZNetView? view)
    {
        if (view != null && view.IsValid() && view.IsOwner())
        {
            view.Destroy();
        }
        else
        {
            UnityEngine.Object.Destroy(instance);
        }
    }

    private static bool IsGroundLoaded(Vector3 position)
    {
        ZoneSystem zones = ZoneSystem.instance;
        return zones != null && zones.IsZoneLoaded(position);
    }

    private static void StripAll<T>(GameObject clone)
        where T : Component
    {
        foreach (T component in clone.GetComponentsInChildren<T>(includeInactive: true))
        {
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    /// <summary>What the base creature is missing, or null when it can be a
    /// worker body.
    ///
    /// <b>Humanoid, not Character.</b> One of the three copies asked only for a
    /// character, which is strictly weaker - every humanoid is a character and
    /// not the reverse - and a body with no humanoid has no inventory, so a
    /// role that stores what it carries would have had nothing to store it in
    /// and would have found out at the first pick-up.</summary>
    private static string? DescribeMissingComponents(GameObject clone)
    {
        if (clone.GetComponent<ZNetView>() == null)
        {
            return "it has no network view, so it cannot be a persistent networked body";
        }

        if (clone.GetComponent<Humanoid>() == null)
        {
            return "it has no humanoid, so it has no body to move and no inventory to carry with";
        }

        if (clone.GetComponent<ZSyncAnimation>() == null)
        {
            return "it has no animation sync, which the AI's own Awake requires";
        }

        if (clone.GetComponent<Rigidbody>() == null)
        {
            return "it has no rigid body on its root, so it cannot walk";
        }

        if (clone.GetComponent<BaseAI>() == null)
        {
            return "it has no AI, so it is not a creature prefab";
        }

        return null;
    }

    /// <summary>Records a failure and says it once.
    ///
    /// The de-duplication is one of the three copies' and is kept: this runs
    /// from an event that can fire on every main-menu return, and a build that
    /// cannot find its base creature would otherwise write the same line into a
    /// player's log forever.</summary>
    private bool Fail(string reason)
    {
        LastFailure = reason;
        if (!string.Equals(_lastLoggedFailure, reason, StringComparison.Ordinal))
        {
            _lastLoggedFailure = reason;
            _log?.Invoke("The body prefab '" + _contract.PrefabName + "' is not available: " + reason
                + ". That role stays off; everything else keeps working.");
        }

        return false;
    }
}
