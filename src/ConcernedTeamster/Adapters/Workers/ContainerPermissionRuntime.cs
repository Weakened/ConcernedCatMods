using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Workers;
using UnityEngine;

namespace TheConcernedCat.ConcernedTeamster.Adapters.Workers;

/// <summary>The player's way to say what Gunnar may do with a chest (#374).
///
/// <b>What this makes true that was not.</b> The permission model has been
/// complete and tested since ConcernedNPC 0.1.0 and reachable by nobody: no
/// product imported it, nothing wrote a permission down, and no player could set
/// one. "OFF by default" was a statement about dead code. Now a player looks at a
/// chest, presses a key, and the four states cycle: off, take, deposit, both,
/// off. Every container starts at off, and off is the absence of a record, so an
/// empty file means "nothing is enabled" rather than "nothing is known".
///
/// <b>It adds a key; it does not replace an interaction.</b> Vanilla's own Use on
/// a container is untouched - this reads the object the player is already
/// hovering, exactly as the shipped companion door permission does, and reports
/// through a centre message. That is #374's "use the existing interaction
/// conventions rather than replacing vanilla's primary interaction", and it is
/// also the reason there is no Harmony patch here: Teamster has never needed one
/// and this did not change that.
///
/// <b>Nothing here consumes a permission yet.</b> Reading it in a transfer is the
/// next slice of #374, and saying so is the difference between this being honest
/// and this being the sixth isolated abstraction. What a player can do today is
/// mark a chest, see the mark survive a reload, and read the marks back with
/// <c>ct_collect chest</c>.
///
/// <b>What it will not mark.</b> A container that moves - one riding a cart - has
/// no place, because a permission is found again by position and attaching one to
/// the patch of ground a cart happened to be standing on would hand it to
/// whatever is parked there tomorrow. The library cannot tell; this can, so this
/// refuses.</summary>
internal sealed class ContainerPermissionRuntime : MonoBehaviour
{
    private readonly ContainerPermissionStore _store = new ContainerPermissionStore();

    private TeamsterSettings _settings = null!;
    private ManualLogSource? _log;
    private NpcContainerDesk _desk = new NpcContainerDesk();
    private long? _world;
    private bool _announcedNotice;
    private bool _faulted;

    /// <summary>Installs the runtime. Always installed, like the other worker
    /// runtimes: the key decides what a player can do, not what exists, so an
    /// unbound key is a product with no way to enable a container rather than a
    /// different plugin state.</summary>
    internal static ContainerPermissionRuntime Install(
        GameObject host, TeamsterSettings settings, ManualLogSource? log)
    {
        ContainerPermissionRuntime runtime = host.AddComponent<ContainerPermissionRuntime>();
        runtime._settings = settings;
        runtime._log = log;
        return runtime;
    }

    /// <summary>What the player has allowed, for a transfer to consult once one
    /// exists.
    ///
    /// <b>It takes the container, not a position and a name.</b> The identity a
    /// permission is recorded under comes from the ZDO where there is one, and a
    /// caller that assembled its own position and prefab could easily assemble a
    /// different one - and then read OFF for a chest the player had enabled,
    /// which looks exactly like the permission not working. One derivation, in
    /// here, used by both the write and the read.</summary>
    internal NpcContainerUse Allowance(Container? container)
    {
        // Three ways this refuses rather than answers, and the third is the one
        // a review had to point out. A faulted runtime has stopped its Update,
        // so `_world` and `_desk` are frozen at whatever world it last saw - and
        // a later world would then be told about that world's chests. An answer
        // that cannot be trusted is a refusal, and this is what makes that
        // sentence true rather than merely written down.
        if (_faulted || _world == null || container == null)
        {
            return NpcContainerUse.Off;
        }

        if (!Fixed(container))
        {
            // A container that moves was never recorded, so it can never be
            // allowed - and saying so here means a caller cannot get a grant for
            // one by looking it up instead of marking it.
            return NpcContainerUse.Off;
        }

        int prefab = PrefabKeyOf(container);
        if (prefab == 0)
        {
            // Prefab 0 matches ANY prefab inside the library's place rule, so
            // answering from it would be answering about a different kind of
            // container standing where this one stands.
            return NpcContainerUse.Off;
        }

        return _desk.Allowance(PointOf(PositionOf(container)), prefab);
    }

