using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Map;
using TheConcernedCat.ConcernedCartographer.Persistence;
using TheConcernedCat.ConcernedCartographer.Roads;

namespace TheConcernedCat.ConcernedCartographer.Runtime;

internal sealed class CartographerRuntime : IDisposable
{
    private readonly CartographerSettings _settings;
    private readonly ManualLogSource _log;
    private readonly RoadPersistence _persistence;
    private readonly GroundPaintProbe _probe;
    private readonly RoadOverlayRenderer _renderer;

    private RoadAtlas _atlas = new();
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
            _mapReady = false;
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
        _surveyor?.EndStroke();
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
        _surveyor = new RoadSurveyor(_settings, _probe, _atlas, _log);
        _autosaveElapsed = 0f;
    }
}
