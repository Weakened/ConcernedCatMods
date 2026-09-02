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
/// never reach it. One op near a chunk seam fires once per affected
/// heightmap; the pipeline's replay idempotency collapses those duplicates.
///
/// RC10 (DEF-v1.0-007): the op is classified by ACTUAL ACTION IDENTITY —
/// the placed prefab's name (the TerrainOp component lives on the
/// instantiated hoe/cultivator piece, e.g. "path_v2(Clone)"), its Piece
/// name token, and the local player's selected build piece — via
/// <see cref="TerrainActionClassifier"/>. TerrainOp.Settings flags are
/// read ONLY for paint (corroboration) and radius, never for authority:
/// the game ships "Level ground" (mud_road_v2) and "Pathen" (path_v2)
/// with near-identical smooth-and-paint-Dirt settings.</summary>
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
    /// paint-clearing terrain operation: identity-authorized Pathen/Paved
    /// ops carry their road kind, everything else carries none and acts as
    /// a road-removal signal.</summary>
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

            TerrainActionClassification classification = TerrainActionClassifier.Classify(
                modifier!.gameObject.name,
                ReadPieceNameToken(modifier),
                ReadSelectedPieceName(),
                settings.m_paintCleared,
                MapPaint(settings.m_paintType));

            active.OperationCaptured?.Invoke(new CapturedTerrainOperation(
                classification.RoadKind,
                modifier.transform.position,
                settings.m_paintRadius,
                classification.Category,
                classification.Description));
        }
        catch (Exception exception)
        {
            // Fail closed: never let a capture bug interfere with terrain
            // placement, and never spam the log from a hot path.
            active._disabledForSession = true;
            active._log.LogError($"Construction capture failed and is disabled for this session: {exception}");
        }
    }

    private static TerrainPaintKind MapPaint(TerrainModifier.PaintType paintType)
    {
        switch (paintType)
        {
            case TerrainModifier.PaintType.Dirt:
                return TerrainPaintKind.Dirt;
            case TerrainModifier.PaintType.Cultivate:
                return TerrainPaintKind.Cultivate;
            case TerrainModifier.PaintType.Paved:
                return TerrainPaintKind.Paved;
            case TerrainModifier.PaintType.Reset:
                return TerrainPaintKind.Reset;
            default:
                return TerrainPaintKind.Other;
        }
    }

    private static string? ReadPieceNameToken(TerrainOp modifier)
    {
        try
        {
            Piece piece = modifier.GetComponent<Piece>();
            return piece == null ? null : piece.m_name;
        }
        catch
        {
            // Identity fallback only; the prefab name still classifies.
            return null;
        }
    }

    /// <summary>The prefab name of the piece the local player has selected.
    /// A genuine hoe placement runs synchronously inside Player.PlacePiece,
    /// so during this postfix the selection IS the placed piece. Null when
    /// unavailable — the classifier then relies on the op identity alone.</summary>
    private static string? ReadSelectedPieceName()
    {
        try
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return null;
            }

            player.GetBuildSelection(out Piece piece, out _, out _, out _, out _);
            return piece == null ? null : piece.gameObject.name;
        }
        catch
        {
            return null;
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
