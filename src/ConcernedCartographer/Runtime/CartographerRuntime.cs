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
    private readonly RateLimitedLog _rateLimited;

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
        _constructionCapture.PaintObserved += HandleConstructionPaint;
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
            SaveIfDirty();
            return;
        }

        if (_surveyor.Tick(unscaledDeltaTime, out RoadSegment segment))
        {
            _renderer.DrawSegment(segment);
        }

        _autosaveElapsed += unscaledDeltaTime;
        if (_autosaveElapsed >= _settings.AutosaveIntervalSeconds.Value)
        {
            _autosaveElapsed = 0f;
            SaveIfDirty();
        }
    }

    private void HandleConstructionPaint(RoadKind kind, Vector3 position)
    {
        if (_disposed ||
            !_settings.Enabled.Value ||
            !_settings.CaptureConstructionActions.Value ||
            !_mapReady ||
            _pipeline is null)
        {
            return;
        }

        var rules = new RoadSamplingRules(
            _settings.MinimumPointSpacingMeters.Value,
            _settings.MaximumStrokeGapMeters.Value,
            _settings.DuplicateSuppressionMeters.Value);
        var observation = new RoadObservation(
            RoadObservationSource.Construction,
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
            _rateLimited.Info("construction-observed", $"Observed {observation}.");
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
        _constructionCapture.PaintObserved -= HandleConstructionPaint;
        _constructionCapture.Dispose();
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
        _autosaveElapsed = 0f;
    }
}
