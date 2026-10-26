using System;
using System.Collections.Generic;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain;
using TheConcernedCat.ConcernedLadders.Runtime;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Pieces;

/// <summary>Scoped placement adapter (owner correction 2026-10-26): after the game has positioned the placement ghost
/// for this frame, a ghost of one of this product's ladders whose bottom-centre already coincides with a placed
/// ladder's top-centre (or whose top-centre coincides with a placed ladder's bottom-centre) takes that ladder's
/// rotation, so a stacked section is always one aligned column whatever rotation the hammer last used. Nothing else in
/// native building changes: other pieces, other snaps, placement rules, costs and validity are the game's.</summary>
internal sealed class LadderSnapAlignment : IDisposable
{
    private static AccessTools.FieldRef<Player, GameObject>? _ghost;
    private static AccessTools.FieldRef<Player, int>? _manual;
    private static AccessTools.FieldRef<Player, List<Transform>>? _snapPoints;
    private readonly Action<string> _log;
    private Harmony? _harmony;

    internal LadderSnapAlignment(Action<string> log) => _log = log;

    internal bool Install(string harmonyId)
    {
        try
        {
            var update = AccessTools.Method(typeof(Player), "UpdatePlacementGhost", new[] { typeof(bool) });
            var field = AccessTools.Field(typeof(Player), "m_placementGhost");
            if (update == null || field == null || field.FieldType != typeof(GameObject))
            {
                _log("Ladder stack alignment unavailable: the installed game's placement API did not match. Native snapping is unchanged.");
                return false;
            }

            _ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
            // Column extension needs the selected snap mode; without it only the exact stack alignment runs.
            var manual = AccessTools.Field(typeof(Player), "m_manualSnapPoint");
            var points = AccessTools.Field(typeof(Player), "m_tempSnapPoints1");
            if (manual != null && manual.FieldType == typeof(int) && points != null && points.FieldType == typeof(List<Transform>))
            {
                _manual = AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");
                _snapPoints = AccessTools.FieldRefAccess<Player, List<Transform>>("m_tempSnapPoints1");
            }
            else
            {
                _log("Ladder column extension unavailable: the installed game's snap-mode fields did not match. Stack alignment still runs.");
            }

            _harmony = new Harmony(harmonyId);
            _harmony.Patch(update, postfix: new HarmonyMethod(typeof(LadderSnapAlignment), nameof(AfterGhostUpdate)));
            return true;
        }
        catch (Exception exception)
        {
            _log("Ladder stack alignment unavailable (" + SafeFailure.Brief(exception) + "). Native snapping is unchanged.");
            Dispose();
            return false;
        }
    }

    private static void AfterGhostUpdate(Player __instance)
    {
        try
        {
            if (_ghost == null || !ReferenceEquals(__instance, Player.m_localPlayer)) { return; }
            GameObject ghost = _ghost(__instance);
            if (ghost == null || !ghost.activeSelf) { return; }
            LadderSegment? section = ghost.GetComponent<LadderSegment>();
            if (section == null) { return; }
            Transform g = ghost.transform;
            Vector3 bottom = g.TransformPoint(section.LocalBottom);
            Vector3 top = g.TransformPoint(section.LocalTop);
            LadderSegment? partner = LadderSegment.FindEndAt(bottom, LadderSnap.Coincident) ?? LadderSegment.FindEndAt(top, LadderSnap.Coincident);
            if (partner != null)
            {
                Transform p = partner.transform;
                if (LadderSnap.TryAlignStack(LadderSegment.Point(bottom), LadderSegment.Point(top),
                        LadderSegment.Point(p.TransformPoint(partner.LocalBottom)), LadderSegment.Point(p.TransformPoint(partner.LocalTop)),
                        out var origin, out _))
                {
                    Place(g, p.rotation, LadderSegment.Vector(origin));
                }

                return;
            }

            // Owner session 2026-10-26: aimed at the body of a placed ladder, the game finds no snap point within its
            // 0.5 m radius and leaves a valid ghost overlapping the ladder. Extend that column instead, at the end the
            // selected snap point names (Bottom stacks above, Top hangs below, Auto takes the nearer end). An explicit
            // wall-corner mode is the player's choice and stays native.
            if (!TrySelectedEnd(__instance, section, out LadderSnap.Extend extend)) { return; }
            Vector3 ghostOrigin = g.TransformPoint(section.LocalBottom);
            LadderSegment? column = LadderSegment.FindColumnAt(ghostOrigin, ColumnRadius);
            if (column == null) { return; }
            if (!LadderSegment.TryRun(column, Run, out var run)) { return; }
            if (!LadderSnap.TryExtendColumn(LadderSegment.Point(ghostOrigin), run.Bottom, run.Top, ColumnRadius, extend, out var end, out _))
            {
                return;
            }

            Place(g, column.transform.rotation, LadderSegment.Vector(end));
        }
        catch
        {
            // On any fault the game's own ghost stands unchanged.
        }
    }

    private static void Place(Transform ghost, Quaternion rotation, Vector3 origin)
    {
        if (!LadderSegment.Finite(origin)) { return; }
        ghost.rotation = rotation;
        ghost.position = origin;
    }

    /// <summary>Half the ladder's width: a ghost whose origin is this close to a column's centreline is on it.</summary>
    private const float ColumnRadius = 0.4f;

    private static readonly List<LadderSegment> Run = new List<LadderSegment>();

    /// <summary>The column end the selected snap mode names, from the selected snap point's authored position:
    /// bottom-centre stacks above, top-centre hangs below, Auto (-1) takes the nearer end. Anything else, or a game
    /// whose fields did not match, is false.</summary>
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
    }
}
