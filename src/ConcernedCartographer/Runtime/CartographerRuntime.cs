using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Map;
using TheConcernedCat.ConcernedCartographer.Persistence;
using TheConcernedCat.ConcernedCartographer.Roads;
using UnityEngine;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal sealed class CartographerRuntime : IDisposable
{
    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly RoadPersistence _persistence;
    private readonly GroundPaintProbe _probe;
    private readonly RoadOverlayRenderer _renderer;
    private readonly ConstructionCapture _constructionCapture;
    private readonly ChunkRecoveryScanner _chunkRecovery;
    private readonly RateLimitedLog _rateLimited;

    // Chunk recovery emits cells in row-major scan order, so only adjacent
    // cells (~1 m apart, diagonal ~1.4 m) may chain into one stroke; a wider
    // gap would draw connectors between separate parallel roads that happen
    // to share a scan row.
    private const float RecoveryMaxGapMeters = 2.5f;

    // Removing ink requires a full overlay rebuild (pixels cannot be
    // un-drawn incrementally); coalesce bursts of hoe swings into one
    // redraw at most this often.
    private const float RedrawDebounceSeconds = 0.5f;

    private bool _redrawPending;
    private float _redrawElapsed;

    private RoadAtlas _atlas = new();
    private RoadObservationPipeline? _pipeline;
    private RoadSurveyor? _surveyor;
    private long? _worldUid;
    private bool _mapReady;
    private float _autosaveElapsed;
    private bool _disposed;

    public CartographerRuntime(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _persistence = new RoadPersistence(log);
        _probe = new GroundPaintProbe(settings, log);
        _renderer = new RoadOverlayRenderer(settings, log);
        _rateLimited = new RateLimitedLog(log, 5f);
        _constructionCapture = new ConstructionCapture(log);
        _constructionCapture.OperationCaptured += HandleTerrainOperation;
        _chunkRecovery = new ChunkRecoveryScanner(settings, log);
        _chunkRecovery.PaintObserved += HandleRecoveredPaint;
    }

    public void OnMapAvailable()
    {
        if (_disposed)
        {
            return;
        }

        if (!WorldContext.TryGetWorldUid(out long uid))
        {
            _log.LogWarning("Map became available before a world UID could be resolved; waiting for the next map event.");
            _mapReady = false;
            return;
        }

        SwitchWorld(uid);
        _mapReady = true;
        _renderer.RedrawAll(_atlas);
        if (_settings.DrawCalibrationMarkers.Value)
        {
            _renderer.DrawCalibrationMarkers();
        }

        _log.LogInfo($"Road atlas ready for world {uid}: {_atlas.Strokes.Count} stroke(s), {_atlas.PointCount} point(s).");
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed || !_settings.Enabled.Value || !_mapReady || _surveyor is null)
        {
            return;
        }

        if (!WorldContext.TryGetWorldUid(out long uid) || _worldUid != uid)
        {
            // Logout or world switch: stop sampling and flush now instead of
            // waiting for the next map event, so no surveyed data is lost.
            _mapReady = false;
            _pipeline?.EndAllStrokes();
            _chunkRecovery.Reset();
            SaveIfDirty();
            return;
        }

        if (_surveyor.Tick(unscaledDeltaTime, out RoadSegment segment))
        {
            _renderer.DrawSegment(segment);
        }

        _chunkRecovery.Tick();

        _redrawElapsed += unscaledDeltaTime;
        if (_redrawPending && _redrawElapsed >= RedrawDebounceSeconds)
        {
            _redrawPending = false;
            _redrawElapsed = 0f;
            _renderer.RedrawAll(_atlas);
        }

        _autosaveElapsed += unscaledDeltaTime;
        if (_autosaveElapsed >= _settings.AutosaveIntervalSeconds.Value)
        {
            _autosaveElapsed = 0f;
            SaveIfDirty();
        }
    }

    private void HandleTerrainOperation(CapturedTerrainOperation operation)
    {
        if (_disposed || !_settings.Enabled.Value || !_mapReady || _pipeline is null || _worldUid is null)
        {
            return;
        }

        var center = new RoadPoint(operation.Position.x, operation.Position.y, operation.Position.z);

        if (_settings.ReconcileTerrainChanges.Value)
        {
            int removed = 0;
            if (operation.RoadKind is RoadKind paintedKind)
            {
                // A kind change: new paint of one kind erases covered ink of
                // the other. Same-kind ink stays put (suppression keeps it
                // from duplicating).
                RoadKind other = paintedKind == RoadKind.Dirt ? RoadKind.Paved : RoadKind.Dirt;
                removed = RemoveCoverageWithBackup(other, center, operation.RadiusMeters);
            }
            else
            {
                // Cultivate/Reset erase road-ness entirely.
                removed = RemoveCoverageWithBackup(RoadKind.Dirt, center, operation.RadiusMeters)
                    + RemoveCoverageWithBackup(RoadKind.Paved, center, operation.RadiusMeters);
            }

            if (removed > 0)
            {
                _redrawPending = true;
                _log.LogInfo(
                    $"Reconciled a terrain change at ({operation.Position.x:0.#}, {operation.Position.z:0.#}) " +
                    $"r={operation.RadiusMeters:0.#}m: removed {removed} road point(s).");
            }
        }

        if (operation.RoadKind is RoadKind kind && _settings.CaptureConstructionActions.Value)
        {
            var rules = new RoadSamplingRules(
                _settings.MinimumPointSpacingMeters.Value,
                _settings.MaximumStrokeGapMeters.Value,
                _settings.DuplicateSuppressionMeters.Value);
            ObserveAndDraw(RoadObservationSource.Construction, kind, operation.Position, rules, "construction-observed");
        }
    }

    private int RemoveCoverageWithBackup(RoadKind kind, RoadPoint center, float radiusMeters)
    {
        // Snapshot the last saved sidecar before this session's first
        // destructive change, so a reconciliation bug is recoverable.
        _persistence.BackupBeforeReconciliation(_worldUid!.Value);
        return _atlas.RemoveCoverage(kind, center, radiusMeters);
    }

    private void HandleRecoveredPaint(RoadKind kind, Vector3 position)
    {
        var rules = new RoadSamplingRules(
            _settings.MinimumPointSpacingMeters.Value,
            RecoveryMaxGapMeters,
            _settings.DuplicateSuppressionMeters.Value);
        ObserveAndDraw(RoadObservationSource.ChunkRecovery, kind, position, rules, "recovery-observed");
    }

    private void ObserveAndDraw(
        RoadObservationSource source,
        RoadKind kind,
        Vector3 position,
        RoadSamplingRules rules,
        string debugKey)
    {
        if (_disposed || !_settings.Enabled.Value || !_mapReady || _pipeline is null)
        {
            return;
        }

        var observation = new RoadObservation(
            source,
            kind,
            new RoadPoint(position.x, position.y, position.z));

        int pointsBefore = _atlas.PointCount;
        if (_pipeline.Observe(observation, rules, out RoadSegment segment))
        {
            _renderer.DrawSegment(segment);
        }
        else if (_atlas.PointCount > pointsBefore)
        {
            // A stroke start stores a point without producing a segment; a
            // lone dab must still appear on the map immediately.
            _renderer.DrawPoint(kind, observation.Position);
        }

        if (_settings.DebugLogging.Value)
        {
            _rateLimited.Info(debugKey, $"Observed {observation}.");
        }
    }

    public void SaveIfDirty()
    {
        if (_worldUid is null || !_atlas.IsDirty)
        {
            return;
        }

        if (_persistence.Save(_worldUid.Value, _atlas))
        {
            _atlas.MarkClean();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SaveIfDirty();
        _pipeline?.EndAllStrokes();
        _constructionCapture.OperationCaptured -= HandleTerrainOperation;
        _constructionCapture.Dispose();
        _chunkRecovery.PaintObserved -= HandleRecoveredPaint;
        _chunkRecovery.Reset();
        _disposed = true;
    }

    private void SwitchWorld(long uid)
    {
        if (_worldUid == uid && _surveyor is not null)
        {
            return;
        }

        SaveIfDirty();

        _worldUid = uid;
        _atlas = _persistence.Load(uid);
        _pipeline = new RoadObservationPipeline(_atlas);
        _surveyor = new RoadSurveyor(_settings, _probe, _pipeline, _log);
        _chunkRecovery.Reset();
        _redrawPending = false;
        _autosaveElapsed = 0f;
    }
}
