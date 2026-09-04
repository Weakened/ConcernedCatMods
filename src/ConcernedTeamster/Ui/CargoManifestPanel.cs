using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Cargo;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The sortable, filterable cargo manifest surface (CT-007),
/// reached from the Cart Status panel's Manifest button — buttons all the
/// way. Data flows one way: tracker-bounded adapter reads (≤ 1/s) →
/// immutable manifest → headless presenter → text rows; the UI never reads
/// game state directly. View re-renders only when the manifest instance,
/// sort, or filter changes, plus a 1 Hz freshness tick while visible — no
/// per-frame work beyond an Escape check. Fail-closed like every Teamster
/// surface: any UI exception disables the panel for the session with one
/// ERROR line.</summary>
internal sealed class CargoManifestPanel
{
    private const float PanelWidth = 420f;
    private const float PanelHeight = 470f;
    private const float RowHeight = 24f;
    private const int VisibleRowCount = 12;
    private const double ViewTickSeconds = 1.0;

    private readonly ManualLogSource _log;
    private readonly CargoManifestTracker _tracker = new();

    private bool _failed;
    private GameObject? _panel;
    private InputField? _filter;
    private Text?[] _rows = Array.Empty<Text?>();
    private Text? _message;
    private Text? _overflow;
    private Text? _total;
    private Text? _freshness;
    private Button? _sortNameButton;
    private Button? _sortCountButton;
    private Button? _sortUnitButton;
    private Button? _sortLineButton;

    private CargoSortColumn _sortColumn = CargoSortColumn.LineWeight;
    private bool _sortDescending = true;
    private string _lastAppliedFilter = string.Empty;
    private CargoManifest? _lastAppliedManifest;
    private bool _viewDirty = true;
    private double _nextViewTick;

    public CargoManifestPanel(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public void Toggle()
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
                _viewDirty = true;
                _nextViewTick = 0d;
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

    /// <summary>Forgets cached cargo (world switch / cart switch) and hides.</summary>
    public void Reset()
    {
        _tracker.Reset();
        _lastAppliedManifest = null;
        _viewDirty = true;
        Hide();
    }

    /// <summary>Per-frame driver while in a world. Cheap when idle: an
    /// Escape check and one clock comparison.</summary>
    public void HandleFrame(double nowSeconds, string? selectedCartId)
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

            if (!_viewDirty && nowSeconds < _nextViewTick)
            {
                return;
            }

            _nextViewTick = nowSeconds + ViewTickSeconds;

            CargoManifest? manifest = _tracker.GetOrRefresh(
                nowSeconds,
                captureTime => CartAdapter.TryReadManifest(
                    CartAdapter.TryFindCartById(selectedCartId), captureTime));

            string filter = _filter != null ? _filter.text : string.Empty;
            if (!_viewDirty &&
                ReferenceEquals(manifest, _lastAppliedManifest) &&
                string.Equals(filter, _lastAppliedFilter, StringComparison.Ordinal))
            {
                // Same data and view settings: only the freshness line moves.
                if (manifest is not null && _freshness != null)
                {
                    CargoManifestViewModel freshnessOnly = CargoManifestPresenter.Present(
                        manifest, _sortColumn, _sortDescending, filter, nowSeconds,
                        GameLocalization.LocalizeOrRaw);
                    _freshness.text = freshnessOnly.FreshnessLine;
                }

                return;
            }

            _lastAppliedManifest = manifest;
            _lastAppliedFilter = filter;
            _viewDirty = false;

            Render(CargoManifestPresenter.Present(
                manifest, _sortColumn, _sortDescending, filter, nowSeconds,
                GameLocalization.LocalizeOrRaw));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Render(CargoManifestViewModel viewModel)
    {
        if (_message != null)
        {
            _message.text = viewModel.Message;
        }

        int shown = Math.Min(viewModel.Rows.Count, VisibleRowCount);
        for (int index = 0; index < _rows.Length; index++)
        {
            Text? row = _rows[index];
            if (row == null)
            {
                continue;
            }

            if (index < shown)
            {
                CargoRowViewModel rowModel = viewModel.Rows[index];
                row.text =
                    rowModel.Name + "   ×" + rowModel.CountText +
                    "   unit " + rowModel.UnitWeightText +
                    "   line " + rowModel.LineWeightText;
            }
            else
            {
                row.text = string.Empty;
            }
        }

        if (_overflow != null)
        {
            int hidden = viewModel.Rows.Count - shown;
            _overflow.text = hidden > 0
                ? "… " + hidden + " more — sort or filter to narrow"
                : string.Empty;
        }

        if (_total != null)
        {
            _total.text = viewModel.TotalLine;
        }

        if (_freshness != null)
        {
            _freshness.text = viewModel.FreshnessLine;
        }

        UpdateSortButtonLabels();
    }

    private void SetSort(CargoSortColumn column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = column != CargoSortColumn.Name;
        }

        _viewDirty = true;
        UpdateSortButtonLabels();
    }

