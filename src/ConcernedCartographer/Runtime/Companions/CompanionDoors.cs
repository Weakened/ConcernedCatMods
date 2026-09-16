using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using TheConcernedCat.Companions.Identity;
using TheConcernedCat.Companions.Persistence;
using TheConcernedCat.Companions.Placement;
using TheConcernedCat.Companions.Surroundings;
using TheConcernedCat.ConcernedCartographer.Reporting;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime.Companions;

/// <summary>Which doors companions may use, as the running game sees them: the
/// per-world door book, a vanilla <c>Door</c> turned into a doorway, and the one
/// line a door's own hover text gains.
///
/// Nothing here writes to the world. The book is a file beside the companion
/// sidecars; the hover line is appended to the text the door already returns;
/// the doorway is read off the door's transform and its ZDO.</summary>
internal sealed class CompanionDoors
{
    /// <summary>Half the width of a doorway, generously: <c>wood_door</c>'s
    /// leaf is 1.39 m wide and <c>wood_gate</c>'s 1.68 m, both centred on the
    /// piece. A route crossing the wall beside a door within this is going
    /// through the door - the wall would have stopped it otherwise.</summary>
    private const float HalfWidthMetres = 1f;

    /// <summary>How far below a door piece's origin its floor is when the
    /// footing probe cannot say: vanilla doors are centred on a 2 m tall
    /// opening, so the floor is a metre down.</summary>
    private const float OriginAboveFloorMetres = 1f;

    private readonly ManualLogSource _log;
    private readonly DoorAccessStore _store;
    private readonly Func<DoorAccessPolicy> _policy;

    private WorldId _world;
    private DoorAccessBook _book = new DoorAccessBook();
    private bool _readOnly;

    public CompanionDoors(ManualLogSource log, string dataDirectory, Func<DoorAccessPolicy> policy)
    {
        _log = log;
        _store = new DoorAccessStore(dataDirectory);
        _policy = policy;
    }

    public DoorAccessPolicy Policy => _policy();

    public IReadOnlyList<DoorPlace> Allowed => _book.Allowed;

    /// <summary>True once a world's book is loaded.</summary>
    public bool Ready => _world.IsValid;

    /// <summary>Loads this world's book, unless it is the one already loaded.
    /// </summary>
    public void UseWorld(WorldId world)
    {
        if (!world.IsValid || world.Equals(_world))
        {
            return;
        }

        DoorAccessStore.LoadReport report = _store.Load(world);
        _world = world;
        _book = report.Book;
        _readOnly = report.ReadOnly;
        if (report.Notice != null)
        {
            _log.LogInfo(report.Notice);
        }

        _log.LogInfo(
            $"Companions may use {_book.Allowed.Count} door(s) you allowed in this world" +
            (Policy == DoorAccessPolicy.AllDoors ? ", and every other door too (DoorAccess = AllDoors)." : "."));
    }

    public void Forget()
    {
        _world = WorldId.None;
        _book = new DoorAccessBook();
        _readOnly = false;
    }

    public bool IsAllowed(Door door)
    {
        return Ready && _book.IsAllowed(PlaceOf(door), Policy);
    }

    /// <summary>Lets companions through this door, or stops them, and saves.
    /// Returns whether they may use it now.</summary>
    public bool Toggle(Door door)
    {
        bool allowed = _book.Toggle(PlaceOf(door));
        Save();
        return allowed;
    }

    public int ForgetAllDoors()
    {
        int count = _book.Clear();
        Save();
        return count;
    }

    private void Save()
    {
        if (!Ready)
        {
            return;
        }

        string? notice = _store.Save(_world, _book, _readOnly);
        if (notice != null)
        {
            _log.LogInfo(notice);
        }
    }

    /// <summary>A door's lasting identity: its piece type and where it stands,
    /// read from its ZDO when it has one. Not the ZDO's id - the game renumbers
    /// those every time a world loads.</summary>
    public static DoorPlace PlaceOf(Door door)
    {
        Vector3 position = door.transform.position;
        int prefab = 0;
        try
        {
            ZNetView? view = door.GetComponent<ZNetView>();
            ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo != null)
            {
                position = zdo.GetPosition();
                prefab = zdo.GetPrefab();
            }
        }
        catch
        {
            // The transform is the same place.
        }

