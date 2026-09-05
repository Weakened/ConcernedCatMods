using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
using TheConcernedCat.ConcernedTeamster.Domain.Load;
using TheConcernedCat.ConcernedTeamster.Domain.Routes;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The read-only Cartographer route picker (CT-022). Constructed
/// only when the capability probe reported Available — with Cartographer
/// absent this class is never instantiated, so no integration stub exists.
/// Rows are buttons bound to the headless presenter's rules; clicking an
/// eligible row selects that route by stable id, held in Teamster only.
/// Refreshes are change-driven: the store's ChangeStamp is polled at 1 Hz
/// while visible and geometry is re-copied only when it moves, so renames,
/// deletions, and archives in Cartographer surface within a second with an
/// explicit status — never a stale ghost. Zero writes: the panel calls only
/// TryRead* on the capability. Fail-closed like every Teamster surface: any
/// UI exception disables the picker for the session with one ERROR line.</summary>
internal sealed class RoutePickerPanel
{
    private const float PanelWidth = 380f;
    private const float PanelHeight = 570f;
    private const float RowHeight = 26f;
    private const int RowCount = 11;
    private const double RefreshPeriodSeconds = 1.0;

    /// <summary>Terrain samples per frame while a profile is building
    /// (CT-023): 24 keeps a fresh 4096-position worst case under ~3 s of
    /// frames while each frame's game reads stay far below the telemetry
    /// sampler's own per-tick work.</summary>
    private const int ProfileSamplesPerFrame = 24;

    private readonly ManualLogSource _log;
    private readonly LoadModel? _loadModel;
    private readonly Func<float?> _cartMassProvider;
    private readonly RouteProfileCache _profileCache = new();
    private readonly RouteReportPanel _reportPanel;
    private string _selectedRouteName = "";
    private RouteProfiler? _profiler;
    private Guid? _profileRouteId;
    private ulong _profileFingerprint;
    private RouteProfile? _shownProfile;
    private RouteLoadBottleneck.Result? _shownBottleneck;
    private Text[] _profileLines = Array.Empty<Text>();
    private bool _failed;

    private GameObject? _panel;
    private Text? _statusLine;
    private Text? _overflowLine;
    private GameObject[] _rowButtons = Array.Empty<GameObject>();
    private Text[] _rowTexts = Array.Empty<Text>();
    private readonly Guid?[] _rowRouteIds = new Guid?[RowCount];

    private Guid? _selectedRouteId;
    private long _lastChangeStamp = long.MinValue;
    private double _nextRefreshTime;

    internal RoutePickerPanel(ManualLogSource log, LoadModel? loadModel, Func<float?> cartMassProvider)
    {
        _log = log;
        _loadModel = loadModel;
        _cartMassProvider = cartMassProvider;
        _reportPanel = new RouteReportPanel(log);
    }

    /// <summary>The validated selection for later leaves (CT-023); null when
    /// nothing is selected or the source invalidated it.</summary>
    internal Guid? SelectedRouteId => _selectedRouteId;

    internal bool IsVisible => _panel != null && _panel.activeSelf;

