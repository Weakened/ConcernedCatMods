using System;
using System.Globalization;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using TheConcernedCat.ConcernedNPC.Containers;
using TheConcernedCat.ConcernedNPC.Storage;
using TheConcernedCat.ConcernedNPC.Work;
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
    /// exists. Off while no world is loaded: an answer that cannot be trusted is
    /// a refusal, never a grant.</summary>
    internal NpcContainerUse Allowance(Vector3 position, string prefabName)
    {
        if (_world == null)
        {
            return NpcContainerUse.Off;
        }

        return _desk.Allowance(PointOf(position), PrefabKey(prefabName));
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
            if (shortcut.MainKey != KeyCode.None && shortcut.IsDown())
            {
                ToggleHovered();
            }
        }
        catch (Exception exception)
        {
            // A failure here must not take the rest of Teamster down with it, and
            // it must not leave a permission half-set: the desk is only changed
            // by ToggleHovered, which saves or says why.
            _log?.LogWarning(
                "The container permission key could not be handled: " + Brief(exception));
            enabled = false;
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
            _store.Save(_world.Value, _desk);
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
            Notify("That container moves, so a permission for it could not be found again. " +
                "Only a chest that stays put can be enabled.");
            return;
        }

        Vector3 position = container.transform.position;
        NpcPoint point = PointOf(position);
        if (!NpcContainerDesk.CanBeRemembered(point))
        {
            Notify("That container has no position this build can read, so it cannot be enabled.");
            return;
        }

        string name = PrefabName(container);
        NpcContainerUse now = _desk.Cycle(point, PrefabKey(name));

        if (_world != null && !_store.Save(_world.Value, _desk))
        {
            // The change is in memory and not on disk. Say so rather than let a
            // player believe a mark survived that will not.
            Notify(Describe(now) + " - but it could NOT be saved, so it will be gone next session.");
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

    private static string Describe(NpcContainerUse use)
    {
        switch (use)
        {
            case NpcContainerUse.Take:
                return "Gunnar may take from this container.";
            case NpcContainerUse.Deposit:
                return "Gunnar may put things into this container.";
            case NpcContainerUse.Both:
                return "Gunnar may take from and put into this container.";
            default:
                return "Gunnar may not use this container.";
        }
    }

    /// <summary>Whether the container stays put. A container parented under a
    /// cart moves with it, and the library cannot see that.</summary>
    private static bool Fixed(Container container)
    {
        try
        {
            return container.GetComponentInParent<Vagon>() == null
                && container.GetComponentInParent<Ship>() == null;
        }
        catch
        {
            // Cannot tell, so refuse: a permission attached to moving ground is
            // handed to whatever parks there tomorrow.
            return false;
        }
    }

    private static string PrefabName(Container container)
    {
        try
        {
            ZNetView view = container.GetComponent<ZNetView>();
            string? name = view == null ? null : view.GetPrefabName();
            return string.IsNullOrEmpty(name) ? container.name : name!;
        }
        catch
        {
            return container.name;
        }
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

    private static long? CurrentWorld()
    {
        try
        {
            return ZNet.instance == null ? (long?)null : ZNet.instance.GetWorldUID();
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
