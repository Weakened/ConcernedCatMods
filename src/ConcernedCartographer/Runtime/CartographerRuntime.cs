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
    private readonly RateLimitedLog _rateLimited;

    // Removing ink requires a full overlay rebuild (pixels cannot be
    // un-drawn incrementally); coalesce bursts of hoe swings into one
    // redraw at most this often.
    private const float RedrawDebounceSeconds = 0.5f;

    private bool _redrawPending;
    private float _redrawElapsed;

    private RoadAtlas _atlas = new();
    private TerrainIntentMask _terrainIntent = new();
    private readonly TerrainIntentPersistence _terrainIntentPersistence;
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
    private readonly MapUiCoordinator _mapUi;
    private readonly CrashConsentPanel _consentPanel;
    private bool _consentPromptChecked;
    private readonly PinPalettePanel _palettePanel;
    private readonly RoutesPanel _routesPanel;
    private readonly SurveyPanel _surveyPanel;
    private readonly SharePanel _sharePanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly SystemMarkersPanel _systemMarkersPanel;
    private int _drawerToken;
    private int _paletteToken;
    private int _routesToken;
    private int _surveyToken;
    private int _shareToken;
    private int _settingsToken;
    private int _systemMarkersToken;
    private int _workbenchToken;
    private bool _quickPinArmed;
    private readonly PaletteBirthTracker<Minimap.PinData> _birthTracker = new();
    private float _hintElapsed;

    // The context button sits away from the hovered pin, so the action
    // stays alive briefly (and while the pointer is over the button) to
    // survive the mouse travel from pin to button.
    private const float ContextGraceSeconds = 1.5f;
    private float _contextGrace;
    private readonly QuickPinCapture _quickPinCapture;
    private readonly SurveyEngine _surveyEngine = new();
    private readonly SurveyScanner _surveyScanner;
    private readonly SurveyRulePersistence _surveyRulePersistence;
    private readonly RoutePersistence _routePersistence;
    private readonly RouteOverlayRenderer _routeRenderer;
    private RouteStore _routeStore = new();
    private RouteCommandHandler? _routeCommands;
    private bool _routeRedrawPending;
    private float _routeRedrawElapsed;
    private readonly SyncInbox _syncInbox = new();
    private readonly SyncTransport _syncTransport;
    private string _authorId = "";
    private readonly CompatibilityRegistry _compatibility = new();
    private readonly AtlasBackupTools _backupTools;
    private long? _worldUid;
    private bool _mapReady;
    private float _autosaveElapsed;
    private bool _disposed;

    public CartographerRuntime(CartographerSettings settings, ManualLogSource log)
    {
        _settings = settings;
        _log = log;
        _persistence = new RoadPersistence(log);
        _terrainIntentPersistence = new TerrainIntentPersistence(log);
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
        _mapUi = new MapUiCoordinator(log);
        _consentPanel = new CrashConsentPanel(log, settings);
        _palettePanel = new PinPalettePanel(log);
        _routesPanel = new RoutesPanel(log, () => _routeCommands);
        _surveyPanel = new SurveyPanel(log, settings, ExecuteSurveyCommand, () => _surveyEngine.Observations);
        _sharePanel = new SharePanel(log, ExecuteSyncCommand, () =>
        {
            var authors = new List<string>();
            foreach (SyncInbox.Envelope envelope in _syncInbox.Envelopes)
            {
                authors.Add(envelope.AuthorName);
            }

            return authors;
        });
        _settingsPanel = new SettingsPanel(log, ExecuteAtlasCommand, ExecuteRoadCommand, () => _consentPanel.ShowSettings());
        _systemMarkersPanel = new SystemMarkersPanel(log);

        // One major side surface at a time (#100).
        _drawerToken = _mapUi.RegisterSurface(() => _drawerPanel.IsVisible, _drawerPanel.Hide);
        _paletteToken = _mapUi.RegisterSurface(() => _palettePanel.IsVisible, _palettePanel.Hide);
        _routesToken = _mapUi.RegisterSurface(() => _routesPanel.IsVisible, _routesPanel.Hide);
        _surveyToken = _mapUi.RegisterSurface(() => _surveyPanel.IsVisible, _surveyPanel.Hide);
        _shareToken = _mapUi.RegisterSurface(() => _sharePanel.IsVisible, _sharePanel.Hide);
        _settingsToken = _mapUi.RegisterSurface(() => _settingsPanel.IsVisible, _settingsPanel.Hide);
        _systemMarkersToken = _mapUi.RegisterSurface(() => _systemMarkersPanel.IsVisible, _systemMarkersPanel.Hide);
        _workbenchToken = _mapUi.RegisterSurface(() => _workbenchPanel.IsVisible, _workbenchPanel.Close);

        _mapUi.AtlasClicked = () => _mapUi.OpenExclusive(_drawerToken, ToggleDrawer);
        _mapUi.MarkersClicked = () =>
        {
            if (PaletteActive())
            {
                _palettePanel.UiScale = _settings.UiScale.Value;
                _palettePanel.EnsureBuilt();
                _mapUi.OpenExclusive(_paletteToken, _palettePanel.Toggle);
            }
            else
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft,
                    "The enhanced marker palette is disabled (setting or a conflicting pin manager); the vanilla selector is shown instead.");
            }
        };
        _mapUi.RoutesClicked = () => OpenSidePanel(_routesToken, _routesPanel);
        _mapUi.SurveyClicked = () => OpenSidePanel(_surveyToken, _surveyPanel);
        _mapUi.ShareClicked = () => OpenSidePanel(_shareToken, _sharePanel);
        _mapUi.SettingsClicked = () => OpenSidePanel(_settingsToken, _settingsPanel);
        _mapUi.QuickPinClicked = ArmQuickPin;
        _drawerPanel.PrivacyClicked = () => _consentPanel.ShowSettings();
        _drawerPanel.SystemMarkersClicked = () => OpenSidePanel(_systemMarkersToken, _systemMarkersPanel);
        MapInputGate.Install(log);
        _palettePanel.IconChosen = definition =>
        {
            // Vanilla placement does the rest: double-click creates the
            // rendering with this type and opens the name input; the birth
            // tracker claims the newborn when naming closes (#96).
            MinimapReflection.TrySelectIcon(definition.VanillaType);
            _birthTracker.Arm(definition.Id, definition.DefaultCategory);
        };
        _palettePanel.SelectionCleared = () => _birthTracker.Disarm();
        _quickPinCapture = new QuickPinCapture(settings, log);
        _surveyRulePersistence = new SurveyRulePersistence(log);
        _surveyEngine.Rules = _surveyRulePersistence.LoadOrCreate();
        _surveyScanner = new SurveyScanner(settings, log);
        _routePersistence = new RoutePersistence(log);
        _routeRenderer = new RouteOverlayRenderer(settings, log);
        _authorId = AuthorIdentity.Get(log);
        _syncTransport = new SyncTransport(log, _syncInbox) { LocalAuthorId = _authorId };
        _backupTools = new AtlasBackupTools(log);
        _constructionCapture = new ConstructionCapture(log);
        _constructionCapture.OperationCaptured += HandleTerrainOperation;

        // RC8 road source authority: the chunk-recovery scanner is not
        // constructed at all — passive road creation is disabled in v1.
        // Roads come exclusively from the player's own Pathen/Paved ops.
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
        _routeRenderer.RedrawAll(_routeStore);
        _syncTransport.EnsureRegistered();
        _compatibility.Evaluate(_log);
        ShowOnboardingOnce();
        if (_settings.DrawCalibrationMarkers.Value)
        {
            _renderer.DrawCalibrationMarkers();
        }

        _log.LogInfo($"Road atlas ready for world {uid}: {_atlas.Strokes.Count} stroke(s), {_atlas.PointCount} point(s).");
    }

    public void Tick(float unscaledDeltaTime)
    {
        if (_disposed)
        {
            return;
        }

        // The workbench frame handler runs before every other gate: it owns
        // the fail-safe that a hidden or orphaned panel can never keep
        // holding the global input block (DEF-v1.0-001), which must hold
        // even when the mod is disabled mid-session or the world tears down.
        _workbenchPanel.HandleFrame();
        _consentPanel.HandleFrame();
        _routesPanel.HandleFrame();
        _surveyPanel.HandleFrame();
        _sharePanel.HandleFrame();
        _settingsPanel.HandleFrame();
        _systemMarkersPanel.HandleFrame();
        _palettePanel.HandleFrame();
        if (!Minimap.IsOpen())
        {
            MapInputGate.ConsumeClicks = false;
        }

        if (!_settings.Enabled.Value || !_mapReady || _surveyor is null)
        {
            // Disabled mid-session: the vanilla controls must come back
            // even though the rest of the runtime is dormant, and no CC
            // surface may linger un-closeable (its Escape handling is
            // gated behind this branch). The road texture overlays return
            // to the player's own layer choice too (RC8-1) — a disabled
            // mod may never strand the map with suppressed ink.
            MapInputGate.ConsumeClicks = false;
            if (!_settings.Enabled.Value)
            {
                _renderer.EnsureTextureFallback();
            }

            if (Minimap.IsOpen())
            {
                EnforceVanillaPaletteVisibility();
                _palettePanel.SetUnavailable();
            }

            if (_mapUi.AnySurfaceVisible)
            {
                _mapUi.CloseAllSurfaces();
            }

            return;
        }

        _drawerPanel.HandleFrame();
        if (!Minimap.IsOpen() && !Minimap.InTextInput())
        {
            if (_quickPinArmed)
            {
                // One-shot armed capture from the toolbar (#102).
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _quickPinArmed = false;
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, AtlasStrings.Get("quickpin.cancelled"));
                }
                else if (Input.GetMouseButtonDown(0) ||
                    (_settings.QuickPinHotkey.Value != KeyCode.None && Input.GetKeyDown(_settings.QuickPinHotkey.Value)) ||
                    GamepadDown(_settings.WorkbenchGamepadButton.Value))
                {
                    _quickPinArmed = false;
                    CaptureQuickPin();
                }
            }
            else if (_settings.QuickPinHotkey.Value != KeyCode.None &&
                Input.GetKeyDown(_settings.QuickPinHotkey.Value))
            {
                CaptureQuickPin();
            }
        }

        if (Minimap.IsOpen() && !Minimap.InTextInput())
        {
            // The CC map surface (#96/#100): toolbar, contextual pin
            // actions, the enhanced pin palette, and the edit hint — on
            // top of (never instead of) the rebindable hotkeys.
            _mapUi.EnsureBuilt(_settings.DrawerHotkey.Value.ToString());

            // One-time crash-reporting consent (#97): offered on the first
            // large-map open only, never on the title screen, never again
            // once answered (unless the policy version materially bumps).
            if (!_consentPromptChecked)
            {
                _consentPromptChecked = true;
                if (_consentPanel.NeedsFirstRunPrompt)
                {
                    _consentPanel.ShowFirstRun();
                }
            }

            if (PaletteActive())
            {
                _palettePanel.UiScale = _settings.UiScale.Value;
                _palettePanel.EnsureBuilt();
            }
            else
            {
                _palettePanel.SetUnavailable();
            }

            UpdateEditHint(unscaledDeltaTime);

            if (_pinCommands is not null && !_workbenchPanel.IsVisible &&
                (Input.GetKeyDown(_settings.WorkbenchHotkey.Value) ||
                 GamepadDown(_settings.WorkbenchGamepadButton.Value)))
            {
                OpenWorkbenchAtCursor();
            }

            if (Input.GetKeyDown(_settings.DrawerHotkey.Value) ||
                GamepadDown(_settings.DrawerGamepadButton.Value))
            {
                _mapUi.OpenExclusive(_drawerToken, ToggleDrawer);
            }

            if (_displayController.ZoomTierChanged())
            {
                _displayController.Apply(_pinStore, _pinAdapter);
            }

            // Route map modes (#101): a mode entered through the Routes
            // panel works with plain mouse input and consumes vanilla map
            // drag/clicks for its duration; the console path keeps the
            // classic modifier+LMB behavior. Vanilla input returns the
            // instant no UI-owned mode is active.
            bool uiRouteMode = _routeCommands is not null &&
                _routeCommands.Mode != RouteCommandHandler.MapMode.None &&
                _routeCommands.UiModeOwned;
            MapInputGate.ConsumeClicks = uiRouteMode;
            if (uiRouteMode)
            {
                MinimapReflection.TrySuppressMapDragThisFrame();
            }

            if (_routeCommands is not null && _routeCommands.Mode != RouteCommandHandler.MapMode.None &&
                (uiRouteMode || Input.GetKey(_settings.RouteDrawModifier.Value)) &&
                MinimapReflection.TryScreenToWorldPoint(Input.mousePosition, out Vector3 cursorWorld))
            {
                _routeCommands.HandleMapFrame(
                    new RoadPoint(cursorWorld.x, cursorWorld.y, cursorWorld.z),
                    Input.GetMouseButton(0),
                    Input.GetMouseButtonDown(0));
            }
        }

        _routeRedrawElapsed += unscaledDeltaTime;
        if (_routeRedrawPending && _routeRedrawElapsed >= RedrawDebounceSeconds)
        {
            _routeRedrawElapsed = 0f;
            _routeRedrawPending = false;
            _routeRenderer.RedrawAll(_routeStore);
        }

        if (!WorldContext.TryGetWorldUid(out long uid) || _worldUid != uid)
        {
            // Logout or world switch: stop sampling and flush now instead of
            // waiting for the next map event, so no surveyed data is lost.
            // The workbench must close here too — it can never carry an
            // input block across a world boundary.
            _mapReady = false;
            _workbenchPanel.Close();
            _mapUi.CloseAllSurfaces();
            MapInputGate.ConsumeClicks = false;
            _quickPinArmed = false;
            _pipeline?.EndAllStrokes();
            _displayController.Reset();
            _mapUi.Reset();
            _palettePanel.Reset();
            _birthTracker.Reset();
            _pinAdapter.Reset();
            SaveIfDirty();
            SavePinsSnapshot();
            return;
        }

        // Diagnostics-only traversal sampling (RC8): feeds `cc_roads align
        // live`, never the atlas. Road data is created only by construction.
        _surveyor.Tick(unscaledDeltaTime);

        // DEF-v1.0-006: the sub-texel large-map road layer follows pan/zoom
        // every frame and rebakes only on data/zoom-step changes.
        _renderer.TickVectorLayer(unscaledDeltaTime, _atlas);

        _surveyScanner.Tick(unscaledDeltaTime, _surveyEngine, _pinStore);

        // Managed-from-birth (#96): claim a palette-placed pin the moment
        // its vanilla naming flow closes. Runs every tick (not only while
        // the map is open) so a naming flow that outlives the map close
        // still resolves.
        if (_settings.EnhancedPinPalette.Value &&
            MinimapReflection.TryGetNamePin(out Minimap.PinData? namingPin))
        {
            Minimap.PinData? born = _birthTracker.Observe(namingPin);
            if (born is not null)
            {
                HandlePaletteBirth(born);
            }
        }

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
            _routePersistence.FlushJournal();
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
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

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
        // Search results carry the stable AtlasId, so selection opens the
        // workbench for that exact pin — never proximity guessing.
        _drawerPanel.ResultClicked = OpenWorkbenchForId;
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

    /// <summary>In-session pin sync (DEF-v1.0-004): targeted per-pin
    /// updates that preserve AtlasId↔rendering tracking, so an edited pin
    /// updates its own rendering instead of orphaning it and duplicating.
    /// Full ReconcileOnMapReady is reserved for map/world reconstruction
    /// in OnMapAvailable. Display filters/clustering are re-applied so
    /// edits that change filter membership take effect immediately.</summary>
    private void ResyncPins()
    {
        _pinAdapter.SyncAllPins(_pinStore, _displayController.IsDisplayHidden);
        ReapplyDisplay();
    }

    private void CaptureQuickPin()
    {
        if (_quickPinCapture.TryCapture(_pinStore, out string quickPinMessage))
        {
            // RC8-10: the new store entity must gain its map rendering NOW,
            // through the tracking-preserving targeted sync — ReapplyDisplay
            // alone only re-filters what already renders, which left a quick
            // pin invisible until some later resync.
            ResyncPins();
        }

        if (quickPinMessage.Length > 0)
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, quickPinMessage);
        }
    }

    private bool PaletteActive()
    {
        return _settings.EnhancedPinPalette.Value && !_compatibility.PinManagerPresent && !_palettePanel.HasFailed;
    }

    /// <summary>Opens one CC side panel exclusively at the shared dock,
    /// with the NoMap cartography-table gate applied.</summary>
    private void OpenSidePanel(int token, Map.CcSidePanel panel)
    {
        if (!AtlasAccessAllowed(out string denial))
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, denial);
            return;
        }

        panel.UiScale = _settings.UiScale.Value;
        _mapUi.OpenExclusive(token, panel.Toggle);
    }

    /// <summary>Toolbar [Quick Pin] (#102): closes the map and arms a
    /// one-shot capture — the next deliberate click (or the quick-pin
    /// hotkey) captures what the player is looking at through the
    /// existing QuickPinCapture (creature refusal and duplicate
    /// protection included). Esc cancels; F7 remains the instant path.</summary>
    private void ArmQuickPin()
    {
        // The same NoMap cartography-table gate every other panel entry
        // uses: arming is an atlas write path (it creates a pin).
        if (!AtlasAccessAllowed(out string denial))
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, denial);
            return;
        }

        try
        {
            _mapUi.CloseAllSurfaces();
            Minimap.instance?.SetMapMode(Minimap.MapMode.Small);
        }
        catch
        {
            // If the map cannot close, the armed mode still works later.
        }

        _quickPinArmed = true;
        Player.m_localPlayer?.Message(MessageHud.MessageType.Center, AtlasStrings.Get("quickpin.armed"));
    }

    /// <summary>Drawer toggle shared by the hotkey and the large-map
    /// button, with the NoMap cartography-table gate applied to both.</summary>
    private void ToggleDrawer()
    {
        if (AtlasAccessAllowed(out string drawerDenial))
        {
            _drawerPanel.UiScale = _settings.UiScale.Value;
            _drawerPanel.Toggle(
                _settings.DrawerShowDirt.Value,
                _settings.DrawerShowPaved.Value,
                _settings.DrawerShowPins.Value,
                _settings.DrawerCluster.Value);
        }
        else
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, drawerDenial);
        }
    }

    /// <summary>Contextual pin UX (#95/#96), throttled: while the map
    /// cursor is over an editable pin, shows the accelerator hint plus the
    /// matching action button — "Edit Pin" for managed pins, "Upgrade &
    /// Edit" for adoptable vanilla ones. The action stays alive while the
    /// pointer travels to the button (hover + grace window). Also enforces
    /// the vanilla-selector visibility on the same cadence. Never touches
    /// vanilla pin input.</summary>
    private void UpdateEditHint(float unscaledDeltaTime)
    {
        _hintElapsed += unscaledDeltaTime;
        if (_hintElapsed < 0.2f)
        {
            return;
        }

        _hintElapsed = 0f;
        EnforceVanillaPaletteVisibility();

        if (_workbenchPanel.IsVisible)
        {
            _mapUi.SetHint(null);
            _mapUi.SetContext(null, null);
            return;
        }

        Minimap.PinData hoverPin = null!;
        bool hovering = MinimapReflection.TryScreenToWorldPoint(Input.mousePosition, out Vector3 cursorWorld) &&
            _pinAdapter.TryFindNearest(cursorWorld, 30f, out hoverPin, out _);

        if (hovering && _pinAdapter.TryGetManagedId(hoverPin!, out AtlasId managedId))
        {
            _contextGrace = ContextGraceSeconds;
            _mapUi.SetHint(AtlasStrings.Format("hud.editHint", _settings.WorkbenchHotkey.Value));
            AtlasId captured = managedId;
            _mapUi.SetContext(AtlasStrings.Get("hud.editPin"), () => OpenWorkbenchForId(captured));
            return;
        }

        if (hovering && _pinAdapter.IsAdoptableVanilla(hoverPin!) && !_compatibility.PinManagerPresent)
        {
            _contextGrace = ContextGraceSeconds;
            _mapUi.SetHint(AtlasStrings.Format("hud.editHint", _settings.WorkbenchHotkey.Value));
            Minimap.PinData capturedPin = hoverPin!;
            _mapUi.SetContext(AtlasStrings.Get("hud.upgradeEdit"), () => UpgradeAndEdit(capturedPin));
            return;
        }

        if (_mapUi.PointerOverContext)
        {
            return;
        }

        _contextGrace -= 0.2f;
        if (_contextGrace > 0f)
        {
            return;
        }

        _mapUi.SetHint(null);
        _mapUi.SetContext(null, null);
    }

    /// <summary>Opens the Pin Workbench for a known managed pin by its
    /// stable identity — no proximity guessing.</summary>
    private void OpenWorkbenchForId(AtlasId id)
    {
        if (_pinCommands is null || !_pinStore.TryGet(id, out AtlasPin pin) || pin.Deleted)
        {
            return;
        }

        if (!AtlasAccessAllowed(out string denial))
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, denial);
            return;
        }

        _workbenchPanel.UiScale = _settings.UiScale.Value;
        _mapUi.OpenExclusive(_workbenchToken,
            () => _workbenchPanel.OpenForManaged(pin, _pinCommands.Operations, ResyncPins));
    }

    /// <summary>The "Upgrade &amp; Edit" context action: converts an
    /// existing vanilla marker into a managed pin (internally: adoption —
    /// position/icon/name/checked preserved, exactly one rendering) and
    /// opens the editor for it.</summary>
    private void UpgradeAndEdit(Minimap.PinData pin)
    {
        if (_pinCommands is null)
        {
            return;
        }

        if (!AtlasAccessAllowed(out string denial))
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, denial);
            return;
        }

        if (!_pinAdapter.ContainsPin(pin) || !_pinAdapter.IsAdoptableVanilla(pin))
        {
            return;
        }

        AtlasPin? managed = _pinAdapter.Adopt(_pinStore, pin);
        if (managed is null)
        {
            return;
        }

        _workbenchPanel.UiScale = _settings.UiScale.Value;
        _mapUi.OpenExclusive(_workbenchToken,
            () => _workbenchPanel.OpenForManaged(managed, _pinCommands.Operations, ResyncPins));
    }

    /// <summary>A palette-placed pin finished its vanilla naming flow:
    /// associate the AtlasPin now, with the palette's icon identity and
    /// default category. Exactly one rendering and one entity — the
    /// existing rendering is tracked, never replaced (#96).</summary>
    private void HandlePaletteBirth(Minimap.PinData born)
    {
        if (!_pinAdapter.ContainsPin(born) || !_pinAdapter.IsAdoptableVanilla(born))
        {
            // Removed (or claimed by something else) during naming.
            return;
        }

        AtlasPin? managed = _pinAdapter.Adopt(_pinStore, born);
        if (managed is null)
        {
            return;
        }

        string iconId = _birthTracker.IconId;
        string category = _birthTracker.Category;
        _pinStore.Mutate(managed.Id, pin =>
        {
            if (iconId.Length > 0)
            {
                pin.IconId = iconId;
            }

            pin.Category = category;
            pin.Source = AtlasPinSource.Managed;
        });
        _palettePanel.NoteUsed(iconId);
        // Targeted sync (not just re-filtering): the icon-id mutation above
        // may swap the rendering to a distinct CC sprite immediately.
        ResyncPins();
        if (_settings.DebugLogging.Value)
        {
            _log.LogInfo($"Palette marker born managed: {managed.Id} icon {iconId} category \"{category}\".");
        }
    }

    /// <summary>Keeps the vanilla right-side map rail hidden while CC owns
    /// its functionality, and restores it the moment any fallback applies
    /// (settings, conflicting pin manager, CC UI failure, disable). The
    /// five placeable icon selectors follow the enhanced-palette rules; the
    /// death/boss filter buttons and the visible-to-others toggle follow
    /// Map/ShowVanillaMapControls (their behavior lives on in Atlas →
    /// System Markers, driven through vanilla state). Only SetActive is
    /// ever used — nothing vanilla is destroyed (#99).</summary>
    private void EnforceVanillaPaletteVisibility()
    {
        bool wantPlaceablesVisible = !_settings.Enabled.Value ||
            !_settings.EnhancedPinPalette.Value ||
            _settings.ShowVanillaPinPalette.Value ||
            _settings.ShowVanillaMapControls.Value ||
            _compatibility.PinManagerPresent ||
            _palettePanel.HasFailed ||
            _mapUi.HasFailed;
        foreach (GameObject button in MinimapReflection.GetPlaceableIconButtons())
        {
            if (button != null && button.activeSelf != wantPlaceablesVisible)
            {
                button.SetActive(wantPlaceablesVisible);
            }
        }

        // The toolbar is the only route to every replacement surface, and
        // the drawer is the only route to System Markers — either failing
        // means the vanilla rail must come back (#99 "CC UI failure").
        bool wantRailVisible = !_settings.Enabled.Value ||
            _settings.ShowVanillaMapControls.Value ||
            _compatibility.PinManagerPresent ||
            _systemMarkersPanel.HasFailed ||
            _drawerPanel.HasFailed ||
            _mapUi.HasFailed;
        foreach (GameObject button in MinimapReflection.GetSystemFilterButtons())
        {
            if (button != null && button.activeSelf != wantRailVisible)
            {
                button.SetActive(wantRailVisible);
            }
        }

        try
        {
            GameObject? publicPosition = Minimap.instance != null && Minimap.instance.m_publicPosition != null
                ? Minimap.instance.m_publicPosition.gameObject
                : null;
            if (publicPosition != null && publicPosition.activeSelf != wantRailVisible)
            {
                publicPosition.SetActive(wantRailVisible);
            }
        }
        catch
        {
            // Missing toggle just stays vanilla.
        }
    }

    /// <summary>Unconditional vanilla-rail restore for teardown.</summary>
    private void RestoreVanillaPalette()
    {
        try
        {
            foreach (GameObject button in MinimapReflection.GetPlaceableIconButtons())
            {
                if (button != null && !button.activeSelf)
                {
                    button.SetActive(true);
                }
            }

            foreach (GameObject button in MinimapReflection.GetSystemFilterButtons())
            {
                if (button != null && !button.activeSelf)
                {
                    button.SetActive(true);
                }
            }

            if (Minimap.instance != null && Minimap.instance.m_publicPosition != null &&
                !Minimap.instance.m_publicPosition.gameObject.activeSelf)
            {
                Minimap.instance.m_publicPosition.gameObject.SetActive(true);
            }
        }
        catch
        {
            // Map may already be gone; vanilla state dies with it anyway.
        }
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

    /// <summary>NoMap worlds keep the atlas as a cartography-table ritual:
    /// panels and consoles work only near a table. Detection failures fail
    /// open so the atlas can never be bricked by an API change.</summary>
    private bool AtlasAccessAllowed(out string denial)
    {
        denial = "";
        try
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey("nomap"))
            {
                return true;
            }

            Player player = Player.m_localPlayer;
            if (player is not null &&
                SurveyScanner.AnyInstanceNear("piece_maptable", player.transform.position, 10f))
            {
                return true;
            }

            denial = AtlasStrings.Get("hud.noMapNeedTable");
            return false;
        }
        catch
        {
            return true;
        }
    }

    private bool _onboardingChecked;

    /// <summary>One-time first-run tip pointing at the two entry hotkeys.</summary>
    private void ShowOnboardingOnce()
    {
        if (_onboardingChecked)
        {
            return;
        }

        _onboardingChecked = true;
        try
        {
            string path = System.IO.Path.Combine(
                BepInEx.Paths.ConfigPath, "ConcernedCatMods", "ConcernedCartographer", "onboarding-shown.txt");
            if (System.IO.File.Exists(path))
            {
                return;
            }

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, DateTime.UtcNow.ToString("o"));
            Player.m_localPlayer?.Message(
                MessageHud.MessageType.Center,
                AtlasStrings.Get("hud.onboarding"));
        }
        catch
        {
            // A failed tip is never worth an error.
        }
    }

    private static bool GamepadDown(string buttonName)
    {
        if (string.IsNullOrEmpty(buttonName))
        {
            return false;
        }

        try
        {
            return ZInput.GetButtonDown(buttonName);
        }
        catch
        {
            return false;
        }
    }

    private void OpenWorkbenchNear(Vector3 world)
    {
        if (_pinCommands is null)
        {
            return;
        }

        if (!AtlasAccessAllowed(out string denial))
        {
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, denial);
            return;
        }

        _workbenchPanel.UiScale = _settings.UiScale.Value;

        var point = new RoadPoint(world.x, world.y, world.z);
        PinOperations operations = _pinCommands.Operations;

        // The workbench applies edits to a known managed pin, so it must
        // resync through the tracking-preserving in-session path — a full
        // reconcile here is exactly the DEF-v1.0-004 duplicate bug.
        Action resync = ResyncPins;

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
            AtlasPin captured = nearestManaged;
            _mapUi.OpenExclusive(_workbenchToken,
                () => _workbenchPanel.OpenForManaged(captured, operations, resync));
            return;
        }

        if (_pinAdapter.TryFindNearest(world, 30f, out Minimap.PinData mapPin, out _))
        {
            if (_pinAdapter.IsAdoptableVanilla(mapPin) && _compatibility.PinManagerPresent)
            {
                // Another pin manager owns the vanilla-pin editing workflow;
                // adoption stays available but only through the explicit
                // cc_pins adopt command.
                _workbenchPanel.OpenReadOnly(
                    $"\"{mapPin.m_name}\" — another pin manager is installed; use 'cc_pins adopt' to manage this pin here");
                return;
            }

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
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

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
            case "compat":
                return _compatibility.Report();
            case "backup":
                if (_worldUid is not long backupUid)
                {
                    return "No world loaded.";
                }

                return "Backed up to " + _backupTools.Backup(backupUid);
            case "backups":
                if (_worldUid is not long listUid)
                {
                    return "No world loaded.";
                }

                List<string> backups = _backupTools.ListBackups(listUid);
                if (backups.Count == 0)
                {
                    return "No backups yet. 'cc_atlas backup' creates one; exports/imports use the same folders.";
                }

                var backupList = new System.Text.StringBuilder($"{backups.Count} backup(s), newest first:");
                for (int index = 0; index < backups.Count && index < 10; index++)
                {
                    backupList.Append($"\n  {index + 1}. {System.IO.Path.GetFileName(backups[index])}");
                }

                backupList.Append("\n'cc_atlas restore <n>' restores one (takes a safety backup first).");
                return backupList.ToString();
            case "restore":
                if (_worldUid is not long restoreUid)
                {
                    return "No world loaded.";
                }

                List<string> candidates = _backupTools.ListBackups(restoreUid);
                if (!int.TryParse(remainder.Trim(), out int restoreIndex) ||
                    restoreIndex < 1 || restoreIndex > candidates.Count)
                {
                    return "Usage: cc_atlas restore <n>  (see 'cc_atlas backups')";
                }

                return _backupTools.Restore(restoreUid, candidates[restoreIndex - 1]);
            case "support":
                if (_worldUid is not long supportUid)
                {
                    return "No world loaded.";
                }

                string report = _backupTools.WriteSupportReport(
                    supportUid,
                    Plugin.PluginVersion,
                    $"enabled={_settings.Enabled.Value}, capture={_settings.CaptureConstructionActions.Value}, " +
                    $"reconcile={_settings.ReconcileTerrainChanges.Value}, " +
                    $"survey={_settings.SurveyRulesEnabled.Value}, cluster={_settings.DrawerCluster.Value}, " +
                    $"contrast={_settings.HighContrast.Value}, uiScale={_settings.UiScale.Value}");
                return "Sanitized support report (no positions/names/notes) written to " + report;
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

    /// <summary>Backs the `cc_survey` console command: review-before-commit
    /// for survey observations.</summary>
    internal string ExecuteSurveyCommand(string[] args)
    {
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

        if (_disposed || !_mapReady)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        string subcommand = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        string remainder = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        IReadOnlyList<SurveyEngine.Observation> observations = _surveyEngine.Observations;

        switch (subcommand)
        {
            case "status":
                return $"Survey: {(_settings.SurveyRulesEnabled.Value ? "ENABLED" : "disabled (Survey/SurveyRulesEnabled)")}, " +
                    $"{_surveyEngine.Rules.Rules.Count} rule(s), {_surveyEngine.Rules.Blacklist.Count} blacklist pattern(s), " +
                    $"{observations.Count} pending observation(s). Rules file: {SurveyRulePersistence.RulePath}";
            case "list":
                if (observations.Count == 0)
                {
                    return "No pending observations.";
                }

                var builder = new System.Text.StringBuilder($"{observations.Count} observation(s):");
                for (int index = 0; index < observations.Count && index < 15; index++)
                {
                    SurveyEngine.Observation observation = observations[index];
                    builder.Append($"\n  {index + 1}. {observation.SuggestedName} [{observation.Category}] at ({observation.Position.X:0}, {observation.Position.Z:0})");
                }

                if (observations.Count > 15)
                {
                    builder.Append($"\n  ... and {observations.Count - 15} more.");
                }

                builder.Append("\ncc_survey accept <n|all> / reject <n|all>");
                return builder.ToString();
            case "accept":
                if (remainder == "all")
                {
                    int accepted = _surveyEngine.AcceptAll(_pinStore);
                    ResyncPins();
                    return $"Accepted {accepted} observation(s) as pins.";
                }

                if (int.TryParse(remainder, out int acceptIndex) && acceptIndex >= 1 && acceptIndex <= observations.Count)
                {
                    _surveyEngine.Accept(observations[acceptIndex - 1].Id, _pinStore);
                    ResyncPins();
                    return $"Accepted observation {acceptIndex}.";
                }

                return "Usage: cc_survey accept <n|all>";
            case "reject":
                if (remainder == "all")
                {
                    return $"Rejected {_surveyEngine.RejectAll()} observation(s).";
                }

                if (int.TryParse(remainder, out int rejectIndex) && rejectIndex >= 1 && rejectIndex <= observations.Count)
                {
                    _surveyEngine.Reject(observations[rejectIndex - 1].Id);
                    return $"Rejected observation {rejectIndex}.";
                }

                return "Usage: cc_survey reject <n|all>";
            case "reload":
                _surveyEngine.Rules = _surveyRulePersistence.LoadOrCreate();
                return $"Reloaded {_surveyEngine.Rules.Rules.Count} rule(s) and {_surveyEngine.Rules.Blacklist.Count} blacklist pattern(s).";
            case "path":
                return SurveyRulePersistence.RulePath + " (the file is the shareable import/export format)";
            default:
                return "Usage: cc_survey [status|list|accept <n|all>|reject <n|all>|reload|path]";
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
            _routePersistence.Save(uid, _routeStore);
        }
    }

    /// <summary>Backs the `cc_sync` console command: explicit share and
    /// review-before-apply for the collaborative atlas.</summary>
    internal string ExecuteSyncCommand(string[] args)
    {
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

        if (_disposed || !_mapReady)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        string subcommand = args.Length == 0 ? "status" : args[0].ToLowerInvariant();
        string remainder = args.Length > 1 ? string.Join(" ", args, 1, args.Length - 1).Trim() : "";

        switch (subcommand)
        {
            case "status":
                (List<AtlasPin> sharedPins, List<AtlasRoute> sharedRoutes) = SyncPlanner.CollectShared(_pinStore, _routeStore);
                return $"Sync: sharing {sharedPins.Count} pin(s) and {sharedRoutes.Count} route(s) " +
                    $"(scope table/server, tombstones included). Inbox: {_syncInbox.Envelopes.Count} pending share(s). " +
                    "Set a pin/route scope with 'cc_pins scope table' or 'cc_routes ...' to share it; 'cc_sync share' broadcasts.";
            case "share":
                (List<AtlasPin> pins, List<AtlasRoute> routes) = SyncPlanner.CollectShared(_pinStore, _routeStore);
                if (pins.Count == 0 && routes.Count == 0)
                {
                    return "Nothing is scoped for sharing yet. 'cc_pins scope table' near a pin shares it.";
                }

                string playerName = Player.m_localPlayer?.GetPlayerName() ?? "";
                _syncTransport.Share(_authorId, playerName, pins, routes, out string shareMessage);
                _log.LogInfo($"Sync share: {shareMessage}");
                return shareMessage;
            case "inbox":
                if (_syncInbox.Envelopes.Count == 0)
                {
                    return "Sync inbox: empty.";
                }

                var builder = new System.Text.StringBuilder("Sync inbox:");
                foreach (SyncInbox.Envelope pending in _syncInbox.Envelopes)
                {
                    builder.Append($"\n  {pending.AuthorName}: {pending.Pins.Count} pin(s), {pending.Routes.Count} route(s) " +
                        $"at {pending.ReceivedUtc:HH:mm} UTC — 'cc_sync preview {pending.AuthorName}'");
                }

                return builder.ToString();
            case "preview":
                if (!_syncInbox.TryPeek(remainder, out SyncInbox.Envelope preview))
                {
                    return $"No pending share from \"{remainder}\". 'cc_sync inbox' lists them.";
                }

                SyncPlan previewPlan = SyncPlanner.Plan(_pinStore, _routeStore, preview.Pins, preview.Routes);
                List<string> deletionNames = previewPlan.DeletionNames(10);
                string deletionDetail = deletionNames.Count == 0
                    ? ""
                    : $"\n  Would DELETE: {string.Join(", ", deletionNames)}" +
                      (previewPlan.TombstonePins.Count + previewPlan.TombstoneRoutes.Count > deletionNames.Count
                          ? $" (+{previewPlan.TombstonePins.Count + previewPlan.TombstoneRoutes.Count - deletionNames.Count} more)"
                          : "");
                return $"Share from {preview.AuthorName}: {previewPlan.Summary()}.{deletionDetail}" +
                    (previewPlan.PinConflicts.Count + previewPlan.RouteConflicts.Count > 0
                        ? $" Apply with 'cc_sync apply {preview.AuthorName} mine' (keep local on conflicts) or '... theirs'."
                        : $" Apply with 'cc_sync apply {preview.AuthorName}'.");
            case "apply":
                string[] applyParts = remainder.Split(' ');
                bool takeRemote = applyParts.Length > 1 &&
                    string.Equals(applyParts[applyParts.Length - 1], "theirs", StringComparison.OrdinalIgnoreCase);
                string author = takeRemote || (applyParts.Length > 1 &&
                        string.Equals(applyParts[applyParts.Length - 1], "mine", StringComparison.OrdinalIgnoreCase))
                    ? string.Join(" ", applyParts, 0, applyParts.Length - 1)
                    : remainder;
                if (!_syncInbox.TryTake(author, out SyncInbox.Envelope envelope))
                {
                    return $"No pending share from \"{author}\".";
                }

                SyncPlan plan = SyncPlanner.Plan(_pinStore, _routeStore, envelope.Pins, envelope.Routes);
                int applied = SyncPlanner.Apply(plan, _pinStore, _routeStore, takeRemote);
                ResyncPins();
                _routeRedrawPending = true;
                SavePinsSnapshot();
                _log.LogInfo($"Sync apply from {envelope.AuthorName}: {applied} change(s); {plan.Summary()}");
                return $"Applied {applied} change(s) from {envelope.AuthorName} " +
                    $"({(takeRemote ? "conflicts took their side" : "conflicts kept your side")}). {plan.Summary()}";
            case "clear":
                _syncInbox.Clear();
                return "Sync inbox cleared.";
            default:
                return "Usage: cc_sync [status|share|inbox|preview <author>|apply <author> [mine|theirs]|clear]";
        }
    }

    /// <summary>Backs the `cc_routes` console command.</summary>
    internal string ExecuteRouteCommand(string[] args)
    {
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

        if (_disposed || !_mapReady || _routeCommands is null)
        {
            return "Concerned Cartographer: no world is loaded yet.";
        }

        Player player = Player.m_localPlayer;
        if (player is null)
        {
            return "Concerned Cartographer: no local player.";
        }

        return _routeCommands.Execute(args, player.transform.position);
    }

    private void HandleTerrainOperation(CapturedTerrainOperation operation)
    {
        if (_disposed || !_settings.Enabled.Value || !_mapReady || _pipeline is null || _worldUid is null)
        {
            return;
        }

        var center = new RoadPoint(operation.Position.x, operation.Position.y, operation.Position.z);

        // DEF-v1.0-005: persistent negative terrain intent. Level/Raise
        // (terraforming side-effect paint) and Cultivate/Reset mark their
        // brush footprint as explicitly-not-road, so traversal and chunk
        // recovery can never rediscover the leftover dirt paint as a road —
        // this session or any later one. A deliberate Pathen/Paved op
        // clears the footprint it covers before its observation lands.
        float intentRadius = operation.RadiusMeters + TerrainIntentMask.BrushMarginMeters;
        if (operation.IsTerraforming || operation.RoadKind is null)
        {
            int excluded = _terrainIntent.AddExclusion(center.X, center.Z, intentRadius);
            if (excluded > 0 && _settings.DebugLogging.Value)
            {
                _rateLimited.Info(
                    "terrain-intent-add",
                    $"Terraforming at ({center.X:0.#}, {center.Z:0.#}) r={operation.RadiusMeters:0.#}m: " +
                    $"{excluded} cell(s) marked not-road ({_terrainIntent.Count} total).");
            }
        }
        else
        {
            _terrainIntent.ClearExclusion(center.X, center.Z, intentRadius);
        }

        if (_settings.ReconcileTerrainChanges.Value)
        {
            int removed = 0;
            if (operation.RoadKind is RoadKind paintedKind && !operation.IsTerraforming)
            {
                // A kind change: new paint of one kind erases covered ink of
                // the other. Same-kind ink stays put (suppression keeps it
                // from duplicating).
                RoadKind other = paintedKind == RoadKind.Dirt ? RoadKind.Paved : RoadKind.Dirt;
                removed = RemoveCoverageWithBackup(other, center, operation.RadiusMeters);
            }
            else
            {
                // Level/Raise/Cultivate/Reset create no roads and ERASE the
                // covered road data of both kinds (RC8): a base pad leveled
                // over an old road removes that stretch from the atlas. A
                // later explicit Pathen/Paved over the same ground wins by
                // clearing the intent mask and recording fresh construction.
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
        if (!AtlasAccessAllowed(out string atlasDenial))
        {
            return atlasDenial;
        }

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
                changed = removed > 0;
                summary = $"Cleared {removed} road point(s) within {rebuildRadius:0.#} m. " +
                    "Roads return only when you Pathen/Pave the ground again ('cc_roads undo' reverts).";
                break;
            case "undo":
                changed = _editor.Undo(out summary);
                break;
            case "align":
                // DEF-v1.0-002 diagnostic: native pin vs overlay cross at
                // known world positions, with the full projection numbers
                // logged. Never touches stored data.
                if (args.Length > 1 && string.Equals(args[1], "clear", StringComparison.OrdinalIgnoreCase))
                {
                    // Immediate redraw: the diagnostic must leave nothing
                    // behind, not wait for the debounce window.
                    _renderer.ClearAlignmentProbe();
                    _renderer.RedrawAll(_atlas);
                    _redrawPending = false;
                    return "Alignment markers removed.";
                }

                if (args.Length > 1 && string.Equals(args[1], "live", StringComparison.OrdinalIgnoreCase))
                {
                    // DEF-v1.0-006: end-to-end player-vs-road-ink diagnosis
                    // with the four error classes answered separately.
                    bool standingOnRoad = _probe.TryClassify(playerPosition, out RoadKind classifiedKind);
                    bool hasNearest = _atlas.TryGetNearestPointOnRoads(
                        position, 50f, out RoadPoint nearestPoint, out float nearestDistance);
                    string liveReport = Map.LiveAlignmentProbe.BuildReport(
                        playerPosition,
                        standingOnRoad,
                        classifiedKind,
                        _surveyor?.LatestSample,
                        _pipeline?.LastAccepted,
                        hasNearest,
                        nearestPoint,
                        nearestDistance,
                        _renderer);
                    _log.LogInfo("cc_roads align live\n" + liveReport);
                    return liveReport;
                }

                return _renderer.RunAlignmentProbe(playerPosition, _atlas);
            default:
                // "align" stays functional but unadvertised: it is a
                // DEF-v1.0-002 diagnostic, not a player tool.
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
        if (_worldUid is null)
        {
            return;
        }

        if (_atlas.IsDirty && _persistence.Save(_worldUid.Value, _atlas))
        {
            _atlas.MarkClean();
        }

        if (_terrainIntent.IsDirty && _terrainIntentPersistence.Save(_worldUid.Value, _terrainIntent))
        {
            _terrainIntent.MarkClean();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _workbenchPanel.Close();
        RestoreVanillaPalette();
        MapInputGate.Uninstall();
        SaveIfDirty();
        SavePinsSnapshot();
        _pipeline?.EndAllStrokes();
        _constructionCapture.OperationCaptured -= HandleTerrainOperation;
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
        SavePinsSnapshot();

        _worldUid = uid;
        _atlas = _persistence.Load(uid);
        _terrainIntent = _terrainIntentPersistence.Load(uid);
        _pinStore = _pinPersistence.Load(uid);
        _pinStore.LocalAuthor = _authorId;
        _pinStore.Changed += _pinPersistence.QueueJournal;
        _pinAdapter.Reset();
        _pinCommands = new PinCommandHandler(
            _pinStore,
            new PinOperations(_pinStore),
            _pinAdapter,
            _log,
            ResyncPins);
        _displayController.Reset();
        _routeStore = _routePersistence.Load(uid);
        _routeStore.LocalAuthor = _authorId;
        _routeStore.Changed += _routePersistence.QueueJournal;
        _syncInbox.Clear();
        _routeCommands = new RouteCommandHandler(
            _routeStore,
            new RouteOperations(_routeStore),
            _atlas,
            _settings,
            _log,
            () => _routeRedrawPending = true);
        _pipeline = new RoadObservationPipeline(_atlas, _terrainIntent);
        _editor = new RoadAtlasEditor(_atlas);
        _surveyor = new RoadSurveyor(_settings, _probe, _atlas, _log);
        _redrawPending = false;
        _autosaveElapsed = 0f;
    }
}
