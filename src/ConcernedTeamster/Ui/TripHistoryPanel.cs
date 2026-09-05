using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Trips;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>Trip history and comparison surface (CT-018), reached from the
/// Cart Status panel's Trips button. Trips load and summarize ONCE per
/// open/reload (bounded at the retention cap); sorting, selection markers,
/// and the distance-normalized comparison are pure presenter work. Row
/// controls: [A]/[B] select for comparison, [X] deletes with a two-step
/// confirmation ("X" → "Sure?" for 3 s). Deletion removes the raw trip
/// only — the cumulative road-quality history is documented as staying.
/// Fail-closed session-disable like every Teamster panel.</summary>
internal sealed class TripHistoryPanel
{
    private const float PanelWidth = 470f;
    private const float PanelHeight = 760f;
    private const float RowHeight = 26f;
    private const int VisibleRowCount = 10;
    private const double ConfirmWindowSeconds = 3.0;

    private readonly ManualLogSource _log;
    private bool _failed;
    private GameObject? _panel;
    private Text?[] _rowTexts = Array.Empty<Text?>();
    private Button?[] _rowSelectA = Array.Empty<Button?>();
    private Button?[] _rowSelectB = Array.Empty<Button?>();
    private Button?[] _rowDelete = Array.Empty<Button?>();
    private Text? _overflow;
    private Text? _message;
    private Text? _compareHeaderA;
    private Text? _compareHeaderB;
    private Text?[] _compareLines = Array.Empty<Text?>();

    private IReadOnlyList<Trip> _trips = Array.Empty<Trip>();
    private Domain.RoadQuality.RoadQualityIndex _segments = new();
    private List<TripSummary> _summaries = new();
    private InputField? _massInput;
    private Text?[] _bottleneckLines = Array.Empty<Text?>();
    private Text? _bottleneckHeader;
    private TripHistoryPresenter.SortColumn _sortColumn = TripHistoryPresenter.SortColumn.StartTime;
    private bool _sortDescending = true;
    private int? _selectedAId;
    private int? _selectedBId;
    private int[] _visibleTripIds = Array.Empty<int>();
    private int? _pendingDeleteId;
    private double _pendingDeleteUntil;

