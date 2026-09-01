using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The unified Atlas Drawer: layers, search, and saved views in
/// one Valheim-styled left-hand panel over the large map, with Routes and
/// Share sections reserved for their sprints. All behavior is delegated to
/// the runtime through assignable callbacks, so the panel is a thin,
/// fail-closed veneer over tested logic; the cc_atlas console drives the
/// same callbacks without UI. Vertical top-to-bottom layout keeps it
/// controller-navigable.</summary>
internal sealed class AtlasDrawerPanel
{
    private const int ResultSlots = 5;
    private const int ViewSlots = 4;

    // RC8-3 explicit layout grid (the DEF-v1.0-003 workbench discipline):
    // every row is derived from these constants —
    // | EdgePadding | content column(s) | EdgePadding |
    // — and the vertical budget is accounted below, so no label, field,
    // count, or footer control can overlap another or leave the panel.
    // Vertical budget: 62 title + 28 hdr + 4·28 toggles + 6 + 28 hdr +
    // 32 search + 30 clear/status + 5·28 results + 6 + 28 hdr + 32 save +
    // 4·28 views = 616, footer reserve 78 → 694 ≤ PanelHeight 700.
    private const float PanelWidth = 380f;
    private const float PanelHeight = 700f;
    private const float EdgePadding = 22f;
    private const float ContentWidth = PanelWidth - (2f * EdgePadding);
    private const float LeftEdge = (-PanelWidth / 2f) + EdgePadding;
    private const float ToggleLabelWidth = 210f;
    private const float ColumnGap = 8f;
    private const float ActionButtonWidth = 56f;
    private const float RowHeight = 28f;
    private const float ClearButtonWidth = 120f;

    private readonly ManualLogSource _log;

    public Action<bool>? DirtToggled;
    public Action<bool>? PavedToggled;
    public Action<bool>? PinsToggled;
    public Action<bool>? ClusterToggled;
    public Action<string>? QueryApplied;
    public Action<string>? ViewSaved;
    public Action<string>? ViewApplied;
    public Action<AtlasId>? ResultClicked;
    public Action? PrivacyClicked;
    public Action? SystemMarkersClicked;
    public Func<string>? StatusLine;
    public Func<List<(string Label, AtlasId Id)>>? TopResults;
    public Func<List<string>>? ViewNames;

    private GameObject? _panel;
    private Toggle? _dirt;
    private Toggle? _paved;
    private Toggle? _pins;
    private Toggle? _cluster;
    private InputField? _query;
    private InputField? _viewName;
    private Text? _status;
    private readonly Button[] _resultButtons = new Button[ResultSlots];
    private readonly Text[] _resultLabels = new Text[ResultSlots];
    private readonly AtlasId[] _resultIds = new AtlasId[ResultSlots];
    private readonly Button[] _viewButtons = new Button[ViewSlots];
    private readonly Text[] _viewLabels = new Text[ViewSlots];
    private bool _failed;
    private bool _suppressToggleEvents;