    private void Update()
    {
        try
        {
            long? world = CurrentWorld();
            if (world != _world)
            {
                SwitchTo(world);
            }

            if (_world == null || !_settings.Enabled.Value)
            {
                return;
            }

            KeyboardShortcut shortcut = _settings.ContainerPermissionShortcut.Value;
            if (shortcut.MainKey != KeyCode.None && shortcut.IsDown() && !KeyboardIsElsewhere())
            {
                ToggleHovered();
            }
        }
        catch (Exception exception)
        {
            // A failure here must not take the rest of Teamster down with it, and
            // it must not leave a permission half-set: the desk is only changed
            // by ToggleHovered, which saves or says why. Faulted also means
            // Allowance refuses from here on, because this runtime has stopped
            // following which world it is in.
            _faulted = true;
            _log?.LogWarning(
                "The container permission key could not be handled, and container permissions are " +
                "off for this session: " + Brief(exception));
            enabled = false;
        }
    }

    /// <summary>Whether the keyboard belongs to something other than the world.
    ///
    /// <b>Why this is not optional, and why it is the first thing a review found.
    /// </b> Without it, typing anything containing the bound key into the chat,
    /// the console, a sign or a rename box advances the chest the player happens
    /// to be looking at - which GRANTS a permission by accident, and "a
    /// permission is only ever lost in the direction of off" stops being true of
    /// the most ordinary action there is. The inventory check matters most of
    /// all: having the chest open is exactly the state a player is in when they
    /// are typing about it.
    ///
    /// These are the same six the shipped companion door hotkey uses, minus
    /// Cartographer's own text-focus helper, which is that product's. Every one
    /// is wrapped, because a build that does not have one of them must not turn
    /// a missing method into a permission change.</summary>
    private static bool KeyboardIsElsewhere()
    {
        try
        {
            return Minimap.IsOpen()
                || Minimap.InTextInput()
                || InventoryGui.IsVisible()
                || (Chat.instance != null && Chat.instance.HasFocus())
                || global::Console.IsVisible()
                || (TextInput.instance != null && TextInput.instance.m_panel != null
                    && TextInput.instance.m_panel.activeSelf);
        }
        catch
        {
            // Cannot tell whose keyboard it is, so assume it is not the world's:
            // a missed key press costs a second press, and the alternative is a
            // permission the player did not ask for.
            return true;
        }
    }

    /// <summary>A world came up, went away, or changed. Permissions are per
    /// world, so the desk is replaced rather than carried over - a chest at the
    /// same coordinates in a different world is a different chest.</summary>
    private void SwitchTo(long? world)
    {
        if (_world != null && _desk.IsDirty)
        {
            // Last chance: a key pressed in the same frame the world went down.
            // The player is past being told on screen, so the log gets it - this
            // is the one write path that cannot report into a reply.
            if (!_store.Save(_world.Value, _desk))
            {
                _log?.LogWarning(
                    "A container permission set as the world closed could not be saved: " +
                    (_store.Notice ?? "unknown reason"));
            }
        }

        _world = world;
        _announcedNotice = false;
        _desk = world == null ? new NpcContainerDesk() : _store.Load(world.Value);

        if (world != null)
        {
            _log?.LogInfo(
                "Gunnar may use " + _desk.Count.ToString(CultureInfo.InvariantCulture) +
                " container(s) you enabled in this world.");
            AnnounceNoticeOnce();
        }
    }

    private void ToggleHovered()
    {
        GameObject? hovered = HoveredObject();
        Container? container = hovered == null ? null : hovered.GetComponentInParent<Container>();
        if (container == null)
        {
            return;
        }

        if (!Fixed(container))
        {
            Notify(TeamsterStrings.Get("containers.moves"));
            return;
        }

        NpcPoint point = PointOf(PositionOf(container));
        if (!NpcContainerDesk.CanBeRemembered(point))
        {
            Notify(TeamsterStrings.Get("containers.noPlace"));
            return;
        }

        int prefab = PrefabKeyOf(container);
        if (prefab == 0)
        {
            // Prefab 0 is a WILDCARD inside the library's place rule: it matches
            // any prefab at that spot. Recording one would mean a different kind
            // of container built there later inherits this permission - a gain,
            // and it would falsify "a different piece rebuilt on the same spot is
            // a different decision". The book keeps the place a permission was
            // FIRST recorded at, so a wildcard written once stays a wildcard for
            // the life of the file even after the name becomes readable. Refuse.
            Notify(TeamsterStrings.Get("containers.noKind"));
            return;
        }

        NpcContainerUse now = _desk.Cycle(point, prefab);

        if (_world != null && !_store.Save(_world.Value, _desk))
        {
            // The change is in memory and not on disk. Say so rather than let a
            // player believe a mark survived that will not.
            Notify(TeamsterStrings.Format("containers.notSaved", Describe(now)));
            AnnounceNotice();
            return;
        }

        Notify(Describe(now));
    }