    public TripHistoryPanel(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public void Toggle(CartTelemetryPump? pump)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            if (_panel == null && !Build())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                Reload(pump);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }
    }

    public void HandleFrame(double nowSeconds, CartTelemetryPump? pump)
    {
        if (_failed || !IsVisible)
        {
            return;
        }

        try
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }

            if (_pendingDeleteId is not null && nowSeconds > _pendingDeleteUntil)
            {
                _pendingDeleteId = null;
                Render();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Reload(CartTelemetryPump? pump)
    {
        (_trips, _segments, _) = pump?.Trips?.LoadWorldData()
            ?? ((IReadOnlyList<Trip>)Array.Empty<Trip>(), new Domain.RoadQuality.RoadQualityIndex(), 0L);

        // Summaries compute once per load — the bounded cost at the
        // retention cap; everything after is sorting and formatting.
        _summaries = new List<TripSummary>(_trips.Count);
        foreach (Trip trip in _trips)
        {
            _summaries.Add(TripSummarizer.Summarize(trip));
        }

        if (_selectedAId is { } a && FindTrip(a) is null)
        {
            _selectedAId = null;
        }

        if (_selectedBId is { } b && FindTrip(b) is null)
        {
            _selectedBId = null;
        }

        _pendingDeleteId = null;
        Render();
    }

    private Trip? FindTrip(int tripId)
    {
        foreach (Trip trip in _trips)
        {
            if (trip.Id == tripId)
            {
                return trip;
            }
        }

        return null;
    }

    private void Render()
    {
        TripHistoryPresenter.ViewModel history = TripHistoryPresenter.Present(
            _summaries, _sortColumn, _sortDescending, _selectedAId, _selectedBId);

        if (_message != null)
        {
            _message.text = history.Empty ? history.Message : string.Empty;
        }

        int shown = Math.Min(history.Rows.Count, VisibleRowCount);
        _visibleTripIds = new int[shown];
        for (int index = 0; index < _rowTexts.Length; index++)
        {
            bool active = index < shown;
            if (_rowTexts[index] != null)
            {
                _rowTexts[index]!.text = active ? history.Rows[index].Text : string.Empty;
            }

            if (active)
            {
                _visibleTripIds[index] = history.Rows[index].TripId;
            }

            SetRowButtonsActive(index, active);
            if (active && _rowDelete[index] != null)
            {
                SetButtonLabel(_rowDelete[index],
                    _pendingDeleteId == history.Rows[index].TripId ? "Sure?" : "X");
            }
        }

        if (_overflow != null)
        {
            int hidden = history.Rows.Count - shown;
            _overflow.text = hidden > 0 ? "… " + hidden + " more — sort to bring them up" : string.Empty;
        }

        RenderBottlenecks(pumpUnused: null);

        TripComparisonPresenter.ViewModel comparison = TripComparisonPresenter.Present(
            _selectedAId is { } aid ? FindTrip(aid) : null,
            _selectedBId is { } bid ? FindTrip(bid) : null);
        if (_compareHeaderA != null)
        {
            _compareHeaderA.text = comparison.HasComparison ? comparison.HeaderA : comparison.Message;
        }

        if (_compareHeaderB != null)
        {
            _compareHeaderB.text = comparison.HasComparison ? comparison.HeaderB : string.Empty;
        }

        for (int index = 0; index < _compareLines.Length; index++)
        {
            if (_compareLines[index] != null)
            {
                _compareLines[index]!.text = comparison.HasComparison && index < comparison.BucketLines.Count
                    ? comparison.BucketLines[index]
                    : string.Empty;
            }
        }
    }

    private static void SetButtonLabel(Button? button, string label)
    {
        if (button == null)
        {
            return;
        }

        Text? text = button.GetComponentInChildren<Text>();
        if (text != null && text.text != label)
        {
            text.text = label;
        }
    }

    private void SetRowButtonsActive(int index, bool active)
    {
        if (_rowSelectA[index] is { } a && a.gameObject.activeSelf != active)
        {
            a.gameObject.SetActive(active);
        }

        if (_rowSelectB[index] is { } b && b.gameObject.activeSelf != active)
        {
            b.gameObject.SetActive(active);
        }

        if (_rowDelete[index] is { } x && x.gameObject.activeSelf != active)
        {
            x.gameObject.SetActive(active);
        }
    }

    private void OnSelect(int rowIndex, bool asA, CartTelemetryPump? pump)
    {
        if (rowIndex >= _visibleTripIds.Length)
        {
            return;
        }

        int tripId = _visibleTripIds[rowIndex];
        if (asA)
        {
            _selectedAId = _selectedAId == tripId ? null : tripId;
        }
        else
        {
            _selectedBId = _selectedBId == tripId ? null : tripId;
        }

        Render();
    }

    private void OnDelete(int rowIndex, CartTelemetryPump? pump)
    {
        if (rowIndex >= _visibleTripIds.Length)
        {
            return;
        }

        int tripId = _visibleTripIds[rowIndex];
        if (_pendingDeleteId != tripId)
        {
            // First press arms the confirmation window.
            _pendingDeleteId = tripId;
            _pendingDeleteUntil = Time.unscaledTimeAsDouble + ConfirmWindowSeconds;
            Render();
            return;
        }

        _pendingDeleteId = null;
        if (pump?.Trips?.DeleteTrip(tripId) == true)
        {
            _log.LogInfo("Trip #" + tripId + " deleted from this world's sidecar.");
        }

        Reload(pump);
    }

    /// <summary>CT-019: bottleneck lines for trip [A] at the entered
    /// hypothetical mass — pure presenter math over already-loaded data.</summary>
    private void RenderBottlenecks(object? pumpUnused)
    {
        Trip? tripA = _selectedAId is { } aid ? FindTrip(aid) : null;
        RouteBottleneckPresenter.ViewModel viewModel = RouteBottleneckPresenter.Present(
            tripA, _segments, _pumpForButtons?.LoadModel,
            _massInput != null ? _massInput.text : null);

        if (_bottleneckHeader != null)
        {
            _bottleneckHeader.text = viewModel.Available ? viewModel.Message : viewModel.Message;
        }

        for (int index = 0; index < _bottleneckLines.Length; index++)
        {
            if (_bottleneckLines[index] != null)
            {
                _bottleneckLines[index]!.text =
                    viewModel.Available && index < viewModel.Lines.Count
                        ? viewModel.Lines[index]
                        : string.Empty;
            }
        }
    }

    private void SetSort(TripHistoryPresenter.SortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = true;
        }

        Render();
    }

    private CartTelemetryPump? _pumpForButtons;

    /// <summary>The pump reference used by row-button callbacks; refreshed
    /// on toggle so callbacks never capture a stale pump.</summary>
    public void BindPump(CartTelemetryPump? pump)
    {
        _pumpForButtons = pump;
    }

    private bool Build()
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
            "Trip History", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        // Sort header buttons.
        (string Label, TripHistoryPresenter.SortColumn Column)[] sorts =
        {
            ("Date", TripHistoryPresenter.SortColumn.StartTime),
            ("Time", TripHistoryPresenter.SortColumn.Duration),
            ("Dist", TripHistoryPresenter.SortColumn.Distance),
            ("Load", TripHistoryPresenter.SortColumn.Load),
            ("Grade", TripHistoryPresenter.SortColumn.WorstGrade),
        };
        float buttonWidth = (PanelWidth - 60f) / sorts.Length;
        float firstCenterX = -(PanelWidth - 60f) / 2f + buttonWidth / 2f;
        for (int index = 0; index < sorts.Length; index++)
        {
            TripHistoryPresenter.SortColumn column = sorts[index].Column;
            GameObject sortButton = gui.CreateButton(
                sorts[index].Label, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(firstCenterX + index * buttonWidth, -62f), buttonWidth - 4f, 26f);
            sortButton.GetComponent<Button>().onClick.AddListener(() => SetSort(column));
        }

        _message = CreateText(gui, font, bodyColor, 0f, -96f, PanelWidth - 40f, RowHeight);

        _rowTexts = new Text?[VisibleRowCount];
        _rowSelectA = new Button?[VisibleRowCount];
        _rowSelectB = new Button?[VisibleRowCount];
        _rowDelete = new Button?[VisibleRowCount];
        float y = -96f;
        float leftEdge = -(PanelWidth / 2f) + 26f;
        for (int index = 0; index < VisibleRowCount; index++)
        {
            int rowIndex = index;
            _rowSelectA[index] = CreateRowButton(gui, "A", leftEdge, y,
                () => OnSelect(rowIndex, asA: true, _pumpForButtons));
            _rowSelectB[index] = CreateRowButton(gui, "B", leftEdge + 30f, y,
                () => OnSelect(rowIndex, asA: false, _pumpForButtons));
            _rowDelete[index] = CreateRowButton(gui, "X", leftEdge + 60f, y,
                () => OnDelete(rowIndex, _pumpForButtons));
            _rowTexts[index] = CreateText(gui, font, bodyColor, 55f, y, PanelWidth - 150f, RowHeight);
            y -= RowHeight;
        }

        _overflow = CreateText(gui, font, bodyColor, 0f, y, PanelWidth - 40f, RowHeight);
        y -= RowHeight + 6f;

        _compareHeaderA = CreateText(gui, font, headerColor, 0f, y, PanelWidth - 40f, RowHeight);
        y -= RowHeight;
        _compareHeaderB = CreateText(gui, font, headerColor, 0f, y, PanelWidth - 40f, RowHeight);
        y -= RowHeight;
        _compareLines = new Text?[TripComparisonPresenter.BucketCount];
        for (int index = 0; index < _compareLines.Length; index++)
        {
            _compareLines[index] = CreateText(gui, font, bodyColor, 0f, y, PanelWidth - 40f, RowHeight);
            y -= RowHeight;
        }

        // CT-019: hypothetical-load bottleneck block.
        y -= 6f;
        GameObject massObject = gui.CreateInputField(
            _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-130f, y - 14f),
            placeholderText: "test mass…", fontSize: 14, width: 150f, height: 26f);
        _massInput = massObject.GetComponent<InputField>();
        if (_massInput != null)
        {
            _massInput.onValueChanged.AddListener(_ => RenderBottlenecks(null));
        }

        _bottleneckHeader = CreateText(gui, font, headerColor, 90f, y, PanelWidth - 220f, RowHeight);
        y -= RowHeight + 4f;
        _bottleneckLines = new Text?[3];
        for (int index = 0; index < _bottleneckLines.Length; index++)
        {
            _bottleneckLines[index] = CreateText(gui, font, bodyColor, 0f, y, PanelWidth - 40f, RowHeight);
            y -= RowHeight;
        }

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), 110f, 28f);
        close.GetComponent<Button>().onClick.AddListener(Hide);

        _panel.SetActive(false);
        return true;
    }

    private Button? CreateRowButton(GUIManager gui, string label, float x, float y, Action onClick)
    {
        GameObject buttonObject = gui.CreateButton(
            label, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(x, y - (RowHeight / 2f)), 26f, 22f);
        Button? button = buttonObject.GetComponent<Button>();
        button?.onClick.AddListener(() => onClick());
        buttonObject.SetActive(false);
        return button;
    }

    private Text? CreateText(
        GUIManager gui, Font font, Color color, float xOffset, float y, float width, float height)
    {
        Text? text = gui.CreateText(
            string.Empty, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(xOffset, y - (height / 2f)),
            font, 14, color, outline: false, Color.black, width, height,
            addContentSizeFitter: false).GetComponent<Text>();
        if (text != null)
        {
            text.alignment = TextAnchor.UpperLeft;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        return text;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        try
        {
            Hide();
        }
        catch
        {
            // Hiding is best-effort; the surface is already disabled.
        }

        _log.LogError(
            "Trip History UI failed and was disabled for this session " +
            $"(recording keeps running): {exception.GetType().Name}: {exception.Message}");
    }
}
