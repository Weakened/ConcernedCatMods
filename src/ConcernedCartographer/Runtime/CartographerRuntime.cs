using System;
using System.Collections.Generic;
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
    private PinCommandHandler? _pinCommands;
    private readonly PinWorkbenchPanel _workbenchPanel;
    private readonly PinDisplayController _displayController;
    private readonly AtlasDrawerPanel _drawerPanel;
    private readonly SavedViewPersistence _savedViewPersistence;
    private SavedViewStore _savedViews = new();
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
        _workbenchPanel = new PinWorkbenchPanel(log);
        _displayController = new PinDisplayController(log);
        _savedViewPersistence = new SavedViewPersistence(log);
        _savedViews = _savedViewPersistence.Load();
        _drawerPanel = new AtlasDrawerPanel(log);
        WireDrawer();
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
        _renderer.SetOverlayEnabled(RoadKind.Dirt, _settings.DrawerShowDirt.Value);
        _renderer.SetOverlayEnabled(RoadKind.Paved, _settings.DrawerShowPaved.Value);
        _displayController.ShowPins = _settings.DrawerShowPins.Value;
        _displayController.ClusterEnabled = _settings.DrawerCluster.Value;
        _displayController.Apply(_pinStore, _pinAdapter);
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

        _workbenchPanel.HandleFrame();
        _drawerPanel.HandleFrame();
        if (Minimap.IsOpen() && !Minimap.InTextInput())
        {
            if (_pinCommands is not null && !_workbenchPanel.IsVisible &&
                Input.GetKeyDown(_settings.WorkbenchHotkey.Value))
            {
                OpenWorkbenchAtCursor();
            }

            if (Input.GetKeyDown(_settings.DrawerHotkey.Value))
            {
                _drawerPanel.Toggle(
                    _settings.DrawerShowDirt.Value,
                    _settings.DrawerShowPaved.Value,
                    _settings.DrawerShowPins.Value,
                    _settings.DrawerCluster.Value);
            }

            if (_displayController.ZoomTierChanged())
            {
                _displayController.Apply(_pinStore, _pinAdapter);
            }
        }

        if (!WorldContext.TryGetWorldUid(out long uid) || _worldUid != uid)
        {
            // Logout or world switch: stop sampling and flush now instead of
            // waiting for the next map event, so no surveyed data is lost.
            _mapReady = false;
            _pipeline?.EndAllStrokes();
            _chunkRecovery.Reset();
            _displayController.Reset();
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

    internal PinCommandHandler? PinCommands => _pinCommands;

    /// <summary>Backs the `cc_pins` console command.</summary>
    internal string ExecutePinCommand(string[] args)
    {
        if (_disposed || !_mapReady || _pinCommands is null)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        Player player = Player.m_localPlayer;
        if (player is null)
        {
            return "Concerned Cartographer: no local player.";
        }

        if (args.Length > 0 && string.Equals(args[0], "edit", StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkbenchNear(player.transform.position);
            return "Opening the Pin Workbench for the nearest pin.";
        }

        return _pinCommands.Execute(args, player.transform.position);
    }

    private void WireDrawer()
    {
        _drawerPanel.DirtToggled = value =>
        {
            _settings.DrawerShowDirt.Value = value;
            _renderer.SetOverlayEnabled(RoadKind.Dirt, value);
        };
        _drawerPanel.PavedToggled = value =>
        {
            _settings.DrawerShowPaved.Value = value;
            _renderer.SetOverlayEnabled(RoadKind.Paved, value);
        };
        _drawerPanel.PinsToggled = value =>
        {
            _settings.DrawerShowPins.Value = value;
            _displayController.ShowPins = value;
            ReapplyDisplay();
        };
        _drawerPanel.ClusterToggled = value =>
        {
            _settings.DrawerCluster.Value = value;
            _displayController.ClusterEnabled = value;
            ReapplyDisplay();
        };
        _drawerPanel.QueryApplied = query =>
        {
            _displayController.SetQuery(query);
            ReapplyDisplay();
        };
        _drawerPanel.ViewSaved = name =>
        {
            _savedViews.Save(new SavedView(
                name,
                _displayController.QueryText,
                _settings.DrawerShowDirt.Value,
                _settings.DrawerShowPaved.Value,
                _displayController.ShowPins,
                _displayController.ClusterEnabled));
            _savedViewPersistence.Save(_savedViews);
        };
        _drawerPanel.ViewApplied = name => ApplySavedView(name);
        _drawerPanel.ResultClicked = id =>
        {
            if (_pinCommands is not null && _pinStore.TryGet(id, out AtlasPin pin))
            {
                _workbenchPanel.OpenForManaged(pin, _pinCommands.Operations, ResyncPins);
            }
        };
        _drawerPanel.StatusLine = () =>
            $"{_displayController.VisibleCount} shown · {_displayController.HiddenByFilter} filtered · {_displayController.ClusterCount} clusters";
        _drawerPanel.TopResults = () =>
        {
            var results = new List<(string, AtlasId)>();
            PinQuery query = PinQuery.Parse(_displayController.QueryText);
            foreach (AtlasPin pin in _pinStore.Living)
            {
                if (pin.Archived || !query.Matches(pin))
                {
                    continue;
                }

                results.Add((pin.Name.Length == 0 ? "(unnamed)" : pin.Name, pin.Id));
                if (results.Count >= 6)
                {
                    break;
                }
            }

            return results;
        };
        _drawerPanel.ViewNames = () =>
        {
            var names = new List<string>();
            foreach (SavedView view in _savedViews.Views)
            {
                names.Add(view.Name);
                if (names.Count >= 5)
                {
                    break;
                }
            }

            return names;
        };
    }

    private bool ApplySavedView(string name)
    {
        if (!_savedViews.TryGet(name, out SavedView view))
        {
            return false;
        }

        _settings.DrawerShowDirt.Value = view.ShowDirt;
        _settings.DrawerShowPaved.Value = view.ShowPaved;
        _settings.DrawerShowPins.Value = view.ShowPins;
        _settings.DrawerCluster.Value = view.ClusterEnabled;
        _renderer.SetOverlayEnabled(RoadKind.Dirt, view.ShowDirt);
        _renderer.SetOverlayEnabled(RoadKind.Paved, view.ShowPaved);
        _displayController.ShowPins = view.ShowPins;
        _displayController.ClusterEnabled = view.ClusterEnabled;
        _displayController.SetQuery(view.Query);
        ReapplyDisplay();
        return true;
    }

    private void ReapplyDisplay()
    {
        if (_mapReady)
        {
            _displayController.Apply(_pinStore, _pinAdapter);
        }
    }

    private void ResyncPins()
    {
        _pinAdapter.ReconcileOnMapReady(_pinStore);
        ReapplyDisplay();
    }

    private void OpenWorkbenchAtCursor()
    {
        if (!MinimapReflection.TryScreenToWorldPoint(Input.mousePosition, out Vector3 world))
        {
            Player player = Player.m_localPlayer;
            if (player is null)
            {
                return;
            }

            world = player.transform.position;
        }

        OpenWorkbenchNear(world);
    }

    private void OpenWorkbenchNear(Vector3 world)
    {
        if (_pinCommands is null)
        {
            return;
        }

        var point = new RoadPoint(world.x, world.y, world.z);
        PinOperations operations = _pinCommands.Operations;
        Action resync = () => _pinAdapter.ReconcileOnMapReady(_pinStore);

        AtlasPin? nearestManaged = null;
        float best = 30f;
        foreach (AtlasPin pin in _pinStore.Living)
        {
            float distance = pin.Position.HorizontalDistanceTo(point);
            if (distance < best)
            {
                best = distance;
                nearestManaged = pin;
            }
        }

        if (nearestManaged is not null)
        {
            _workbenchPanel.OpenForManaged(nearestManaged, operations, resync);
            return;
        }

        if (_pinAdapter.TryFindNearest(world, 30f, out Minimap.PinData mapPin, out _))
        {
            if (_pinAdapter.IsAdoptableVanilla(mapPin))
            {
                Minimap.PinData captured = mapPin;
                _workbenchPanel.OpenAdoptPrompt(
                    captured.m_name ?? "",
                    () => _pinAdapter.Adopt(_pinStore, captured),
                    operations,
                    resync);
            }
            else
            {
                _workbenchPanel.OpenReadOnly($"\"{mapPin.m_name}\" ({mapPin.m_type})");
            }
        }
    }

    /// <summary>Roads, pins, and views together, for quit/teardown paths.</summary>
    public void SaveAll()
    {
        SaveIfDirty();
        SavePinsSnapshot();
        _savedViewPersistence.Save(_savedViews);
    }

    /// <summary>Backs the `cc_atlas` console command: the scriptable drawer.</summary>
    internal string ExecuteAtlasCommand(string[] args)
    {
        if (_disposed || !_mapReady)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        string subcommand = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        string remainder = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1) : "";

        switch (subcommand)
        {
            case "status":
                return $"Atlas view: query \"{_displayController.QueryText}\", pins {(_displayController.ShowPins ? "on" : "off")}, " +
                    $"cluster {(_displayController.ClusterEnabled ? "on" : "off")}, dirt {(_settings.DrawerShowDirt.Value ? "on" : "off")}, " +
                    $"paved {(_settings.DrawerShowPaved.Value ? "on" : "off")}. " +
                    $"{_displayController.VisibleCount} shown, {_displayController.HiddenByFilter} filtered, {_displayController.ClusterCount} clusters.";
            case "query":
                _displayController.SetQuery(remainder);
                ReapplyDisplay();
                return $"Filter applied: \"{remainder}\" — {_displayController.VisibleCount} shown, {_displayController.HiddenByFilter} filtered. Filters are display-only; 'cc_atlas clear' restores everything.";
            case "clear":
                _displayController.SetQuery("");
                ReapplyDisplay();
                return "Filter cleared; all pins shown.";
            case "pins":
            case "cluster":
            case "dirt":
            case "paved":
                if (!TryParseOnOff(remainder, out bool enabled))
                {
                    return $"Usage: cc_atlas {subcommand} on|off";
                }

                ApplyToggle(subcommand, enabled);
                return $"{subcommand} {(enabled ? "on" : "off")}.";
            case "view":
                return HandleViewCommand(remainder);
            case "views":
                var names = new System.Text.StringBuilder("Saved views:");
                if (_savedViews.Views.Count == 0)
                {
                    return "Saved views: none. 'cc_atlas view save <name>' captures the current filter/layer state.";
                }

                foreach (SavedView view in _savedViews.Views)
                {
                    names.Append($"\n  \"{view.Name}\" — query \"{view.Query}\"");
                }

                return names.ToString();
            default:
                return "Usage: cc_atlas [status|query <text>|clear|pins on/off|cluster on/off|dirt on/off|paved on/off|view save/apply/del <name>|views]";
        }
    }

    private string HandleViewCommand(string remainder)
    {
        int space = remainder.IndexOf(' ');
        string action = (space < 0 ? remainder : remainder.Substring(0, space)).ToLowerInvariant();
        string name = space < 0 ? "" : remainder.Substring(space + 1).Trim();
        if (name.Length == 0)
        {
            return "Usage: cc_atlas view save|apply|del <name>";
        }

        switch (action)
        {
            case "save":
                _drawerPanel.ViewSaved?.Invoke(name);
                return $"View \"{name}\" saved with the current filter and layer state.";
            case "apply":
                return ApplySavedView(name)
                    ? $"View \"{name}\" applied."
                    : $"No view named \"{name}\".";
            case "del":
                bool removed = _savedViews.Remove(name);
                _savedViewPersistence.Save(_savedViews);
                return removed ? $"View \"{name}\" deleted." : $"No view named \"{name}\".";
            default:
                return "Usage: cc_atlas view save|apply|del <name>";
        }
    }

    private void ApplyToggle(string which, bool enabled)
    {
        switch (which)
        {
            case "pins":
                _settings.DrawerShowPins.Value = enabled;
                _displayController.ShowPins = enabled;
                ReapplyDisplay();
                break;
            case "cluster":
                _settings.DrawerCluster.Value = enabled;
                _displayController.ClusterEnabled = enabled;
                ReapplyDisplay();
                break;
            case "dirt":
                _settings.DrawerShowDirt.Value = enabled;
                _renderer.SetOverlayEnabled(RoadKind.Dirt, enabled);
                break;
            case "paved":
                _settings.DrawerShowPaved.Value = enabled;
                _renderer.SetOverlayEnabled(RoadKind.Paved, enabled);
                break;
        }
    }

    private static bool TryParseOnOff(string text, out bool enabled)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "on":
            case "true":
            case "1":
                enabled = true;
                return true;
            case "off":
            case "false":
            case "0":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
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
        _pinCommands = new PinCommandHandler(
            _pinStore,
            new PinOperations(_pinStore),
            _pinAdapter,
            _log,
            ResyncPins);
        _displayController.Reset();
        _pipeline = new RoadObservationPipeline(_atlas);
        _editor = new RoadAtlasEditor(_atlas);
        _surveyor = new RoadSurveyor(_settings, _probe, _pipeline, _log);
        _chunkRecovery.Reset();
        _redrawPending = false;
        _autosaveElapsed = 0f;
    }
}