    private void UpdateSortButtonLabels()
    {
        string arrow = _sortDescending ? " ▼" : " ▲";
        SetButtonLabel(_sortNameButton, "Item" + (_sortColumn == CargoSortColumn.Name ? arrow : string.Empty));
        SetButtonLabel(_sortCountButton, "Count" + (_sortColumn == CargoSortColumn.Count ? arrow : string.Empty));
        SetButtonLabel(_sortUnitButton, "Unit" + (_sortColumn == CargoSortColumn.UnitWeight ? arrow : string.Empty));
        SetButtonLabel(_sortLineButton, "Line" + (_sortColumn == CargoSortColumn.LineWeight ? arrow : string.Empty));
    }

    private static void SetButtonLabel(Button? button, string label)
    {
        if (button == null)
        {
            return;
        }

        Text? text = button.GetComponentInChildren<Text>();
        if (text != null)
        {
            text.text = label;
        }
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
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(PanelWidth / 2f) - 380f, 0f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            "Cargo Manifest", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        GameObject filterObject = gui.CreateInputField(
            _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f),
            placeholderText: "filter items…", fontSize: 15, width: PanelWidth - 60f, height: 28f);
        _filter = filterObject.GetComponent<InputField>();
        if (_filter != null)
        {
            _filter.onValueChanged.AddListener(_ => _viewDirty = true);
        }

        float buttonWidth = (PanelWidth - 60f) / 4f;
        float firstCenterX = -(PanelWidth - 60f) / 2f + buttonWidth / 2f;
        _sortNameButton = CreateSortButton(gui, firstCenterX + 0f * buttonWidth, buttonWidth,
            () => SetSort(CargoSortColumn.Name));
        _sortCountButton = CreateSortButton(gui, firstCenterX + 1f * buttonWidth, buttonWidth,
            () => SetSort(CargoSortColumn.Count));
        _sortUnitButton = CreateSortButton(gui, firstCenterX + 2f * buttonWidth, buttonWidth,
            () => SetSort(CargoSortColumn.UnitWeight));
        _sortLineButton = CreateSortButton(gui, firstCenterX + 3f * buttonWidth, buttonWidth,
            () => SetSort(CargoSortColumn.LineWeight));

        _message = CreateBodyText(gui, font, bodyColor, -128f, RowHeight);

        _rows = new Text?[VisibleRowCount];
        float y = -152f;
        for (int index = 0; index < VisibleRowCount; index++)
        {
            _rows[index] = CreateBodyText(gui, font, bodyColor, y, RowHeight);
            y -= RowHeight;
        }

        _overflow = CreateBodyText(gui, font, bodyColor, y, RowHeight);
        _total = CreateBodyText(gui, font, headerColor, y - RowHeight, RowHeight);
        _freshness = CreateBodyText(gui, font, bodyColor, y - 2f * RowHeight, RowHeight);

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), 110f, 28f);
        close.GetComponent<Button>().onClick.AddListener(Hide);

        UpdateSortButtonLabels();
        _panel.SetActive(false);
        return true;
    }

    private Button? CreateSortButton(GUIManager gui, float centerX, float width, Action onClick)
    {
        GameObject buttonObject = gui.CreateButton(
            string.Empty, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(centerX, -98f),
            width - 4f, 26f);
        Button? button = buttonObject.GetComponent<Button>();
        button?.onClick.AddListener(() => onClick());
        return button;
    }

    private Text? CreateBodyText(GUIManager gui, Font font, Color color, float y, float height)
    {
        Text? text = gui.CreateText(
            string.Empty, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - (height / 2f)),
            font, 15, color, outline: false, Color.black, PanelWidth - 40f, height,
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
            "Cargo Manifest UI failed and was disabled for this session " +
            $"(telemetry keeps running): {exception.GetType().Name}: {exception.Message}");
    }
}
