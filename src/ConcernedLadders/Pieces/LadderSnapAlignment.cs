using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Scoped placement adapter (owner correction 2026-10-26). A ghost of one of this product's ladders is moved
/// onto the connected column it belongs to, so a stacked section is always one aligned column whatever rotation the
/// hammer last used:
/// <list type="bullet">
/// <item>the game has already snapped the ghost's bottom-centre onto a placed ladder's top-centre (or its top-centre
/// onto a bottom-centre): the ghost takes that ladder's rotation and lies exactly on it;</item>
/// <item>the placement ray hits a placed ladder's rails (the game found no snap point within its 0.5 m radius and would
/// leave the ghost overlapping or beside the ladder): the ghost extends that column at the end the selected snap mode
/// names. An explicit wall-corner mode stays native.</item>
/// </list>
/// The move happens inside the game's own placement update, after its snapping and before its validity checks (the
/// no-build zone, ward access, player overlap, biome and clipping tests all read the moved ghost), so nothing is ever
/// placed where the game would refuse it. Held "toggle snapping" (free placement) is respected. Other pieces, costs and
/// rules are the game's. Any mismatch in the installed game's placement API leaves native snapping untouched.</summary>
internal sealed class LadderSnapAlignment : IDisposable
{
    /// <summary>The game's placement ray layers (Player.m_placeRayMask, 1.0.20).</summary>
    private static readonly string[] PlaceRayLayers = { "Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle" };

    private static AccessTools.FieldRef<Player, GameObject>? _ghost;
    private static AccessTools.FieldRef<Player, int>? _manual;
    private static AccessTools.FieldRef<Player, List<Transform>>? _snapPoints;
    private static AccessTools.FieldRef<Player, bool>? _altPlace;
    private static Player? _updating;
    private static int _placeMask;
    private static readonly List<LadderSegment> Run = new List<LadderSegment>();
    private readonly Action<string> _log;
    private Harmony? _harmony;

    internal LadderSnapAlignment(Action<string> log) => _log = log;

    internal bool Install(string harmonyId)
    {
        try
        {
            var update = AccessTools.Method(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) });
            var noBuild = AccessTools.Method(typeof(Location), "IsInsideNoBuildLocation", new[] { typeof(Vector3) });
            if (update == null || noBuild == null || noBuild.ReturnType != typeof(bool) ||
                !Is<GameObject>("m_placementGhost") || !Is<int>("m_manualSnapPoint") ||
                !Is<List<Transform>>("m_tempSnapPoints1") || !Is<bool>("m_altPlace"))
            {
                _log("Ladder stack alignment unavailable: the installed game's placement API did not match. Native snapping is unchanged.");
                return false;
            }

