using System;
using BepInEx.Logging;
using TheConcernedCat.ConcernedCartographer.Atlas;
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
    private readonly PinPersistence _pinPersistence;
    private readonly GroundPaintProbe _probe;
    private readonly RoadOverlayRenderer _renderer;
    private readonly ConstructionCapture _constructionCapture;
    private readonly PinAdapter _pinAdapter;
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
    private RoadAtlasEditor? _editor;
    private RoadSurveyor? _surveyor;
    private PinStore _pinStore = new();
    private long? _worldUid;
    private bool _mapReady;
    private float _autosaveElapsed;
    private bool _disposed;

    public CartographerRuntime(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _persistence = new RoadPersistence(log);
        _pinPersistence = new PinPersistence(log);
        _probe = new GroundPaintProbe(settings, log);
        _renderer = new RoadOverlayRenderer(settings, log);
        _rateLimited = new RateLimitedLog(log, 5f);
        _pinAdapter = new PinAdapter(log);
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
        _pinAdapter.ReconcileOnMapReady(_pinStore);
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
            _pinAdapter.Reset();
            SaveIfDirty();
            SavePinsSnapshot();
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
            _pinAdapter.AbsorbVanillaChanges(_pinStore);
            _pinPersistence.FlushJournal();
        }
    }

    /// <summary>The current world's managed pins. Empty store before the
    /// first world loads.</summary>
    internal PinStore Pins => _pinStore;

    internal PinAdapter PinAdapter => _pinAdapter;

    /// <summary>Roads and pins together, for quit/teardown paths.</summary>
    public void SaveAll()
    {
        SaveIfDirty();
        SavePinsSnapshot();
    }

    private void SavePinsSnapshot()
    {
        if (_worldUid is long uid)
        {
            _pinPersistence.Save(uid, _pinStore);
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

        if (operation.RoadKind is RoadKind kind && !operation.IsTerraforming && _settings.CaptureConstructionActions.Value)
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

    /// <summary>Backs the `cc_roads` console command. Returns the message to
    /// print in the terminal; every mutation is journaled, saved, and
    /// scheduled for redraw.</summary>
    internal string ExecuteRoadCommand(string[] args)
    {
        if (_disposed || !_mapReady || _editor is null || _worldUid is null)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        Player player = Player.m_localPlayer;
        if (player is null)
        {
            return "Concerned Cartographer: no local player.";
        }

        UnityEngine.Vector3 playerPosition = player.transform.position;
        var position = new RoadPoint(playerPosition.x, playerPosition.y, playerPosition.z);
        string subcommand = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        float radius = RoadAtlasEditor.DefaultSelectRadiusMeters;
        if (args.Length > 1 &&
            float.TryParse(args[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsedRadius))
        {
            radius = UnityEngine.Mathf.Clamp(parsedRadius, 1f, 100f);
        }

        bool changed;
        string summary;
        switch (subcommand)
        {
            case "status":
                int hiddenCount = 0;
                foreach (RoadStroke stroke in _atlas.Strokes)
                {
                    if (stroke.Hidden)
                    {
                        hiddenCount++;
                    }
                }

                return $"Atlas: {_atlas.Strokes.Count} road(s), {_atlas.PointCount} point(s), " +
                    $"{hiddenCount} hidden, undo depth {_editor.UndoCount}. {_editor.DescribeNearest(position, radius)}";
            case "delete":
                changed = MutateWithBackup(() => _editor.DeleteNearest(position, radius, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "kind":
                changed = MutateWithBackup(() => _editor.ReclassifyNearest(position, radius, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "hide":
                changed = MutateWithBackup(() => _editor.SetHiddenNearest(position, radius, hidden: true, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "unhide":
                changed = MutateWithBackup(() => _editor.SetHiddenNearest(position, radius, hidden: false, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "split":
                changed = MutateWithBackup(() => _editor.SplitNearest(position, radius, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "join":
                changed = MutateWithBackup(() => _editor.JoinNearest(position, radius, out _lastToolSummary));
                summary = _lastToolSummary;
                break;
            case "rebuild":
                float rebuildRadius = args.Length > 1 ? radius : 32f;
                _persistence.BackupBeforeReconciliation(_worldUid.Value);
                int removed = _atlas.RemoveCoverage(RoadKind.Dirt, position, rebuildRadius)
                    + _atlas.RemoveCoverage(RoadKind.Paved, position, rebuildRadius);
                _chunkRecovery.Reset();
                changed = removed > 0;
                summary = $"Cleared {removed} road point(s) within {rebuildRadius:0.#} m; explored loaded terrain " +
                    "will be re-scanned with the current detection settings.";
                break;
            case "undo":
                changed = _editor.Undo(out summary);
                break;
            default:
                return "Usage: cc_roads [status|delete|kind|hide|unhide|split|join|rebuild|undo] [radius].";
        }

        if (changed)
        {
            _redrawPending = true;
            SaveIfDirty();
            _log.LogInfo($"Road tool '{subcommand}': {summary}");
        }

        return summary;
    }

    private string _lastToolSummary = "";

    private bool MutateWithBackup(Func<bool> operation)
    {
        // Snapshot the last saved sidecar once per session before the first
        // tool mutation, mirroring reconciliation's journal.
        _persistence.BackupBeforeReconciliation(_worldUid!.Value);
        return operation();
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
        SavePinsSnapshot();
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
        SavePinsSnapshot();

        _worldUid = uid;
        _atlas = _persistence.Load(uid);
        _pinStore = _pinPersistence.Load(uid);
        _pinStore.Changed += _pinPersistence.QueueJournal;
        _pinAdapter.Reset();
        _pipeline = new RoadObservationPipeline(_atlas);
        _editor = new RoadAtlasEditor(_atlas);
        _surveyor = new RoadSurveyor(_settings, _probe, _pipeline, _log);
        _chunkRecovery.Reset();
        _redrawPending = false;
        _autosaveElapsed = 0f;
    }
}
