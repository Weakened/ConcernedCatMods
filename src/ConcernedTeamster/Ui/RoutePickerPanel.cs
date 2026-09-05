using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Cartographer;
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
    private const float PanelHeight = 430f;
    private const float RowHeight = 26f;
    private const int RowCount = 11;
    private const double RefreshPeriodSeconds = 1.0;

    private readonly ManualLogSource _log;
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

    internal RoutePickerPanel(ManualLogSource log)
    {
        _log = log;
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
        // could only mislead. Clear eagerly, fail closed.
        _selectedRouteId = null;
        _lastChangeStamp = long.MinValue;
    }

    internal void HandleFrame(double nowSeconds)
    {
        if (_failed || !IsVisible || nowSeconds < _nextRefreshTime)
        {
            return;
        }

        try
        {
            _nextRefreshTime = nowSeconds + RefreshPeriodSeconds;
            bool stampReadable = CartographerCapability.TryReadRouteChangeStamp(out long stamp);
            if (stampReadable && stamp == _lastChangeStamp)
            {
                return;
            }

            _lastChangeStamp = stampReadable ? stamp : long.MinValue;
            Refresh();
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

        GameObject clear = gui.CreateButton(
            "Clear selection", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-80f, 30f), 150f, 30f);
        clear.GetComponent<Button>().onClick.AddListener(() =>
        {
            _selectedRouteId = null;
            ForceRefresh();
        });

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(80f, 30f), 96f, 30f);
        close.GetComponent<Button>().onClick.AddListener(() => _panel!.SetActive(false));

        _panel.SetActive(false);
        return true;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        // A selection nobody can see or manage anymore must not linger for
        // a later consumer (CT-023) — fail closed all the way.
        _selectedRouteId = null;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError(
            "Route picker disabled for this session after a UI exception; everything else keeps working: " +
            exception);
    }
}