    /// <summary>`ct_collect chest [list|clear]`: what is enabled, and a way back
    /// to nothing. A diagnostic, not the player-facing surface - the key is that
    /// - and deliberately read-only apart from `clear`, because setting a
    /// permission should require looking at the chest it is about.</summary>
    internal string Console(string remainder)
    {
        string what = string.IsNullOrEmpty(remainder)
            ? "status"
            : remainder.Trim().ToLowerInvariant();

        if (_world == null)
        {
            return "Concerned Teamster: no world is loaded, so no container permissions are in force.";
        }

        switch (what)
        {
            case "clear":
            {
                int count = _desk.Clear();
                bool saved = _store.Save(_world.Value, _desk);
                return "Forgot " + count.ToString(CultureInfo.InvariantCulture) +
                    " container permission(s)." +
                    (saved ? "" : " They could not be saved: " + (_store.Notice ?? "unknown reason"));
            }

            case "list":
            {
                if (_desk.Count == 0)
                {
                    return "No containers are enabled in this world. Look at a chest and press " +
                        KeyName() + ".";
                }

                var text = new StringBuilder();
                text.Append(_desk.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" container(s) enabled:");
                foreach (NpcContainerDecision decision in _desk.Decisions)
                {
                    text.Append("\n  ")
                        .Append(ContainerPermissionRows.Name(decision.Allowed))
                        .Append(" at ")
                        .Append(Coordinate(decision.X)).Append(", ")
                        .Append(Coordinate(decision.Y)).Append(", ")
                        .Append(Coordinate(decision.Z));
                }

                return text.ToString();
            }

            default:
                return "Containers: " + _desk.Count.ToString(CultureInfo.InvariantCulture) +
                    " enabled in this world, key " + KeyName() +
                    (_store.IsReadOnly ? ", NOT being saved (the file could not be read)" : "") +
                    (_store.Dropped > 0
                        ? ", " + _store.Dropped.ToString(CultureInfo.InvariantCulture) + " unreadable record(s) dropped"
                        : "") +
                    ". Nothing consumes these yet - the transfer that does is the next part of #374. " +
                    "Subcommands: status, list, clear.";
        }
    }

    private void OnDestroy()
    {
        try
        {
            if (_world != null && _desk.IsDirty)
            {
                _store.Save(_world.Value, _desk);
            }
        }
        catch
        {
            // A teardown that throws is worse than an unsaved permission, and
            // the permission is only ever unsaved in the direction of off.
        }
    }

    private void AnnounceNoticeOnce()
    {
        if (!_announcedNotice)
        {
            AnnounceNotice();
        }
    }

    private void AnnounceNotice()
    {
        _announcedNotice = true;
        if (!string.IsNullOrEmpty(_store.Notice))
        {
            _log?.LogWarning(_store.Notice);
        }
    }

    private string KeyName()
    {
        KeyboardShortcut shortcut = _settings.ContainerPermissionShortcut.Value;
        return shortcut.MainKey == KeyCode.None ? "(unbound)" : shortcut.ToString();
    }

    /// <summary>What the player is told, through the catalog. This product's
    /// README promises that every user-facing string resolves through it, and a
    /// raw literal here would have been the only on-screen text in Teamster that
    /// did not.</summary>
    private static string Describe(NpcContainerUse use)
    {
        switch (use)
        {
            case NpcContainerUse.Take:
                return TeamsterStrings.Get("containers.now.take");
            case NpcContainerUse.Deposit:
                return TeamsterStrings.Get("containers.now.deposit");
            case NpcContainerUse.Both:
                return TeamsterStrings.Get("containers.now.both");
            default:
                return TeamsterStrings.Get("containers.now.off");
        }
    }

