using System;
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
            if (partner == null) { return; }
            Transform p = partner.transform;
            if (!LadderSnap.TryAlignStack(LadderSegment.Point(bottom), LadderSegment.Point(top),
                    LadderSegment.Point(p.TransformPoint(partner.LocalBottom)), LadderSegment.Point(p.TransformPoint(partner.LocalTop)),
                    out var origin, out _))
            {
                return;
            }

            Vector3 aligned = LadderSegment.Vector(origin);
            if (!LadderSegment.Finite(aligned)) { return; }
            g.rotation = p.rotation;
            g.position = aligned;
        }
        catch
        {
            // On any fault the game's own ghost stands unchanged.
        }
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Own patch only. */ }
        _harmony = null;
        _ghost = null;
    }
}
