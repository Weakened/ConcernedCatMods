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
    private const float PanelWidth = 232f;
    private const float PanelHeight = 560f;
    private const float RowHeight = 24f;
    private const float RowWidth = PanelWidth - 40f;
    private const float ListTop = 108f;
    private const float ListBottomMargin = 18f;
    private const int MaxRecents = 3;

    private readonly ManualLogSource _log;
    private GameObject? _panel;
    private InputField? _search;
    private Text? _status;
    private GameObject? _listRoot;
    private ScrollRect? _scroll;
    private string? _selectedIconId;
    private readonly List<string> _recents = new();

    // RC10 feedback 11: collapsible category sections. Collapsed state is
    // session UI state keyed by category name.
    private readonly HashSet<string> _collapsedCategories = new();
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

    /// <summary>The palette panel root, for the RC8-9 pointer guard.</summary>
    public GameObject? PanelObject => _panel;

    /// <summary>Builds the palette onto the open large map when needed;
    /// cheap after the first call, rebuilt automatically after teardown.
    /// Opened from the toolbar's [Markers] action (#100) — the panel
    /// starts hidden and docks at the shared right-edge position.</summary>
    public void EnsureBuilt()
    {
        if (_failed || _panel != null)
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

            // RC10 feedback 11: draggable, like every other CC surface.
            _panel = gui.CreateWoodpanel(
                largeRoot.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-((PanelWidth * UiScale) / 2f) - 30f, 0f),
                PanelWidth, PanelHeight,
                draggable: true);
            _panel.transform.localScale = Vector3.one * UiScale;
            _panel.SetActive(false);

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

            // RC10 feedback 11: the marker list lives in a scroll view, so
            // the palette can never overflow the screen no matter how many
            // markers the registry grows.
            float listHeight = PanelHeight - ListTop - ListBottomMargin;
            GameObject scrollRoot = gui.CreateScrollView(
                _panel.transform,
                showHorizontalScrollbar: false, showVerticalScrollbar: true,
                handleSize: 8f, handleDistanceToBorder: 2f,
                GUIManager.Instance.ValheimScrollbarHandleColorBlock,
                new Color(0f, 0f, 0f, 0.35f),
                PanelWidth - 20f, listHeight);
            var scrollRect = (RectTransform)scrollRoot.transform;
            scrollRect.anchorMin = new Vector2(0.5f, 1f);
            scrollRect.anchorMax = new Vector2(0.5f, 1f);
            scrollRect.pivot = new Vector2(0.5f, 1f);
            scrollRect.anchoredPosition = new Vector2(0f, -ListTop);

            Transform scrollView = scrollRoot.transform.Find("Scroll View");
            _scroll = scrollView != null ? scrollView.GetComponent<ScrollRect>() : null;
            if (_scroll == null)
            {
                throw new InvalidOperationException("Jötunn scroll view layout changed; palette cannot build.");
            }

            // The scroll chrome ships an opaque black backdrop; soften it so
            // the wood panel shows through.
            var backdrop = scrollView!.GetComponent<Image>();
            if (backdrop != null)
            {
                backdrop.color = new Color(0f, 0f, 0f, 0.25f);
            }

            // Rows are positioned manually (same math as RC8); the stock
            // vertical layout group would fight that.
            GameObject content = _scroll.content != null ? _scroll.content.gameObject : null!;
            if (content == null)
            {
                Transform found = scrollView.Find("Viewport/Content");
                content = found != null ? found.gameObject : throw new InvalidOperationException(
                    "Jötunn scroll view content missing; palette cannot build.");
                _scroll.content = (RectTransform)found!;
            }

            foreach (var component in content.GetComponents<UnityEngine.UI.LayoutGroup>())
            {
                UnityEngine.Object.DestroyImmediate(component);
            }

            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                UnityEngine.Object.DestroyImmediate(fitter);
            }

            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, contentRect.offsetMin.y);
            contentRect.offsetMax = new Vector2(0f, contentRect.offsetMax.y);
            _listRoot = content;

            RebuildList();
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    public void Toggle()
    {
        if (_panel != null)
        {
            _panel.SetActive(!_panel.activeSelf);
        }
    }

    public void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }
    }

    /// <summary>Escape closes the palette like every other major surface
    /// (#100). Call every tick; a closed large map hides the panel with its
    /// root, so only Escape needs handling here.</summary>
    public void HandleFrame()
    {
        if (IsVisible && Input.GetKeyDown(KeyCode.Escape) && !CcTextFocus.EscapeShouldOnlyBlur())
        {
            Hide();
        }
    }

    /// <summary>Called when the enhanced palette becomes unavailable
    /// (setting off, conflicting pin manager, failure): hides the panel
    /// and clears any armed selection.</summary>
    public void SetUnavailable()
    {
        Hide();
        if (_selectedIconId is not null)
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
        _panel = null;
        _search = null;
        _status = null;
        _listRoot = null;
        _scroll = null;
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
            float y = -4f;

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
                        AddIconRow(gui, definition, ref y);
                    }
                }

                // Collapsible category sections in a scrolling list (RC10
                // feedback 11): every marker is reachable, nothing is
                // capped away, and big sections fold out of the way.
                var seenCategories = new List<string>();
                foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
                {
                    if (!seenCategories.Contains(definition.DefaultCategory))
                    {
                        seenCategories.Add(definition.DefaultCategory);
                    }
                }

                foreach (string category in seenCategories)
                {
                    int count = 0;
                    foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
                    {
                        if (definition.DefaultCategory == category)
                        {
                            count++;
                        }
                    }

                    bool collapsed = _collapsedCategories.Contains(category);
                    AddCategoryHeader(gui, category, count, collapsed, ref y);
                    if (collapsed)
                    {
                        continue;
                    }

                    foreach (IconRegistry.IconDefinition definition in IconRegistry.All)
                    {
                        if (definition.DefaultCategory == category)
                        {
                            AddIconRow(gui, definition, ref y);
                        }
                    }
                }
            }
            else
            {
                // Search flattens the grouping and ignores collapse state.
                foreach (IconRegistry.IconDefinition definition in IconRegistry.Search(query))
                {
                    AddIconRow(gui, definition, ref y);
                }
            }

            var contentRect = (RectTransform)_listRoot.transform;
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, Mathf.Max(-y + 6f, 10f));
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

    /// <summary>A clickable section header that folds its category.</summary>
    private void AddCategoryHeader(GUIManager gui, string category, int count, bool collapsed, ref float y)
    {
        GameObject header = gui.CreateButton(
            $"{(collapsed ? "▸" : "▾")} {category} ({count})",
            _listRoot!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            RowWidth, 20f);
        Text label = header.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.fontSize = 11;
            label.color = new Color(0.85f, 0.75f, 0.55f, 1f);
        }

        header.GetComponent<Button>().onClick.AddListener(() =>
        {
            if (!_collapsedCategories.Remove(category))
            {
                _collapsedCategories.Add(category);
            }

            RebuildList();
        });
        y -= 23f;
    }

    private void AddIconRow(GUIManager gui, IconRegistry.IconDefinition definition, ref float y)
    {
        bool selected = string.Equals(definition.Id, _selectedIconId, StringComparison.Ordinal);
        GameObject row = gui.CreateButton(
            (selected ? "» " : "") + definition.DisplayName,
            _listRoot!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            RowWidth, RowHeight);

        Sprite? sprite = CcIconSprites.TryGet(definition.Id, out Sprite ccSprite) ? ccSprite : null;
        if (sprite != null || MinimapReflection.TryGetPinSprite(definition.VanillaType, out sprite))
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

        _log.LogError($"Enhanced pin palette failed and was disabled for this session (vanilla pin selector remains available): {exception}");
    }

    /// <summary>True once the palette has failed; the runtime restores the
    /// vanilla selector so pin creation is never lost.</summary>
    public bool HasFailed => _failed;
}
