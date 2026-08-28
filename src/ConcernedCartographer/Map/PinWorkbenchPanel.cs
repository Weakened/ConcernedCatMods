using System;
using System.Collections.Generic;
using System.Globalization;
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
///
/// Visual properties use pickers instead of developer free-text (#94):
/// icon = sprite preview + dropdown list over the stable IconRegistry IDs
/// (unknown legacy IDs preserved and offered as "Keep custom"), category =
/// free text + suggestion dropdown, size = stepper. Pin color is not map-
/// rendered in v1, so it lives at the bottom labeled metadata-only.
///
/// Fail-closed: any UI exception hides the panel and leaves the console
/// workbench as the fallback; store data can never be harmed because every
/// write goes through the controller's validated single-batch Apply.</summary>
internal sealed class PinWorkbenchPanel
{
    private const float PanelWidth = 460f;
    private const float PanelHeight = 640f;

    // Explicit two-column edit layout (DEF-v1.0-003). Every row is derived
    // from these constants, so no label or control can leave the panel:
    // | EdgePadding | label column | ColumnGap | field column | EdgePadding |
    private const float EdgePadding = 24f;
    private const float LabelColumnWidth = 150f;
    private const float ColumnGap = 12f;
    private const float FieldColumnWidth = PanelWidth - (2f * EdgePadding) - LabelColumnWidth - ColumnGap;
    private const float LabelCenterX = (-PanelWidth / 2f) + EdgePadding + (LabelColumnWidth / 2f);
    private const float FieldCenterX = LabelCenterX + (LabelColumnWidth / 2f) + ColumnGap + (FieldColumnWidth / 2f);
    private const float ContentHalfWidth = (PanelWidth / 2f) - EdgePadding;
    private const float FieldLeftEdge = FieldCenterX - (FieldColumnWidth / 2f);

    // The scaled panel re-docks this far from the screen's right edge so
    // it stays fully on screen at every configured UiScale.
    private const float ScreenEdgeMargin = 30f;

    private const float SizeStep = 0.25f;
    private const float SizeMin = 0.5f;
    private const float SizeMax = 2f;

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
    private InputField? _category;
    private InputField? _color;
    private InputField? _tags;
    private InputField? _notes;
    private Toggle? _checked;
    private Text? _statusLabel;
    private Text? _scopeLabel;
    private GameObject? _editRows;
    private GameObject? _adoptButton;

    private Image? _iconPreview;
    private Text? _iconButtonLabel;
    private GameObject? _iconDropdown;
    private GameObject? _categoryDropdown;
    private Text? _sizeValueLabel;
    private float _iconRowY;
    private float _categoryRowY;

    /// <summary>The stable registry ID (or preserved legacy/custom ID) the
    /// picker currently shows; written back verbatim on Apply.</summary>
    private string _selectedIconId = IconRegistry.DefaultIconId;

    /// <summary>Set when the opened pin carries an ID the registry does not
    /// know: the dropdown then offers "Keep custom" so the identity is
    /// never lost by merely opening the editor.</summary>
    private string? _customIconId;

    private float _sizeValue = 1f;

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
        _category!.text = _controller.CategoryField;
        _color!.text = _controller.ColorField;
        _tags!.text = _controller.TagsField;
        _notes!.text = _controller.NotesField;
        _checked!.isOn = _controller.CheckedField;
        _statusLabel!.text = AtlasStrings.Get("workbench.status") + ": " + _controller.StatusField;
        _scopeLabel!.text = AtlasStrings.Get("workbench.scope") + ": " + _controller.ScopeField;

        _selectedIconId = _controller.IconField.Trim().Length == 0
            ? IconRegistry.DefaultIconId
            : _controller.IconField.Trim();
        _customIconId = IconRegistry.TryResolve(_selectedIconId, out _) ? null : _selectedIconId;
        UpdateIconWidgets();

