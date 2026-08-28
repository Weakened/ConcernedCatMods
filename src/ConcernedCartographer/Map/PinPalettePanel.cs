using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The Enhanced Pin Palette (#96): a marker browser on the right
/// side of the large map that replaces the five raw vanilla icon buttons
/// as the pin-creation surface. Sprite preview + human label + search over
/// the stable IconRegistry (no raw IDs in normal UI), session recents, a
/// collapse toggle, and a selection that arms the managed-from-birth flow:
/// choosing a marker selects the mapped vanilla icon type, the player
/// double-clicks the map exactly like vanilla, and the runtime associates
/// the newborn pin. Parents to Minimap.m_largeRoot so it lives and dies
/// with the large map; fail-closed — any UI exception disables the palette
/// and the vanilla selector is restored by the runtime.</summary>
internal sealed class PinPalettePanel
{
    private const float PanelWidth = 216f;
    private const float PanelHeight = 500f;
    private const float RowHeight = 24f;
    private const float RowWidth = PanelWidth - 24f;
    private const int MaxRows = 14;
    private const int MaxRecents = 3;

    private readonly ManualLogSource _log;
    private GameObject? _toggleButton;
    private GameObject? _panel;
    private InputField? _search;
    private Text? _status;
    private GameObject? _listRoot;
    private string? _selectedIconId;
    private readonly List<string> _recents = new();
    private bool _failed;

    /// <summary>Raised when the player picks a marker; the runtime selects
    /// the vanilla icon type and arms the birth tracker.</summary>
    public Action<IconRegistry.IconDefinition>? IconChosen;

    /// <summary>Raised when the player clears the selection.</summary>
    public Action? SelectionCleared;

    /// <summary>Accessibility scale applied when built.</summary>
    public float UiScale = 1f;

    public PinPalettePanel(ManualLogSource log)
    {
        _log = log;
    }

    public string? SelectedIconId => _selectedIconId;

    /// <summary>Builds the palette onto the open large map when needed;
    /// cheap after the first call, rebuilt automatically after teardown.</summary>
    public void EnsureBuilt()
    {
        if (_failed || _toggleButton != null)
        {
            return;
        }

        try
        {
            GameObject? largeRoot = Minimap.instance != null ? Minimap.instance.m_largeRoot : null;
            if (largeRoot == null || !largeRoot.activeInHierarchy || GUIManager.Instance == null)
            {
                return;
            }

            GUIManager gui = GUIManager.Instance;

            _toggleButton = gui.CreateButton(
                AtlasStrings.Get("palette.toggle"),
                largeRoot.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-110f, 128f), 170f, 30f);
            _toggleButton.transform.localScale = Vector3.one * UiScale;
            _toggleButton.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (_panel != null)
                {
                    _panel.SetActive(!_panel.activeSelf);
                }
            });

            _panel = gui.CreateWoodpanel(
                largeRoot.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-((PanelWidth * UiScale) / 2f) - 12f, 40f),
                PanelWidth, PanelHeight,
                draggable: false);
            _panel.transform.localScale = Vector3.one * UiScale;

            Font font = gui.AveriaSerifBold;
            var labelColor = new Color(0.9f, 0.8f, 0.6f, 1f);
            gui.CreateText(
                AtlasStrings.Get("palette.title"), _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f),
                font, 16, labelColor, outline: true, Color.black, PanelWidth - 24f, 26f, addContentSizeFitter: false);

            GameObject searchField = gui.CreateInputField(
                _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                InputField.ContentType.Standard, AtlasStrings.Get("palette.search"), 13, RowWidth, 26f);
            _search = searchField.GetComponent<InputField>();
            _search.onValueChanged.AddListener(_ => RebuildList());

            _status = gui.CreateText(
                AtlasStrings.Get("palette.pick"), _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f),
                font, 11, Color.white, outline: false, Color.black, RowWidth, 30f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _status.alignment = TextAnchor.UpperCenter;

            _listRoot = new GameObject("CCPaletteList", typeof(RectTransform));
            _listRoot.transform.SetParent(_panel.transform, worldPositionStays: false);
            var listRect = (RectTransform)_listRoot.transform;
            listRect.anchorMin = Vector2.zero;
            listRect.anchorMax = Vector2.one;
            listRect.offsetMin = Vector2.zero;
            listRect.offsetMax = Vector2.zero;

            RebuildList();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    /// <summary>Master visibility (EnhancedPinPalette setting / compat
    /// fallback). Hiding also clears any armed selection.</summary>
    public void SetVisible(bool visible)
    {
        if (_toggleButton != null && _toggleButton.activeSelf != visible)
        {
            _toggleButton.SetActive(visible);
        }

        if (!visible && _panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }

        if (!visible && _selectedIconId is not null)
        {
            _selectedIconId = null;
            SelectionCleared?.Invoke();
        }
    }

    /// <summary>Records a successful palette placement for the session
    /// Recent group.</summary>
    public void NoteUsed(string iconId)
    {
        _recents.Remove(iconId);
        _recents.Insert(0, iconId);
        while (_recents.Count > MaxRecents)
        {
            _recents.RemoveAt(_recents.Count - 1);
        }

        if (_panel != null)
        {
            RebuildList();
        }
    }

    /// <summary>Drops references after map/world teardown so the next open
    /// rebuilds. Session recents survive; the armed selection is cleared
    /// by the owner via SelectionCleared flows.</summary>
    public void Reset()
    {
        _toggleButton = null;
        _panel = null;
        _search = null;
        _status = null;
        _listRoot = null;
    }

    private void RebuildList()
    {
        if (_listRoot == null)
        {
            return;
        }

        try
        {
            foreach (Transform child in _listRoot.transform)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            GUIManager gui = GUIManager.Instance;
            Font font = gui.AveriaSerifBold;
            var headerColor = new Color(0.8f, 0.7f, 0.5f, 1f);
            float y = -108f;
            int rows = 0;

            string query = _search != null ? _search.text.Trim() : "";
            if (query.Length == 0)
            {
                var recentDefinitions = new List<IconRegistry.IconDefinition>();
                foreach (string recentId in _recents)
                {
                    if (IconRegistry.TryResolve(recentId, out IconRegistry.IconDefinition recent))
                    {
                        recentDefinitions.Add(recent);
                    }
                }

                if (recentDefinitions.Count > 0)
                {
                    AddHeader(gui, font, headerColor, AtlasStrings.Get("palette.recent"), ref y);
                    foreach (IconRegistry.IconDefinition definition in recentDefinitions)
                    {
                        if (rows >= MaxRows)
                        {
                            break;
                        }

                        AddIconRow(gui, definition, ref y);
                        rows++;
                    }
                }

                AddHeader(gui, font, headerColor, AtlasStrings.Get("palette.all"), ref y);
                foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
                {
                    if (rows >= MaxRows)
                    {
                        break;
                    }

                    AddIconRow(gui, definition, ref y);
                    rows++;
                }
            }
            else
            {
                foreach (IconRegistry.IconDefinition definition in IconRegistry.Search(query))
                {
                    if (rows >= MaxRows)
                    {
                        break;
                    }

                    AddIconRow(gui, definition, ref y);
                    rows++;
                }
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void AddHeader(GUIManager gui, Font font, Color color, string text, ref float y)
    {
        gui.CreateText(
            text, _listRoot!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            font, 11, color, outline: false, Color.black, RowWidth, 16f, addContentSizeFitter: false)
            .GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        y -= 18f;
    }

    private void AddIconRow(GUIManager gui, IconRegistry.IconDefinition definition, ref float y)
    {
        bool selected = string.Equals(definition.Id, _selectedIconId, StringComparison.Ordinal);
        GameObject row = gui.CreateButton(
            (selected ? "» " : "") + definition.DisplayName,
            _listRoot!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            RowWidth, RowHeight);

        if (MinimapReflection.TryGetPinSprite(definition.VanillaType, out Sprite? sprite))
        {
            var previewHolder = new GameObject("CCPalettePreview", typeof(RectTransform), typeof(Image));
            previewHolder.transform.SetParent(row.transform, worldPositionStays: false);
            var previewRect = (RectTransform)previewHolder.transform;
            previewRect.anchorMin = new Vector2(0f, 0.5f);
            previewRect.anchorMax = new Vector2(0f, 0.5f);
            previewRect.anchoredPosition = new Vector2(14f, 0f);
            previewRect.sizeDelta = new Vector2(18f, 18f);
            var preview = previewHolder.GetComponent<Image>();
            preview.sprite = sprite;
            preview.preserveAspect = true;
            preview.raycastTarget = false;
        }

        row.GetComponent<Button>().onClick.AddListener(() => RowClicked(definition));
        y -= RowHeight + 2f;
    }

    private void RowClicked(IconRegistry.IconDefinition definition)
    {
        try
        {
            if (string.Equals(definition.Id, _selectedIconId, StringComparison.Ordinal))
            {
                _selectedIconId = null;
                if (_status != null)
                {
                    _status.text = AtlasStrings.Get("palette.pick");
                }

                SelectionCleared?.Invoke();
            }
            else
            {
                _selectedIconId = definition.Id;
                if (_status != null)
                {
                    _status.text = AtlasStrings.Format("palette.place", definition.DisplayName);
                }

                IconChosen?.Invoke(definition);
            }

            RebuildList();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        if (_toggleButton != null)
        {
            _toggleButton.SetActive(false);
        }

        _log.LogError($"Enhanced pin palette failed and was disabled for this session (vanilla pin selector remains available): {exception}");
    }

    /// <summary>True once the palette has failed; the runtime restores the
    /// vanilla selector so pin creation is never lost.</summary>
    public bool HasFailed => _failed;
}
