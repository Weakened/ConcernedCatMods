using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheConcernedCat.ConcernedLadders.Domain.Excavation;
using TheConcernedCat.Diagnostics;
using UnityEngine;

namespace TheConcernedCat.ConcernedLadders.Excavation;

/// <summary>The game's own tools clear earth blocks (#584, owner playtest 2026-11-11: "Add a way to get rid of filling
/// material, level grounding or raising should have them go so we can have one continuous digout area" and "Pickaxe should
/// also destroy the filling material").
/// <list type="bullet">
/// <item><b>Terrain tools.</b> A vanilla terrain operation (the hoe's level and raise, the pickaxe's dig, a terrain piece
/// placed with the hoe) runs <c>TerrainOp.Awake</c> on the peer that used the tool, after the game's own ward and no-build
/// checks let it be placed (read from the installed game). Before it applies, the earth blocks within its height-changing
/// radius (<see cref="EarthFill.ClearRadius"/>) are removed through the pinned clearing site, so the ground it levels or
/// raises is one continuous area. Painting alone, and the Iron Shovel's own operations, clear nothing.</item>
/// <item><b>Pickaxe hits.</b> The block itself breaks under a pickaxe through the game's own <c>Destructible</c> (set up by
/// the block's registration). The game's <c>Destructible</c> asks no ward, so a hit on a block inside a ward that does not
/// grant the attacking local player access is refused here, with the ward's own flash, as its terrain tools are.</item>
/// </list>
/// No block is created and nothing else is changed. Not observed in game.</summary>
internal sealed class EarthFillTerrainTools : IDisposable
{
    private static readonly Collider[] Hits = new Collider[128];
    private static readonly List<GameObject> Found = new List<GameObject>();
    private static readonly HashSet<GameObject> Seen = new HashSet<GameObject>();
    private static int _mask;
    private static Action<string>? _log;
    private static bool _faultLogged;
    private Harmony? _harmony;

    internal bool Install(string harmonyId, Action<string> log)
    {
        try
        {
            MethodInfo? awake = AccessTools.Method(typeof(TerrainOp), "Awake");
            MethodInfo? damage = AccessTools.Method(typeof(Destructible), nameof(Destructible.Damage), new[] { typeof(HitData) });
            if (awake == null || damage == null)
            {
                log("Earth fill: the installed game's terrain tools did not match; the hoe and pickaxe do not clear earth blocks.");
                return false;
            }

            _log = log;
            _harmony = new Harmony(harmonyId);
            _harmony.Patch(awake, prefix: new HarmonyMethod(typeof(EarthFillTerrainTools), nameof(BeforeTerrainOp)));
            _harmony.Patch(damage, prefix: new HarmonyMethod(typeof(EarthFillTerrainTools), nameof(BeforeDamage)));
            return true;
        }
        catch (Exception exception)
        {
            log("Earth fill: terrain tools unavailable (" + SafeFailure.Brief(exception) + "); the hammer still removes earth blocks.");
            Dispose();
            return false;
        }
    }

    /// <summary>Clears the earth blocks a height-changing terrain operation reaches, before it applies.</summary>
    private static void BeforeTerrainOp(TerrainOp __instance)
    {
        try
        {
            if (TerrainOp.m_forceDisableTerrainOps || __instance == null) return;
            TerrainOp.Settings settings = __instance.m_settings;
            float radius = EarthFill.ClearRadius(Utils.GetPrefabName(__instance.gameObject), settings.m_level, settings.m_levelRadius,
                settings.m_raise, settings.m_raiseRadius, settings.m_smooth, settings.m_smoothRadius);
            if (!(radius > 0f)) return;
            if (_mask == 0) _mask = LayerMask.GetMask("piece");
            int count = Physics.OverlapSphereNonAlloc(__instance.transform.position, radius + EarthFill.ClearMargin, Hits, _mask, QueryTriggerInteraction.Ignore);
            Found.Clear();
            Seen.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider hit = Hits[i];
                Hits[i] = null!;
                ZNetView? view = hit != null ? hit.GetComponentInParent<ZNetView>() : null;
                if (view != null && view.IsValid() && EarthFillPlacement.IsFill(view.gameObject) && Seen.Add(view.gameObject)) Found.Add(view.gameObject);
            }

            if (Found.Count > 0) ExcavationClearing.RemoveFill(Found);
            Found.Clear();
            Seen.Clear();
        }
        catch (Exception exception)
        {
            if (_faultLogged) return;
            _faultLogged = true;
            _log?.Invoke("Earth fill: clearing blocks for a terrain tool failed (" + SafeFailure.Brief(exception) + "); the tool itself is unaffected.");
        }
    }

    /// <summary>Refuses the local player's hit on an earth block inside a ward that does not grant them access.</summary>
    private static bool BeforeDamage(Destructible __instance, HitData hit)
    {
        try
        {
            if (__instance == null || hit == null || !EarthFillPlacement.IsFill(__instance.gameObject)) return true;
            Player local = Player.m_localPlayer;
            if (local == null || !ReferenceEquals(hit.GetAttacker(), local)) return true;
            return PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: true, wardCheck: false);
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        try { _harmony?.UnpatchSelf(); } catch { /* Teardown only. */ }
        _harmony = null;
        _log = null;
        Found.Clear();
        Seen.Clear();
    }
}