            _ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            _manual = AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");
            _snapPoints = AccessTools.FieldRefAccess<Player, List<Transform>>("m_tempSnapPoints1");
            _altPlace = AccessTools.FieldRefAccess<Player, bool>("m_altPlace");
            _placeMask = LayerMask.GetMask(PlaceRayLayers);
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(update, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeGhostUpdate)),
                finalizer: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(AfterGhostUpdate)));
            _harmony.Patch(noBuild, prefix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(BeforeValidity)));
            return true;
        }
        catch (Exception exception)
        {
            _log("Ladder stack alignment unavailable (" + SafeFailure.Brief(exception) + "). Native snapping is unchanged.");
            Dispose();
            return false;
        }
    }

    private static bool Is<T>(string name)
    {
        var field = AccessTools.Field(typeof(Player), name);
        return field != null && field.FieldType == typeof(T);
    }

    private static void BeforeGhostUpdate(Player __instance) =>
        _updating = ReferenceEquals(__instance, Player.m_localPlayer) ? __instance : null;

    private static Exception? AfterGhostUpdate(Exception? __exception)
    {
        _updating = null;
        return __exception;
    }

    /// <summary>The game's placement update calls the no-build test exactly once, after it has snapped the ghost and
    /// before every validity check; the test itself then reads the aligned position.</summary>
    private static void BeforeValidity(ref Vector3 __0)
    {
        Player? player = _updating;
        _updating = null; // Once per update; any other caller of the no-build test is untouched.
        if (player == null) { return; }
        try
        {
            if (Align(player, out Vector3 aligned)) { __0 = aligned; }
        }
        catch
        {
            // On any fault the game's own ghost stands unchanged.
        }
    }

    private static bool Align(Player player, out Vector3 aligned)
    {
        aligned = default;
        if (_ghost == null || SnappingSuppressed(player)) { return false; }
        GameObject ghost = _ghost(player);
        if (ghost == null || !ghost.activeSelf) { return false; }
        LadderSegment? section = ghost.GetComponent<LadderSegment>();
        if (section == null) { return false; }
        Transform g = ghost.transform;
        Vector3 bottom = g.TransformPoint(section.LocalBottom);
        Vector3 top = g.TransformPoint(section.LocalTop);
        LadderSegment? partner = LadderSegment.FindEndAt(bottom, LadderSnap.Coincident) ?? LadderSegment.FindEndAt(top, LadderSnap.Coincident);
        if (partner != null)
        {
            Transform p = partner.transform;
            return LadderSnap.TryAlignStack(LadderSegment.Point(bottom), LadderSegment.Point(top),
                       LadderSegment.Point(p.TransformPoint(partner.LocalBottom)), LadderSegment.Point(p.TransformPoint(partner.LocalTop)),
                       out var origin, out _) &&
                   Place(g, p.rotation, LadderSegment.Vector(origin), out aligned);
        }

        // Owner session 2026-10-26: aimed at the rails of a placed ladder, the game finds no snap point within its
        // 0.5 m radius and leaves the ghost overlapping or beside the ladder. The game's own placement ray (same
        // origin, direction, length and layers) says which piece is aimed at.
        if (!TrySelectedEnd(player, section, out LadderSnap.Extend extend) || GameCamera.instance == null) { return false; }
        Transform camera = GameCamera.instance.transform;
        if (!Physics.Raycast(camera.position, camera.forward, out RaycastHit hit, 50f, _placeMask) || hit.collider == null ||
            hit.collider.attachedRigidbody != null)
        {
            return false;
        }

        LadderSegment? column = hit.collider.GetComponentInParent<LadderSegment>();
        if (column == null || !column.IsLive || !LadderSegment.TryRun(column, Run, out var run)) { return false; }
        return LadderSnap.TryExtendColumn(hit.point.y, run.Bottom, run.Top, extend, out var end, out _) &&
               Place(g, column.transform.rotation, LadderSegment.Vector(end), out aligned);
    }

    /// <summary>The game's own "toggle snapping" hold (Player.UpdatePlacementGhost's free-placement flag).</summary>
    private static bool SnappingSuppressed(Player player)
    {
        if (ZInput.IsNonClassicFunctionality() && ZInput.IsGamepadActive()) { return _altPlace == null || _altPlace(player); }
        return ZInput.GetButton("AltPlace") || (ZInput.GetButton("JoyAltPlace") && !ZInput.GetButton("JoyRotate"));
    }

    private static bool Place(Transform ghost, Quaternion rotation, Vector3 origin, out Vector3 placed)
    {
        placed = origin;
        if (!LadderSegment.Finite(origin)) { return false; }
        ghost.rotation = rotation;
        ghost.position = origin;
        return true;
    }

    /// <summary>The column end the selected snap mode names, from the selected snap point's authored position:
    /// bottom-centre stacks above, top-centre hangs below, Auto (-1) takes the nearer end. Anything else is false.</summary>
    private static bool TrySelectedEnd(Player player, LadderSegment section, out LadderSnap.Extend extend)
    {
        extend = LadderSnap.Extend.Nearest;
        if (_manual == null || _snapPoints == null) { return false; }
        int index = _manual(player);
        if (index == -1) { return true; }
        List<Transform> points = _snapPoints(player);
        if (points == null || index < 0 || index >= points.Count || points[index] == null) { return false; }
        Vector3 local = points[index].localPosition;
        if ((local - section.LocalBottom).sqrMagnitude < 1e-4f) { extend = LadderSnap.Extend.Above; return true; }
        if ((local - section.LocalTop).sqrMagnitude < 1e-4f) { extend = LadderSnap.Extend.Below; return true; }
        return false;
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch only. */ }
        _harmony = null;
        _ghost = null;
        _manual = null;
        _snapPoints = null;
        _altPlace = null;
        _updating = null;
    }
}
