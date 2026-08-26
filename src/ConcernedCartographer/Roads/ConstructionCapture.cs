using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Roads;

/// <summary>The construction observation source: a read-only Harmony postfix
/// on <c>TerrainComp.ApplyOperation(TerrainOp)</c>, which runs exactly on the
/// placing client after a terrain-paint placement succeeded (failed or
/// cancelled placements never spawn a TerrainOp). The actual terrain change
/// is applied by the chunk-owner client via an owner-routed RPC, so this
/// source records only the local player's own actions; other players' roads
/// are recovered by the chunk-recovery source. One op near a chunk seam
/// fires once per affected heightmap; the pipeline's replay idempotency
/// collapses those duplicates.</summary>
internal sealed class ConstructionCapture : IDisposable
{
    private static ConstructionCapture? s_active;

    private readonly ManualLogSource _log;
    private Harmony? _harmony;
    private bool _disabledForSession;

    public ConstructionCapture(ManualLogSource log)
    {
        _log = log;

        try
        {
            _harmony = new Harmony(Plugin.PluginGuid + ".construction");
            _harmony.Patch(
                AccessTools.Method(typeof(TerrainComp), nameof(TerrainComp.ApplyOperation)),
                postfix: new HarmonyMethod(typeof(ConstructionCapture), nameof(AfterApplyOperation)));
            s_active = this;
        }
        catch (Exception exception)
        {
            _harmony = null;
            _log.LogError($"Construction capture could not be installed and is disabled for this session: {exception}");
        }
    }

    /// <summary>Raised on the placing client for every successful
    /// terrain-paint operation: Dirt/Paved ops carry their road kind,
    /// Cultivate/Reset ops carry none and act as road-removal signals.</summary>
    public event Action<CapturedTerrainOperation>? OperationCaptured;

    private static void AfterApplyOperation(TerrainOp modifier)
    {
        ConstructionCapture? active = s_active;
        if (active is null || active._disabledForSession)
        {
            return;
        }

        try
        {
            TerrainOp.Settings? settings = modifier == null ? null : modifier.m_settings;
            if (settings is null || !settings.m_paintCleared)
            {
                return;
            }

            RoadKind? kind;
            switch (settings.m_paintType)
            {
                case TerrainModifier.PaintType.Dirt:
                    kind = RoadKind.Dirt;
                    break;
                case TerrainModifier.PaintType.Paved:
                    kind = RoadKind.Paved;
                    break;
                case TerrainModifier.PaintType.Cultivate:
                case TerrainModifier.PaintType.Reset:
                    // These erase road paint; reconciliation removes the
                    // covered ink.
                    kind = null;
                    break;
                default:
                    return;
            }

            active.OperationCaptured?.Invoke(new CapturedTerrainOperation(
                kind,
                modifier!.transform.position,
                settings.m_paintRadius));
        }
        catch (Exception exception)
        {
            // Fail closed: never let a capture bug interfere with terrain
            // placement, and never spam the log from a hot path.
            active._disabledForSession = true;
            active._log.LogError($"Construction capture failed and is disabled for this session: {exception}");
        }
    }

    public void Dispose()
    {
        if (ReferenceEquals(s_active, this))
        {
            s_active = null;
        }

        try
        {
            _harmony?.UnpatchSelf();
        }
        catch
        {
            // Unpatching only fails during teardown races; the postfix is
            // inert anyway once s_active is cleared.
        }

        _harmony = null;
    }
}
