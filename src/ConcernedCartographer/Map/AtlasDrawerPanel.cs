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
    private const int ResultSlots = 6;
    private const int ViewSlots = 5;

    private readonly ManualLogSource _log;

    public Action<bool>? DirtToggled;
    public Action<bool>? PavedToggled;
    public Action<bool>? PinsToggled;
    public Action<bool>? ClusterToggled;
    public Action<string>? QueryApplied;
    public Action<string>? ViewSaved;
    public Action<string>? ViewApplied;
    public Action<AtlasId>? ResultClicked;
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
            _panel!.SetActive(true);
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

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront!.transform,
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(210f, 0f), 380f, 680f, draggable: true);

        gui.CreateText("Atlas", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 20, header, true, Color.black, 340f, 30f, false);

        float y = -62f;
        gui.CreateText("Layers", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-130f, y),
            font, 15, header, false, Color.black, 120f, 24f, false);
        y -= 30f;

        _dirt = CreateToggleRow(gui, font, "Dirt roads", ref y, value => { if (!_suppressToggleEvents) DirtToggled?.Invoke(value); });
        _paved = CreateToggleRow(gui, font, "Paved roads", ref y, value => { if (!_suppressToggleEvents) PavedToggled?.Invoke(value); });
        _pins = CreateToggleRow(gui, font, "Pins", ref y, value => { if (!_suppressToggleEvents) { PinsToggled?.Invoke(value); RefreshLists(); } });
        _cluster = CreateToggleRow(gui, font, "Clustering", ref y, value => { if (!_suppressToggleEvents) { ClusterToggled?.Invoke(value); RefreshLists(); } });
        y -= 8f;

        gui.CreateText("Search", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-125f, y),
            font, 15, header, false, Color.black, 120f, 24f, false);
        y -= 30f;

        _query = gui.CreateInputField(
            _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-35f, y), InputField.ContentType.Standard, "name, tag:iron, near:…", 13, 250f, 28f)
            .GetComponent<InputField>();
        GameObject go = gui.CreateButton("Go", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(135f, y), 55f, 28f);
        go.GetComponent<Button>().onClick.AddListener(() =>
        {
            QueryApplied?.Invoke(_query!.text);
            RefreshLists();
        });
        y -= 34f;

        GameObject clear = gui.CreateButton("Clear filter", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-95f, y), 130f, 26f);
        clear.GetComponent<Button>().onClick.AddListener(() =>
        {
            _query!.text = "";
            QueryApplied?.Invoke("");
            RefreshLists();
        });

        _status = gui.CreateText("", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(70f, y),
            font, 12, Color.white, false, Color.black, 200f, 26f, false)
            .GetComponent<Text>();
        y -= 32f;

        for (int index = 0; index < ResultSlots; index++)
        {
            int captured = index;
            GameObject result = gui.CreateButton("", _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), 330f, 26f);
            _resultButtons[index] = result.GetComponent<Button>();
            _resultLabels[index] = result.GetComponentInChildren<Text>();
            _resultButtons[index].onClick.AddListener(() => ResultClicked?.Invoke(_resultIds[captured]));
            result.SetActive(false);
            y -= 30f;
        }

        y -= 6f;
        gui.CreateText("Views", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-129f, y),
            font, 15, header, false, Color.black, 120f, 24f, false);
        y -= 30f;

        _viewName = gui.CreateInputField(
            _panel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-35f, y), InputField.ContentType.Standard, "view name", 13, 250f, 28f)
            .GetComponent<InputField>();
        GameObject save = gui.CreateButton("Save", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(135f, y), 55f, 28f);
        save.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (_viewName!.text.Trim().Length > 0)
            {
                ViewSaved?.Invoke(_viewName.text.Trim());
                RefreshLists();
            }
        });
        y -= 34f;

        for (int index = 0; index < ViewSlots; index++)
        {
            int captured = index;
            GameObject view = gui.CreateButton("", _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), 330f, 26f);
            _viewButtons[index] = view.GetComponent<Button>();
            _viewLabels[index] = view.GetComponentInChildren<Text>();
            _viewButtons[index].onClick.AddListener(() =>
            {
                ViewApplied?.Invoke(_viewLabels[captured].text);
                RefreshLists();
            });
            view.SetActive(false);
            y -= 30f;
        }

        gui.CreateText("Routes — arrives in v0.5   ·   Sharing — arrives in v0.6",
            _panel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f),
            font, 11, new Color(1f, 1f, 1f, 0.6f), false, Color.black, 360f, 22f, false);

        _panel.SetActive(false);
    }

    private Toggle CreateToggleRow(GUIManager gui, Font font, string label, ref float y, Action<bool> changed)
    {
        gui.CreateText(label, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-110f, y),
            font, 13, Color.white, false, Color.black, 160f, 24f, false);
        GameObject toggle = gui.CreateToggle(_panel.transform, 26f, 26f);
        var rect = (RectTransform)toggle.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(60f, y);
        Toggle component = toggle.GetComponentInChildren<Toggle>();
        component.onValueChanged.AddListener(value => changed(value));
        y -= 30f;
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