    /// <summary>Whether the container stays put, decided structurally rather
    /// than by naming the things that move.
    ///
    /// <b>A first version of this named two types</b> - <c>Vagon</c> and
    /// <c>Ship</c> - and a review pointed out that "a container that moves is
    /// refused" is then a claim about exactly those two: any other container with
    /// a body, a modded wagon or raft or moving platform included, was accepted
    /// and had its permission attached to the ground it happened to be on.
    ///
    /// So the test is now what a chest that stays put actually HAS: a
    /// <c>Piece</c>, which is what the game gives a built thing, and no
    /// non-kinematic <c>Rigidbody</c> above it, which is what the game gives a
    /// thing that is carried. The two named types are kept as well, because they
    /// are the cases this was written for and a belt is cheap.</summary>
    private static bool Fixed(Container container)
    {
        try
        {
            if (container.GetComponentInParent<Vagon>() != null
                || container.GetComponentInParent<Ship>() != null)
            {
                return false;
            }

            Rigidbody body = container.GetComponentInParent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                return false;
            }

            // A built piece has a Piece. Something with none is not a chest
            // somebody placed, and this refuses rather than guessing.
            return container.GetComponentInParent<Piece>() != null;
        }
        catch
        {
            // Cannot tell, so refuse: a permission attached to moving ground is
            // handed to whatever parks there tomorrow.
            return false;
        }
    }

    /// <summary>Where the game says this container is. The ZDO's position
    /// first, because that is the SAVED position and it is what will be there
    /// after a reload; the transform only when there is no ZDO to ask. The
    /// shipped door permission reads it the same way round, for the same reason.
    /// </summary>
    private static Vector3 PositionOf(Container container)
    {
        try
        {
            ZNetView view = container.GetComponent<ZNetView>();
            ZDO? zdo = view == null || !view.IsValid() ? null : view.GetZDO();
            if (zdo != null)
            {
                Vector3 saved = zdo.GetPosition();
                if (!float.IsNaN(saved.x) && !float.IsNaN(saved.y) && !float.IsNaN(saved.z))
                {
                    return saved;
                }
            }
        }
        catch
        {
            // Fall through to the transform.
        }

        return container.transform.position;
    }

    /// <summary>The container's kind, or empty. <b>Never `container.name`.</b>
    /// An instantiated object is called `piece_chest_wood(Clone)`, which hashes
    /// to something different from `piece_chest_wood` - so one frame where the
    /// prefab name could not be read would record the permission under a name
    /// nothing looks up again, and the mark would vanish silently on the next
    /// reload. Empty is refused by the caller instead.</summary>
    private static string PrefabName(Container container)
    {
        try
        {
            ZNetView view = container.GetComponent<ZNetView>();
            string? name = view == null ? null : view.GetPrefabName();
            return string.IsNullOrEmpty(name) ? string.Empty : name!;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>The container's kind as one stable number: the ZDO's own saved
    /// prefab hash when there is one, else the hash of the prefab name. 0 means
    /// "could not tell", and the callers refuse on it rather than recording a
    /// wildcard.</summary>
    private static int PrefabKeyOf(Container container)
    {
        try
        {
            ZNetView view = container.GetComponent<ZNetView>();
            ZDO? zdo = view == null || !view.IsValid() ? null : view.GetZDO();
            int saved = zdo == null ? 0 : zdo.GetPrefab();
            if (saved != 0)
            {
                return saved;
            }
        }
        catch
        {
            // Fall through to the name.
        }

        return PrefabKey(PrefabName(container));
    }

    /// <summary>The prefab as one stable number. A name hash rather than the name
    /// itself, because the record holds a number and the name is the game's.
    /// </summary>
    private static int PrefabKey(string prefabName) =>
        string.IsNullOrEmpty(prefabName) ? 0 : prefabName.GetStableHashCode();

    private static NpcPoint PointOf(Vector3 position) =>
        new NpcPoint(position.x, position.y, position.z);

    private static string Coordinate(float value) =>
        value.ToString("F1", CultureInfo.InvariantCulture);

    private static GameObject? HoveredObject()
    {
        try
        {
            Player player = Player.m_localPlayer;
            return player == null ? null : player.GetHoverObject();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The world, or null. <b>Zero is null.</b> `GetWorldUID` answers 0
    /// while the world is not resolved yet, and this product already treats 0 as
    /// "no world" everywhere else it asks. Accepting it would give every
    /// unresolved frame in every save the same permission file
    /// (`teamster_containers_0.tsv`), so a chest marked in one world's first
    /// frames would be in force in the next world - a permission GAINED across
    /// saves, which is the one direction this must never go.</summary>
    private static long? CurrentWorld()
    {
        try
        {
            if (ZNet.instance == null)
            {
                return null;
            }

            long uid = ZNet.instance.GetWorldUID();
            return uid == 0L ? (long?)null : uid;
        }
        catch
        {
            return null;
        }
    }

    private static string Brief(Exception exception) =>
        Domain.Support.SupportBundleSanitizer.Sanitize(
            exception.GetType().Name + ": " + exception.Message);

    private static void Notify(string text)
    {
        try
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null)
            {
                hud.ShowMessage(MessageHud.MessageType.Center, text);
            }
        }
        catch
        {
            // A notice that could not be shown is not worth a fault.
        }
    }
}