    internal void Toggle()
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_panel == null && !EnsurePanel())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                ForceRefresh();
            }
            else
            {
                // The report is fed exclusively by this panel's refresh
                // loop; left open without it, it would freeze into a stale
                // ghost. Closing the picker closes the report.
                _reportPanel.Hide();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    internal void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }

        // World exit: the next world's route ids are a different catalog;
        // an id held across the boundary would be validated against it and
        // could only mislead. Clear eagerly, fail closed — profiles and the
        // cache go with it (terrain and ids are world-scoped).
        _selectedRouteId = null;
        _lastChangeStamp = long.MinValue;
        _profiler?.Cancel();
        _profiler = null;
        _profileRouteId = null;
        _shownProfile = null;
        _shownBottleneck = null;
        _selectedRouteName = "";
        _profileCache.Clear();
        _reportPanel.Hide();
    }

    internal void HandleFrame(double nowSeconds)
    {
        if (_failed || !IsVisible)
        {
            return;
        }

        try
        {
            if (nowSeconds >= _nextRefreshTime)
            {
                _nextRefreshTime = nowSeconds + RefreshPeriodSeconds;
                bool stampReadable = CartographerCapability.TryReadRouteChangeStamp(out long stamp);
                if (!stampReadable || stamp != _lastChangeStamp)
                {
                    _lastChangeStamp = stampReadable ? stamp : long.MinValue;
                    Refresh();
                }
                else
                {
                    // The cart's mass changes without any Cartographer edit;
                    // the load line re-binds at 1 Hz regardless of the route
                    // stamp so the verdict always answers the CURRENT cart.
                    RecomputeBottleneck();
                    RenderProfileLines();
                }
            }

            // Profiling advances every frame (bounded), not just on the
            // 1 Hz refresh — the budget is per frame by design (CT-023).
            AdvanceProfiling();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void ForceRefresh()
    {
        _nextRefreshTime = 0d;
        _lastChangeStamp = long.MinValue;
        Refresh();
    }

    private void Refresh()
    {
        bool readable = CartographerCapability.TryReadRoutes(out var routes);
        RoutePickerPresenter.ViewModel viewModel =
            RoutePickerPresenter.Present(readable, readable ? routes : null, _selectedRouteId);
        _selectedRouteId = viewModel.EffectiveSelectedId;

        SyncProfileTarget(
            readable && _selectedRouteId is Guid selected ? FindRoute(routes, selected) : null);
        RecomputeBottleneck();

        if (_statusLine == null || _rowTexts.Length != RowCount)
        {
            return;
        }

        _statusLine.text = viewModel.StatusLine;
        int shown = Math.Min(RowCount, viewModel.Rows.Count);
        for (int index = 0; index < RowCount; index++)
        {
            bool used = index < shown;
            RoutePickerPresenter.Row? row = used ? viewModel.Rows[index] : null;
            _rowRouteIds[index] = row is { Eligible: true } ? row.RouteId : null;
            _rowTexts[index].text = row?.Text ?? string.Empty;
            if (_rowButtons[index].activeSelf != used)
            {
                _rowButtons[index].SetActive(used);
            }
        }

        int hidden = viewModel.Rows.Count - shown;
        if (_overflowLine != null)
        {
            _overflowLine.text = hidden > 0
                ? "… +" + hidden.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " more (rename in Cartographer to sort forward)"
                : string.Empty;
        }

        RenderProfileLines();
    }

    private static CartographerRouteSnapshot? FindRoute(
        IReadOnlyList<CartographerRouteSnapshot> routes, Guid routeId)
    {
        for (int index = 0; index < routes.Count; index++)
        {
            if (routes[index].Id == routeId)
            {
                return routes[index];
            }
        }

        return null;
    }

    /// <summary>Points the profiler at the selected route's CURRENT
    /// geometry. Same id + same fingerprint → keep whatever is shown or in
    /// flight; anything else cancels the old work (a stale profile is never
    /// displayed) and serves from cache or starts a fresh bounded profiler.</summary>
    private void SyncProfileTarget(CartographerRouteSnapshot? route)
    {
        if (route is null)
        {
            _profiler?.Cancel();
            _profiler = null;
            _profileRouteId = null;
            _shownProfile = null;
            _shownBottleneck = null;
            _selectedRouteName = "";
            return;
        }

        _selectedRouteName = route.Name;
        ulong fingerprint = RouteGeometry.Fingerprint(route.Points);
        if (_profileRouteId == route.Id && _profileFingerprint == fingerprint &&
            (_shownProfile is not null || _profiler is not null))
        {
            return;
        }

        _profiler?.Cancel();
        _profiler = null;
        _profileRouteId = route.Id;
        _profileFingerprint = fingerprint;
        _shownProfile = null;
        _shownBottleneck = null;
        if (_profileCache.TryGet(route.Id, fingerprint, out RouteProfile? cached))
        {
            _shownProfile = cached;
            return;
        }

        _profiler = new RouteProfiler(route.Points, TerrainAdapter.TrySamplePoint);
    }

    private void AdvanceProfiling()
    {
        if (_profiler is null)
        {
            return;
        }

        _profiler.Advance(ProfileSamplesPerFrame);
        if (_profiler.IsComplete)
        {
            RouteProfile? profile = _profiler.TryBuildProfile();
            _profiler = null;
            if (profile is not null && _profileRouteId is Guid routeId)
            {
                _profileCache.Store(routeId, _profileFingerprint, profile);
                _shownProfile = profile;
                RecomputeBottleneck();
            }
        }

        RenderProfileLines();
    }

    /// <summary>Re-binds the load check to the current cart mass; called on
    /// every 1 Hz refresh (cargo changes) and on profile completion. Cheap:
    /// one dominance scan over the calibration rows.</summary>
    private void RecomputeBottleneck()
    {
        _shownBottleneck = _loadModel is not null && _shownProfile is not null
            ? RouteLoadBottleneck.Evaluate(_shownProfile, _loadModel, _cartMassProvider())
            : null;
    }

    private void RenderProfileLines()
    {
        if (_profileLines.Length != RouteProfilePresenter.LineCount || _profileLines[0] == null)
        {
            return;
        }

        IReadOnlyList<string> lines = RouteProfilePresenter.Present(
            _selectedRouteId is not null,
            _profiler is not null,
            _profiler?.PositionsProbed ?? 0,
            _profiler?.PositionCount ?? 0,
            _shownProfile,
            _shownBottleneck);
        for (int index = 0; index < _profileLines.Length; index++)
        {
            _profileLines[index].text = lines[index];
        }

        // An open report follows the same cadence as the profile block.
        if (_reportPanel.IsVisible)
        {
            _reportPanel.Render(BuildReportViewModel());
        }
    }

    private RouteReportPresenter.ViewModel BuildReportViewModel()
    {
        return RouteReportPresenter.Present(
            _selectedRouteName, _shownProfile, _loadModel, _cartMassProvider());
    }

    private void SelectRow(int index)
    {
        try
        {
            Guid? routeId = _rowRouteIds[index];
            if (routeId is null)
            {
                return;
            }

            _selectedRouteId = routeId;
            ForceRefresh();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private bool EnsurePanel()
    {
        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);
        var bodyColor = new Color(0.85f, 0.85f, 0.82f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Cartographer Routes", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        _statusLine = gui.CreateText(
            string.Empty, _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f),
            font, 15, bodyColor, outline: false, Color.black, PanelWidth - 40f, 24f,
            addContentSizeFitter: false).GetComponent<Text>();
        _statusLine.alignment = TextAnchor.UpperLeft;

        _rowButtons = new GameObject[RowCount];
        _rowTexts = new Text[RowCount];
        float y = -86f;
        for (int index = 0; index < RowCount; index++)
        {
            int captured = index;
            GameObject button = gui.CreateButton(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, y - (RowHeight / 2f)), PanelWidth - 40f, RowHeight - 2f);
            button.GetComponent<Button>().onClick.AddListener(() => SelectRow(captured));
            _rowButtons[index] = button;
            _rowTexts[index] = button.GetComponentInChildren<Text>();
            _rowTexts[index].fontSize = 14;
            _rowTexts[index].alignment = TextAnchor.MiddleLeft;
            button.SetActive(false);
            y -= RowHeight;
        }

        _overflowLine = gui.CreateText(
            string.Empty, _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f),
            font, 13, bodyColor, outline: false, Color.black, PanelWidth - 40f, 22f,
            addContentSizeFitter: false).GetComponent<Text>();

        // CT-023: the profile block for the selected route, below the list.
        _profileLines = new Text[RouteProfilePresenter.LineCount];
        float profileY = -388f;
        for (int index = 0; index < _profileLines.Length; index++)
        {
            _profileLines[index] = gui.CreateText(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, profileY - 10f),
                font, 13, bodyColor, outline: false, Color.black, PanelWidth - 40f, 20f,
                addContentSizeFitter: false).GetComponent<Text>();
            _profileLines[index].alignment = TextAnchor.UpperLeft;
            _profileLines[index].verticalOverflow = VerticalWrapMode.Truncate;
            profileY -= 21f;
        }

        GameObject clear = gui.CreateButton(
            "Clear", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 30f), 100f, 30f);
        clear.GetComponent<Button>().onClick.AddListener(() =>
        {
            _selectedRouteId = null;
            ForceRefresh();
        });

        // CT-024: the report opens from a visible button and renders the
        // same presenter state the profile block shows — buttons first.
        GameObject report = gui.CreateButton(
            "Report", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), 100f, 30f);
        report.GetComponent<Button>().onClick.AddListener(() =>
        {
            _reportPanel.Toggle(BuildReportViewModel());
        });

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(120f, 30f), 100f, 30f);
        close.GetComponent<Button>().onClick.AddListener(() =>
        {
            _panel!.SetActive(false);
            _reportPanel.Hide();
        });

        _panel.SetActive(false);
        return true;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        // A selection nobody can see or manage anymore must not linger for
        // a later consumer (CT-023) — fail closed all the way, profiling
        // included.
        _selectedRouteId = null;
        _profiler?.Cancel();
        _profiler = null;
        _profileRouteId = null;
        _shownProfile = null;
        _shownBottleneck = null;
        _selectedRouteName = "";
        _profileCache.Clear();
        _reportPanel.Hide();
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError(
            "Route picker disabled for this session after a UI exception; everything else keeps working: " +
            exception);
    }
}
