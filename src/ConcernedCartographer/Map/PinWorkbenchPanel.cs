using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Pin Workbench map panel: a Valheim-styled inspector built
/// from Jötunn's verified GUI factory, driven entirely by the unit-tested
/// PinWorkbenchController. Three modes: edit (managed pin), adopt prompt
/// (unadopted vanilla pin), and read-only (foreign/system pin). Fields are
/// laid out as a linear top-to-bottom chain for controller navigation.
/// Fail-closed: any UI exception hides the panel and leaves the console
/// workbench as the fallback; store data can never be harmed because every
/// write goes through the controller's validated single-batch Apply.</summary>
internal sealed class PinWorkbenchPanel
{
    private const float PanelWidth = 400f;
    private const float PanelHeight = 640f;

    private readonly ManualLogSource _log;
    private readonly PinWorkbenchController _controller = new();

    // Jötunn's BlockInput is reference-counted, so the panel must hold at
    // most ONE outstanding request for its whole modal lifetime no matter
    // how many times it transitions (adopt prompt → managed editor re-shows
    // without hiding). All block traffic goes through this owner.
    private readonly ModalInputBlock _inputBlock = new(GUIManager.BlockInput);

    private GameObject? _panel;
    private Text? _title;
    private Text? _info;
    private InputField? _name;
    private InputField? _icon;
    private InputField? _category;
    private InputField? _color;
    private InputField? _size;
    private InputField? _tags;
    private InputField? _notes;
    private Toggle? _checked;
    private Text? _statusLabel;
    private Text? _scopeLabel;
    private GameObject? _editRows;
    private GameObject? _adoptButton;

    private PinOperations? _operations;
    private Action? _onApplied;
    private Func<AtlasPin?>? _adopt;
    private bool _failed;