        return new DoorPlace(new WorldPoint(position.x, position.y, position.z), prefab);
    }

    /// <summary>0 shut, anything else open, the way the door reads it.
    /// </summary>
    public static int StateOf(Door door)
    {
        try
        {
            ZNetView? view = door.GetComponent<ZNetView>();
            ZDO? zdo = view == null ? null : view.GetZDO();
            return zdo == null ? 0 : zdo.GetInt(ZDOVars.s_state);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Whether a player could open it right now: shut, no key, can be
    /// closed again, and guard-stone access allowed here - the same doors the
    /// local player could open, and nothing else.</summary>
    public static bool CanOpen(Door door)
    {
        try
        {
            return StateOf(door) == 0 &&
                door.m_keyItem == null &&
                !door.m_canNotBeClosed &&
                (!door.m_checkGuardStone || PrivateArea.CheckAccess(door.transform.position, 0f, flash: false));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The door as a doorway a route can be checked against.</summary>
    public DoorPortal PortalOf(Door door)
    {
        Vector3 centre = door.transform.position;
        Vector3 floor = CompanionFooting.TryFind(centre, 0.5f, 2.5f, out Vector3 ground, out _)
            ? ground
            : centre - (Vector3.up * OriginAboveFloorMetres);

        Vector3 across = door.transform.forward;
        across.y = 0f;

        return new DoorPortal(
            PlaceOf(door),
            new WorldPoint(centre.x, floor.y, centre.z),
            across.x,
            across.z,
            HalfWidthMetres,
            isOpen: StateOf(door) != 0,
            canOpen: CanOpen(door),
            allowed: IsAllowed(door));
    }

    /// <summary>Every door piece within <paramref name="radius"/> of
    /// <paramref name="centre"/>, from the game's own list of loaded pieces -
    /// no physics query, so a large base cannot overflow a buffer and hide one.
    /// </summary>
    public static void FindDoors(Vector3 centre, float radius, List<Door> doors)
    {
        doors.Clear();
        var pieces = new List<Piece>();
        try
        {
            Piece.GetAllPiecesInRadius(centre, radius, pieces);
        }
        catch
        {
            return;
        }

        foreach (Piece piece in pieces)
        {
            Door? door = piece == null ? null : piece.GetComponent<Door>();
            if (door != null)
            {
                doors.Add(door);
            }
        }
    }
}

/// <summary>The line a vanilla door's hover text gains while companions are
/// about: whether they may use it, and the key that changes that.
///
/// A postfix on <c>Door.GetHoverText</c>, appended to whatever the game
/// returned and nothing else - no input is intercepted, and pressing Use on a
/// door does exactly what it always did. With no describer set, the text is
/// the game's own, untouched.</summary>
internal static class CompanionDoorHover
{
    /// <summary>What to add for a door, or null for nothing. Set by the
    /// director while there is a companion to care.</summary>
    public static Func<Door, string?>? Describe;

    private static Harmony? s_harmony;

    public static void Install(ManualLogSource log)
    {
        if (s_harmony != null)
        {
            return;
        }

        try
        {
            var target = AccessTools.Method(typeof(Door), nameof(Door.GetHoverText));
            if (target == null)
            {
                log.LogInfo(
                    "Doors do not show the companion line on this build (Door.GetHoverText not found). " +
                    "cc_companion doors still lists and resets them.");
                return;
            }

            var harmony = new Harmony(Plugin.PluginGuid + ".companiondoors");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(CompanionDoorHover), nameof(AfterHoverText)));
            s_harmony = harmony;
        }
        catch (Exception exception)
        {
            s_harmony = null;
            log.LogInfo($"Doors do not show the companion line on this build: {SafeLogText.Brief(exception)}");
        }
    }

    private static void AfterHoverText(Door __instance, ref string __result)
    {
        try
        {
            Func<Door, string?>? describe = Describe;
            if (describe == null || __instance == null || string.IsNullOrEmpty(__result))
            {
                return;
            }

            string? line = describe(__instance);
            if (!string.IsNullOrEmpty(line))
            {
                __result += "\n" + line;
            }
        }
        catch
        {
            // The door's own text stands.
        }
    }
}