        _sizeValue = float.TryParse(
            _controller.SizeField.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSize)
            ? Mathf.Clamp(parsedSize, SizeMin, SizeMax)
            : 1f;
        UpdateSizeLabel();
    }

    private void ReadWidgetsIntoBuffer()
    {
        _controller.NameField = _name!.text;
        _controller.IconField = _selectedIconId;
        _controller.CategoryField = _category!.text;
        _controller.ColorField = _color!.text;
        _controller.SizeField = _sizeValue.ToString("0.##", CultureInfo.InvariantCulture);
        _controller.TagsField = _tags!.text;
        _controller.NotesField = _notes!.text;
        _controller.CheckedField = _checked!.isOn;
        // Status and scope are already in the controller via the cycle buttons.
    }

    private void SetMode(bool edit, bool adopt)
    {
        CloseDropdowns();
        _editRows!.SetActive(edit);
        _adoptButton!.SetActive(adopt);
    }

    /// <summary>Accessibility scale applied when the panel shows.</summary>
    public float UiScale = 1f;

    private float _appliedScale = 1f;

    private void Show(bool visible)
    {
        if (_panel == null)
        {
            return;
        }

        CloseDropdowns();
        if (visible && !Mathf.Approximately(_appliedScale, UiScale))
        {
            // A scale change re-docks the panel at the default position so
            // the resized panel stays fully on screen; the user can still
            // drag it afterwards.
            _appliedScale = UiScale;
            _panel.transform.localScale = Vector3.one * UiScale;
            ((RectTransform)_panel.transform).anchoredPosition = DefaultAnchoredPosition(UiScale);
        }

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
            DefaultAnchoredPosition(1f),
            PanelWidth,
            PanelHeight,
            draggable: true);

        _title = gui.CreateText(
            AtlasStrings.Get("workbench.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f),
            font, 20, labelColor, outline: true, Color.black, ContentHalfWidth * 2f, 32f, addContentSizeFitter: false)
            .GetComponent<Text>();

        _info = gui.CreateText(
            "", _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f),
            font, 12, Color.white, outline: false, Color.black, ContentHalfWidth * 2f, 40f, addContentSizeFitter: false)
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
        BuildIconRow(gui, font, labelColor, ref y);
        BuildCategoryRow(gui, font, labelColor, ref y);
        BuildSizeRow(gui, font, labelColor, ref y);
        _tags = CreateRow(gui, font, labelColor, AtlasStrings.Get("workbench.tags"), ref y);

        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.notes"), new Vector2(LabelCenterX, y));
        GameObject notesField = gui.CreateInputField(
            _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(FieldCenterX, y - 22f),
            InputField.ContentType.Standard, "notes", 13, FieldColumnWidth, 74f);
        _notes = notesField.GetComponent<InputField>();
        _notes.lineType = InputField.LineType.MultiLineNewline;
        y -= 92f;

        const float cycleButtonWidth = 190f;
        GameObject statusButton = gui.CreateButton(
            "Status", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(-ContentHalfWidth + (cycleButtonWidth / 2f), y), cycleButtonWidth, 30f);
        _statusLabel = statusButton.GetComponentInChildren<Text>();
        statusButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            _statusLabel!.text = AtlasStrings.Get("workbench.status") + ": " + _controller.CycleStatus();
        });

        GameObject scopeButton = gui.CreateButton(
            "Scope", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(ContentHalfWidth - (cycleButtonWidth / 2f), y), cycleButtonWidth, 30f);
        _scopeLabel = scopeButton.GetComponentInChildren<Text>();
        scopeButton.GetComponent<Button>().onClick.AddListener(() =>
        {
            _scopeLabel!.text = AtlasStrings.Get("workbench.scope") + ": " + _controller.CycleScope();
        });
        y -= 38f;

        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.checked"), new Vector2(LabelCenterX, y));
        GameObject toggle = gui.CreateToggle(_editRows.transform, 28f, 28f);
        var toggleRect = (RectTransform)toggle.transform;
        toggleRect.anchorMin = new Vector2(0.5f, 1f);
        toggleRect.anchorMax = new Vector2(0.5f, 1f);
        toggleRect.anchoredPosition = new Vector2(FieldLeftEdge + 14f, y);
        _checked = toggle.GetComponentInChildren<Toggle>();
        y -= 40f;

        // Metadata-only footer: pin color is stored/synced but NOT rendered
        // on the map in v1, so it must not masquerade as a visual control
        // (#94). Raw hex entry is the advanced fallback by design.
        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.colorMeta"), new Vector2(LabelCenterX, y));
        GameObject colorField = gui.CreateInputField(
            _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(FieldCenterX, y),
            InputField.ContentType.Standard, AtlasStrings.Get("workbench.colorMeta"), 14, FieldColumnWidth, 28f);
        _color = colorField.GetComponent<InputField>();
        y -= 36f;

        const float actionButtonWidth = 110f;
        GameObject apply = gui.CreateButton(
            AtlasStrings.Get("workbench.apply"), _editRows.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(-ContentHalfWidth + (actionButtonWidth / 2f), 40f), actionButtonWidth, 36f);
        apply.GetComponent<Button>().onClick.AddListener(ApplyClicked);

        GameObject deleteButton = gui.CreateButton(
            AtlasStrings.Get("workbench.delete"), _editRows.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(ContentHalfWidth - (actionButtonWidth / 2f), 40f), actionButtonWidth, 36f);
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

    private void BuildIconRow(GUIManager gui, Font font, Color labelColor, ref float y)
    {
        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.icon"), new Vector2(LabelCenterX, y));

        const float previewSize = 26f;
        var previewHolder = new GameObject("CCIconPreview", typeof(RectTransform), typeof(Image));
        previewHolder.transform.SetParent(_editRows!.transform, worldPositionStays: false);
        var previewRect = (RectTransform)previewHolder.transform;
        previewRect.anchorMin = new Vector2(0.5f, 1f);
        previewRect.anchorMax = new Vector2(0.5f, 1f);
        previewRect.anchoredPosition = new Vector2(FieldLeftEdge + (previewSize / 2f), y);
        previewRect.sizeDelta = new Vector2(previewSize, previewSize);
        _iconPreview = previewHolder.GetComponent<Image>();
        _iconPreview.preserveAspect = true;
        _iconPreview.enabled = false;

        float buttonWidth = FieldColumnWidth - previewSize - 6f;
        GameObject iconButton = gui.CreateButton(
            "", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(FieldLeftEdge + previewSize + 6f + (buttonWidth / 2f), y), buttonWidth, 28f);
        _iconButtonLabel = iconButton.GetComponentInChildren<Text>();
        iconButton.GetComponent<Button>().onClick.AddListener(ToggleIconDropdown);
        _iconRowY = y;
        y -= 36f;
    }

    private void BuildCategoryRow(GUIManager gui, Font font, Color labelColor, ref float y)
    {
        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.category"), new Vector2(LabelCenterX, y));

        const float suggestWidth = 32f;
        float fieldWidth = FieldColumnWidth - suggestWidth - 4f;
        GameObject categoryField = gui.CreateInputField(
            _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(FieldLeftEdge + (fieldWidth / 2f), y),
            InputField.ContentType.Standard, AtlasStrings.Get("workbench.category"), 14, fieldWidth, 28f);
        _category = categoryField.GetComponent<InputField>();

        GameObject suggestButton = gui.CreateButton(
            "...", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(FieldLeftEdge + fieldWidth + 4f + (suggestWidth / 2f), y), suggestWidth, 28f);
        suggestButton.GetComponent<Button>().onClick.AddListener(ToggleCategoryDropdown);
        _categoryRowY = y;
        y -= 36f;
    }

    private void BuildSizeRow(GUIManager gui, Font font, Color labelColor, ref float y)
    {
        CreateLabel(gui, font, labelColor, AtlasStrings.Get("workbench.sizeMeta"), new Vector2(LabelCenterX, y));

        float x = FieldLeftEdge;
        GameObject minus = gui.CreateButton(
            "-", _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x + 14f, y), 28f, 28f);
        minus.GetComponent<Button>().onClick.AddListener(() => NudgeSize(-SizeStep));
        x += 32f;

        _sizeValueLabel = gui.CreateText(
            "", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x + 28f, y),
            font, 14, Color.white, outline: false, Color.black, 56f, 28f, addContentSizeFitter: false)
            .GetComponent<Text>();
        _sizeValueLabel.alignment = TextAnchor.MiddleCenter;
        x += 60f;

        GameObject plus = gui.CreateButton(
            "+", _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x + 14f, y), 28f, 28f);
        plus.GetComponent<Button>().onClick.AddListener(() => NudgeSize(SizeStep));
        x += 40f;

        float resetWidth = FieldColumnWidth - (x - FieldLeftEdge);
        GameObject reset = gui.CreateButton(
            AtlasStrings.Get("workbench.reset"), _editRows.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x + (resetWidth / 2f), y), resetWidth, 28f);
        reset.GetComponent<Button>().onClick.AddListener(() =>
        {
            _sizeValue = 1f;
            UpdateSizeLabel();
        });
        y -= 36f;
    }

    private void NudgeSize(float delta)
    {
        _sizeValue = Mathf.Clamp(_sizeValue + delta, SizeMin, SizeMax);
        UpdateSizeLabel();
    }

    private void UpdateSizeLabel()
    {
        if (_sizeValueLabel != null)
        {
            _sizeValueLabel.text = "×" + _sizeValue.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private void UpdateIconWidgets()
    {
        bool known = IconRegistry.TryResolve(_selectedIconId, out IconRegistry.IconDefinition definition);
        if (_iconButtonLabel != null)
        {
            _iconButtonLabel.text = known
                ? definition.DisplayName
                : AtlasStrings.Format("workbench.customIcon", Truncate(_selectedIconId, 16));
        }

        if (_iconPreview != null)
        {
            if (MinimapReflection.TryGetPinSprite(IconRegistry.ResolveVanillaType(_selectedIconId), out Sprite? sprite))
            {
                _iconPreview.sprite = sprite;
                _iconPreview.enabled = true;
            }
            else
            {
                _iconPreview.enabled = false;
            }
        }
    }

    private void ToggleIconDropdown()
    {
        try
        {
            if (_iconDropdown != null && _iconDropdown.activeSelf)
            {
                _iconDropdown.SetActive(false);
                return;
            }

            CloseDropdowns();
            if (_iconDropdown != null)
            {
                UnityEngine.Object.Destroy(_iconDropdown);
                _iconDropdown = null;
            }

            var entries = new List<(string Label, Action OnPick)>();
            if (_customIconId is string customId)
            {
                entries.Add((
                    AtlasStrings.Format("workbench.keepCustomIcon", Truncate(customId, 14)),
                    () =>
                    {
                        _selectedIconId = customId;
                        UpdateIconWidgets();
                    }));
            }

            foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
            {
                string id = definition.Id;
                entries.Add((definition.DisplayName, () =>
                {
                    _selectedIconId = id;
                    UpdateIconWidgets();
                }));
            }

            _iconDropdown = BuildDropdown(new Vector2(FieldCenterX, _iconRowY - 16f), 230f, entries);
            _iconDropdown.SetActive(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void ToggleCategoryDropdown()
    {
        try
        {
            if (_categoryDropdown != null && _categoryDropdown.activeSelf)
            {
                _categoryDropdown.SetActive(false);
                return;
            }

            CloseDropdowns();
            if (_categoryDropdown != null)
            {
                UnityEngine.Object.Destroy(_categoryDropdown);
                _categoryDropdown = null;
            }

            // Suggestions only: picking one fills the field, which stays
            // free text so custom categories are always possible.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<(string Label, Action OnPick)>();
            string current = _category != null ? _category.text.Trim() : "";
            if (current.Length > 0)
            {
                seen.Add(current);
            }

            foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
            {
                string category = definition.DefaultCategory;
                if (category.Length == 0 || !seen.Add(category))
                {
                    continue;
                }

                entries.Add((category, () =>
                {
                    if (_category != null)
                    {
                        _category.text = category;
                    }
                }));
            }

            _categoryDropdown = BuildDropdown(new Vector2(FieldCenterX, _categoryRowY - 16f), 230f, entries);
            _categoryDropdown.SetActive(true);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private GameObject BuildDropdown(Vector2 anchoredPosition, float width, List<(string Label, Action OnPick)> entries)
    {
        GUIManager gui = GUIManager.Instance;
        const float rowHeight = 28f;
        float height = (entries.Count * rowHeight) + 12f;

        var dropdown = new GameObject("CCDropdown", typeof(RectTransform), typeof(Image));
        dropdown.transform.SetParent(_panel!.transform, worldPositionStays: false);
        var rect = (RectTransform)dropdown.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition + new Vector2(0f, -height / 2f);
        rect.sizeDelta = new Vector2(width, height);
        dropdown.GetComponent<Image>().color = new Color(0.13f, 0.1f, 0.07f, 0.98f);

        for (int index = 0; index < entries.Count; index++)
        {
            (string label, Action onPick) = entries[index];
            GameObject button = gui.CreateButton(
                label, dropdown.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -6f - (rowHeight / 2f) - (index * rowHeight)), width - 12f, 26f);
            GameObject captured = dropdown;
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                onPick();
                captured.SetActive(false);
            });
        }

        return dropdown;
    }

    private void CloseDropdowns()
    {
        if (_iconDropdown != null)
        {
            _iconDropdown.SetActive(false);
        }

        if (_categoryDropdown != null)
        {
            _categoryDropdown.SetActive(false);
        }
    }

    private InputField? CreateRow(GUIManager gui, Font font, Color labelColor, string label, ref float y)
    {
        CreateLabel(gui, font, labelColor, label, new Vector2(LabelCenterX, y));
        GameObject field = gui.CreateInputField(
            _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(FieldCenterX, y),
            InputField.ContentType.Standard, label, 14, FieldColumnWidth, 28f);
        y -= 36f;
        return field.GetComponent<InputField>();
    }

    private void CreateLabel(GUIManager gui, Font font, Color color, string text, Vector2 position)
    {
        // Left-aligned inside the label column; the extra height lets a
        // long localized label wrap to a second line instead of clipping.
        gui.CreateText(
            text, _editRows!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position,
            font, 13, color, outline: false, Color.black, LabelColumnWidth, 36f, addContentSizeFitter: false)
            .GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
    }

    private static Vector2 DefaultAnchoredPosition(float scale)
    {
        return new Vector2(-((PanelWidth * scale) / 2f) - ScreenEdgeMargin, 0f);
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