    public AtlasDrawerPanel(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    /// <summary>True after a UI failure disabled the drawer for this
    /// session. The drawer is the only route to Atlas → System Markers, so
    /// owners restore the vanilla rail when it fails (#99).</summary>
    public bool HasFailed => _failed;

    /// <summary>Accessibility scale applied when the drawer shows.</summary>
    public float UiScale = 1f;

    public void Toggle(bool showDirt, bool showPaved, bool showPins, bool cluster)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _suppressToggleEvents = true;
            _dirt!.isOn = showDirt;
            _paved!.isOn = showPaved;
            _pins!.isOn = showPins;
            _cluster!.isOn = cluster;
            _suppressToggleEvents = false;
            RefreshLists();
            _panel!.transform.localScale = Vector3.one * UiScale;
            ((RectTransform)_panel.transform).anchoredPosition =
                new Vector2(-((PanelWidth * UiScale) / 2f) - 30f, 0f);
            _panel.SetActive(true);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(
                _dirt != null ? _dirt.gameObject : null);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void Hide()
    {
        if (_panel != null)
        {
            _panel.SetActive(false);
        }
    }

    /// <summary>Refreshes status text, result rows, and view rows.</summary>
    public void RefreshLists()
    {
        if (!IsVisible && _panel == null)
        {
            return;
        }

        try
        {
            _status!.text = StatusLine?.Invoke() ?? "";

            List<(string Label, AtlasId Id)> results = TopResults?.Invoke() ?? new List<(string, AtlasId)>();
            for (int index = 0; index < ResultSlots; index++)
            {
                bool used = index < results.Count;
                _resultButtons[index].gameObject.SetActive(used);
                if (used)
                {
                    _resultLabels[index].text = Truncate(results[index].Label, 30);
                    _resultIds[index] = results[index].Id;
                }
            }

            List<string> views = ViewNames?.Invoke() ?? new List<string>();
            for (int index = 0; index < ViewSlots; index++)
            {
                bool used = index < views.Count;
                _viewButtons[index].gameObject.SetActive(used);
                if (used)
                {
                    _viewLabels[index].text = Truncate(views[index], 26);
                }
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void HandleFrame()
    {
        if (IsVisible && Input.GetKeyDown(KeyCode.Escape))
        {
            Hide();
        }
    }

    private bool EnsureBuilt()
    {
        if (_failed)
        {
            return false;
        }

        if (_panel != null)
        {
            return true;
        }

        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            _log.LogWarning("Atlas drawer UI is unavailable (no GUI root yet); use the cc_atlas console instead.");
            return false;
        }

        try
        {
            Build();
            return _panel != null;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private void Build()
    {
        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        var header = new Color(0.9f, 0.8f, 0.6f, 1f);

        // Shared right-edge dock (#100): the same placement reference as
        // the Pin Workbench and every other CC side panel.
        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront!.transform,
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-((PanelWidth / 2f) + 30f), 0f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(AtlasStrings.Get("drawer.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 20, header, true, Color.black, ContentWidth, 30f, false);

        float y = -62f;
        AddSectionHeader(gui, font, header, AtlasStrings.Get("drawer.layers"), ref y);

        _dirt = CreateToggleRow(gui, font, AtlasStrings.Get("drawer.dirtRoads"), ref y, value => { if (!_suppressToggleEvents) DirtToggled?.Invoke(value); });
        _paved = CreateToggleRow(gui, font, AtlasStrings.Get("drawer.pavedRoads"), ref y, value => { if (!_suppressToggleEvents) PavedToggled?.Invoke(value); });
        _pins = CreateToggleRow(gui, font, AtlasStrings.Get("drawer.pins"), ref y, value => { if (!_suppressToggleEvents) { PinsToggled?.Invoke(value); RefreshLists(); } });
        _cluster = CreateToggleRow(gui, font, AtlasStrings.Get("drawer.clustering"), ref y, value => { if (!_suppressToggleEvents) { ClusterToggled?.Invoke(value); RefreshLists(); } });
        y -= 6f;

        AddSectionHeader(gui, font, header, AtlasStrings.Get("drawer.search"), ref y);

        // Search row: | input (flex) | gap | Go |, all inside the grid.
        float inputWidth = ContentWidth - ColumnGap - ActionButtonWidth;
        _query = gui.CreateInputField(
            _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + (inputWidth / 2f), y), InputField.ContentType.Standard, "name, tag:iron, near:…", 13, inputWidth, 28f)
            .GetComponent<InputField>();
        GameObject go = gui.CreateButton(AtlasStrings.Get("drawer.go"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + inputWidth + ColumnGap + (ActionButtonWidth / 2f), y), ActionButtonWidth, 28f);
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            QueryApplied?.Invoke(_query!.text);
            RefreshLists();
        });
        y -= 32f;

        // Clear + live counts share one row in two DERIVED columns:
        // | Clear (fixed) | gap | status text (rest, truncating) | — the
        // two rects cannot meet, and long counts truncate inside their box.
        GameObject clear = gui.CreateButton(AtlasStrings.Get("drawer.clearFilter"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + (ClearButtonWidth / 2f), y), ClearButtonWidth, 26f);
        clear.GetComponent<Button>().onClick.AddListener(() =>
        {
            _query!.text = "";
            QueryApplied?.Invoke("");
            RefreshLists();
        });

        float statusWidth = ContentWidth - ClearButtonWidth - ColumnGap;
        _status = gui.CreateText("", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + ClearButtonWidth + ColumnGap + (statusWidth / 2f), y),
            font, 11, Color.white, false, Color.black, statusWidth, 26f, false)
            .GetComponent<Text>();
        _status.alignment = TextAnchor.MiddleLeft;
        _status.horizontalOverflow = HorizontalWrapMode.Wrap;
        _status.verticalOverflow = VerticalWrapMode.Truncate;
        y -= 30f;

        for (int index = 0; index < ResultSlots; index++)
        {
            int captured = index;
            GameObject result = gui.CreateButton("", _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), ContentWidth, 26f);
            _resultButtons[index] = result.GetComponent<Button>();
            _resultLabels[index] = result.GetComponentInChildren<Text>();
            _resultButtons[index].onClick.AddListener(() => ResultClicked?.Invoke(_resultIds[captured]));
            result.SetActive(false);
            y -= RowHeight;
        }

        y -= 6f;
        AddSectionHeader(gui, font, header, AtlasStrings.Get("drawer.views"), ref y);

        _viewName = gui.CreateInputField(
            _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + (inputWidth / 2f), y), InputField.ContentType.Standard, "view name", 13, inputWidth, 28f)
            .GetComponent<InputField>();
        GameObject save = gui.CreateButton(AtlasStrings.Get("drawer.save"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + inputWidth + ColumnGap + (ActionButtonWidth / 2f), y), ActionButtonWidth, 28f);
        save.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_viewName!.text.Trim().Length > 0)
            {
                ViewSaved?.Invoke(_viewName.text.Trim());
                RefreshLists();
            }
        });
        y -= 32f;

        for (int index = 0; index < ViewSlots; index++)
        {
            int captured = index;
            GameObject view = gui.CreateButton("", _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), ContentWidth, 26f);
            _viewButtons[index] = view.GetComponent<Button>();
            _viewLabels[index] = view.GetComponentInChildren<Text>();
            _viewButtons[index].onClick.AddListener(() =>
            {
                ViewApplied?.Invoke(_viewLabels[captured].text);
                RefreshLists();
            });
            view.SetActive(false);
            y -= RowHeight;
        }

        // Footer rows, bottom-anchored: hint text above the two buttons.
        gui.CreateText(AtlasStrings.Get("drawer.placeholders"),
            _panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            font, 11, new Color(1f, 1f, 1f, 0.6f), false, Color.black, ContentWidth, 22f, false);

        // Settings → Privacy (#97) and Atlas → System Markers (#99): the
        // two footer buttons split the content width evenly.
        float footerHalf = (ContentWidth - ColumnGap) / 2f;
        GameObject privacy = gui.CreateButton(AtlasStrings.Get("drawer.privacy"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(LeftEdge + (footerHalf / 2f), 52f), footerHalf, 26f);
        privacy.GetComponent<Button>().onClick.AddListener(() => PrivacyClicked?.Invoke());
        GameObject systemMarkers = gui.CreateButton(AtlasStrings.Get("system.title"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(LeftEdge + footerHalf + ColumnGap + (footerHalf / 2f), 52f), footerHalf, 26f);
        systemMarkers.GetComponent<Button>().onClick.AddListener(() => SystemMarkersClicked?.Invoke());

        _panel.SetActive(false);
    }

    private void AddSectionHeader(GUIManager gui, Font font, Color color, string text, ref float y)
    {
        Text headerText = gui.CreateText(text, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(LeftEdge + (ContentWidth / 2f), y),
            font, 15, color, false, Color.black, ContentWidth, 24f, false)
            .GetComponent<Text>();
        headerText.alignment = TextAnchor.MiddleLeft;
        y -= RowHeight;
    }

    private Toggle CreateToggleRow(GUIManager gui, Font font, string label, ref float y, Action<bool> changed)
    {
        // | label (ToggleLabelWidth, left) | gap | toggle | inside the grid.
        Text labelText = gui.CreateText(label, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(LeftEdge + (ToggleLabelWidth / 2f), y),
            font, 13, Color.white, false, Color.black, ToggleLabelWidth, 24f, false)
            .GetComponent<Text>();
        labelText.alignment = TextAnchor.MiddleLeft;
        GameObject toggle = gui.CreateToggle(_panel.transform, 26f, 26f);
        var rect = (RectTransform)toggle.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(LeftEdge + ToggleLabelWidth + ColumnGap + 13f, y);
        Toggle component = toggle.GetComponentInChildren<Toggle>();
        component.onValueChanged.AddListener(value => changed(value));
        y -= RowHeight;
        return component;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError($"Atlas drawer failed and was disabled for this session (cc_atlas console remains available): {exception}");
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