    public PinWorkbenchPanel(ManualLogSource log)
    {
        _log = log;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public void OpenForManaged(AtlasPin pin, PinOperations operations, Action onApplied)
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _operations = operations;
            _onApplied = onApplied;
            _adopt = null;
            _controller.Open(pin);
            LoadBufferIntoWidgets();
            SetMode(edit: true, adopt: false);
            _title!.text = pin.Name.Length == 0 ? AtlasStrings.Get("workbench.title") : $"Pin: {Truncate(pin.Name, 24)}";
            _info!.text = _controller.InfoLine;
            Show(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void OpenAdoptPrompt(string pinName, Func<AtlasPin?> adopt, PinOperations operations, Action onApplied)
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _operations = operations;
            _onApplied = onApplied;
            _adopt = adopt;
            SetMode(edit: false, adopt: true);
            string vanillaLabel = AtlasStrings.Get("workbench.vanillaPin");
            _title!.text = pinName.Length == 0 ? vanillaLabel : $"{vanillaLabel}: {Truncate(pinName, 20)}";
            _info!.text = AtlasStrings.Get("workbench.adoptInfo");
            Show(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void OpenReadOnly(string description)
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            _operations = null;
            _adopt = null;
            SetMode(edit: false, adopt: false);
            _title!.text = AtlasStrings.Get("workbench.foreignPin");
            _info!.text = description + " — " + AtlasStrings.Get("workbench.foreignInfo");
            Show(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void Close()
    {
        _controller.Cancel();
        Show(false);
    }

    /// <summary>Per-frame housekeeping from the runtime. Escape closes; a
    /// large map that disappeared under the panel (death, another mod,
    /// world teardown) force-closes it; and a fail-safe invariant makes
    /// sure a hidden workbench can never keep holding the global input
    /// block. Runs every frame, gated only on plugin disposal.</summary>
    public void HandleFrame()
    {
        if (IsVisible)
        {
            if (!Minimap.IsOpen())
            {
                // The panel lives inside the open large map; if the map
                // went away underneath it, fail closed with it.
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }

            return;
        }

        if (_inputBlock.Owned)
        {
            _log.LogError(
                "Workbench invariant violated: the panel is hidden but still owned the GUI input block; releasing it now.");
            _inputBlock.Release();
        }
    }

    private void ApplyClicked()
    {
        try
        {
            if (_operations is null)
            {
                Close();
                return;
            }

            ReadWidgetsIntoBuffer();
            if (_controller.TryApply(_operations, out string message))
            {
                _log.LogInfo($"Workbench: {message}");
                Show(false);
                _onApplied?.Invoke();
            }
            else
            {
                _info!.text = message;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void DeleteClicked()
    {
        try
        {
            if (_operations is not null && _operations.Delete(_controller.TargetId))
            {
                _log.LogInfo($"Workbench: deleted {_controller.TargetId}.");
                Show(false);
                _onApplied?.Invoke();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void AdoptClicked()
    {
        try
        {
            AtlasPin? adopted = _adopt?.Invoke();
            if (adopted is null)
            {
                _info!.text = "Adoption failed; see the log.";
                return;
            }

            OpenForManaged(adopted, _operations!, _onApplied ?? (() => { }));
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void LoadBufferIntoWidgets()
    {
        _name!.text = _controller.NameField;
        _icon!.text = _controller.IconField;
        _category!.text = _controller.CategoryField;
        _color!.text = _controller.ColorField;
        _size!.text = _controller.SizeField;
        _tags!.text = _controller.TagsField;
        _notes!.text = _controller.NotesField;
        _checked!.isOn = _controller.CheckedField;
        _statusLabel!.text = AtlasStrings.Get("workbench.status") + ": " + _controller.StatusField;
        _scopeLabel!.text = AtlasStrings.Get("workbench.scope") + ": " + _controller.ScopeField;
    }

    private void ReadWidgetsIntoBuffer()
    {
        _controller.NameField = _name!.text;
        _controller.IconField = _icon!.text;
        _controller.CategoryField = _category!.text;
        _controller.ColorField = _color!.text;
        _controller.SizeField = _size!.text;
        _controller.TagsField = _tags!.text;
        _controller.NotesField = _notes!.text;
        _controller.CheckedField = _checked!.isOn;
        // Status and scope are already in the controller via the cycle buttons.
    }

    private void SetMode(bool edit, bool adopt)
    {
        _editRows!.SetActive(edit);
        _adoptButton!.SetActive(adopt);
    }

    /// <summary>Accessibility scale applied when the panel shows.</summary>
    public float UiScale = 1f;

    private void Show(bool visible)
    {
        if (_panel == null)
        {
            return;
        }

        _panel.transform.localScale = Vector3.one * UiScale;
        _panel.SetActive(visible);
        if (visible)
        {
            _inputBlock.Acquire();

            // Controller entry point: focus the first field so navigation
            // can walk the chain.
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(
                _adoptButton != null && _adoptButton.activeSelf ? _adoptButton : _name?.gameObject);
        }
        else
        {
            _inputBlock.Release();
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
            _log.LogWarning("Workbench UI is unavailable (no GUI root yet); use the cc_pins console instead.");
            return false;
        }

        try
        {
            BuildPanel();
            return _panel != null;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private void BuildPanel()
    {
        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        var labelColor = new Color(0.9f, 0.8f, 0.6f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront!.transform,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(-230f, 0f),
            PanelWidth,
            PanelHeight,
            draggable: true);

        _title = gui.CreateText(
            AtlasStrings.Get("workbench.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f),
            font, 20, labelColor, outline: true, Color.black, 360f, 32f, addContentSizeFitter: false)
            .GetComponent<Text>();

        _info = gui.CreateText(
            "", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f),
            font, 12, Color.white, outline: false, Color.black, 370f, 40f, addContentSizeFitter: false)
            .GetComponent<Text>();

        _editRows = new GameObject("CCEditRows", typeof(RectTransform));
        _editRows.transform.SetParent(_panel.transform, worldPositionStays: false);
        var editRect = (RectTransform)_editRows.transform;
        editRect.anchorMin = Vector2.zero;
        editRect.anchorMax = Vector2.one;
        editRect.offsetMin = Vector2.zero;
        editRect.offsetMax = Vector2.zero;

        float y = -95f;
        _name = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.name"), ref y);
        _icon = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.icon"), ref y);
        _category = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.category"), ref y);
        _color = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.color"), ref y);
        _size = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.size"), ref y);
        _tags = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.tags"), ref y);

        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.notes"), new Vector2(-150f, y));
        GameObject notesField = gui.CreateInputField(
            _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(60f, y - 22f),
            InputField.ContentType.Standard, "notes", 13, 240f, 74f);
        _notes = notesField.GetComponent<InputField>();
        _notes.lineType = InputField.LineType.MultiLineNewline;
        y -= 92f;

        GameObject statusButton = gui.CreateButton(
            "Status", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-95f, y), 170f, 30f);
        _statusLabel = statusButton.GetComponentInChildren<Text>();
        statusButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            _statusLabel!.text = AtlasStrings.Get("workbench.status") + ": " + _controller.CycleStatus();
        });

        GameObject scopeButton = gui.CreateButton(
            "Scope", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(95f, y), 170f, 30f);
        _scopeLabel = scopeButton.GetComponentInChildren<Text>();
        scopeButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            _scopeLabel!.text = AtlasStrings.Get("workbench.scope") + ": " + _controller.CycleScope();
        });
        y -= 38f;

        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.checked"), new Vector2(-150f, y));
        GameObject toggle = gui.CreateToggle(_editRows.transform, 28f, 28f);
        var toggleRect = (RectTransform)toggle.transform;
        toggleRect.anchorMin = new Vector2(0.5f, 1f);
        toggleRect.anchorMax = new Vector2(0.5f, 1f);
        toggleRect.anchoredPosition = new Vector2(-40f, y);
        _checked = toggle.GetComponentInChildren<Toggle>();
        y -= 40f;

        GameObject apply = gui.CreateButton(
            AtlasStrings.Get("workbench.apply"), _editRows.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, 40f), 110f, 36f);
        apply.GetComponent<Button>().onClick.AddListener(ApplyClicked);

        GameObject deleteButton = gui.CreateButton(
            AtlasStrings.Get("workbench.delete"), _editRows.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(120f, 40f), 110f, 36f);
        deleteButton.GetComponent<Button>().onClick.AddListener(DeleteClicked);

        _adoptButton = gui.CreateButton(
            AtlasStrings.Get("workbench.adopt"), _panel.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), 220f, 40f);
        _adoptButton.GetComponent<Button>().onClick.AddListener(AdoptClicked);

        GameObject cancel = gui.CreateButton(
            AtlasStrings.Get("workbench.close"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), 110f, 36f);
        cancel.GetComponent<Button>().onClick.AddListener(Close);

        _panel.SetActive(false);
    }

    private InputField? CreateRow(GUIManager gui, Font font, Color labelColor, string label, ref float y)
    {
        CreateLabel(gui, font, labelColor, label, new Vector2(-150f, y));
        GameObject field = gui.CreateInputField(
            _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(60f, y),
            InputField.ContentType.Standard, label, 14, 240f, 28f);
        y -= 36f;
        return field.GetComponent<InputField>();
    }

    private void CreateLabel(GUIManager gui, Font font, Color color, string text, Vector2 position)
    {
        gui.CreateText(
            text, _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position,
            font, 13, color, outline: false, Color.black, 130f, 26f, addContentSizeFitter: false);
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        // Release only a block this panel actually owns: an unconditional
        // BlockInput(false) here could steal another mod's request.
        _inputBlock.Release();
        _log.LogError($"Workbench panel failed and was disabled for this session (cc_pins console remains available): {exception}");
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }
}
