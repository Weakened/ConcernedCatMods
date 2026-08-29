using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>The large-map UI coordinator (#100): one persistent compact
/// toolbar — [Atlas][Markers][Routes][Survey][Share][Quick Pin][Settings]
/// — plus the contextual pin action button, the accelerator hint, and the
/// Atlas tooltip, all on <c>Minimap.m_largeRoot</c> so they live and die
/// with the map. It also enforces the one-major-side-surface-at-a-time
/// rule: every registered surface is closed before another opens, and all
/// share the Pin Workbench right-dock placement (via
/// <see cref="CcSidePanel"/>). Fail-closed; hotkeys stay the accelerator
/// path.</summary>
internal sealed class MapUiCoordinator
{
    private readonly ManualLogSource _log;
    private GameObject? _toolbar;
    private Text? _tooltipText;
    private Text? _hintText;
    private GameObject? _contextButton;
    private Text? _contextLabel;
    private Action? _contextAction;
    private bool _pointerOverContext;
    private bool _failed;

    private readonly List<(Func<bool> IsVisible, Action Close)> _surfaces = new();

    public Action? AtlasClicked;
    public Action? MarkersClicked;
    public Action? RoutesClicked;
    public Action? SurveyClicked;
    public Action? ShareClicked;
    public Action? QuickPinClicked;
    public Action? SettingsClicked;

    public MapUiCoordinator(ManualLogSource log)
    {
        _log = log;
    }

    public bool PointerOverContext => _pointerOverContext;

    /// <summary>Registers a major side surface for exclusivity and returns
    /// its token for <see cref="OpenExclusive"/>.</summary>
    public int RegisterSurface(Func<bool> isVisible, Action close)
    {
        _surfaces.Add((isVisible, close));
        return _surfaces.Count - 1;
    }

    /// <summary>Closes every registered surface except the given one, then
    /// runs the open/toggle action — the one-panel-at-a-time rule.</summary>
    public void OpenExclusive(int selfToken, Action openOrToggle)
    {
        try
        {
            for (int index = 0; index < _surfaces.Count; index++)
            {
                if (index != selfToken && SafeIsVisible(_surfaces[index].IsVisible))
                {
                    _surfaces[index].Close();
                }
            }

            openOrToggle();
        }
        catch (Exception exception)
        {
            _log.LogWarning($"Map surface switch failed: {exception.Message}");
        }
    }

    public void CloseAllSurfaces()
    {
        foreach ((Func<bool> isVisible, Action close) in _surfaces)
        {
            if (SafeIsVisible(isVisible))
            {
                try
                {
                    close();
                }
                catch
                {
                    // Closing is best effort.
                }
            }
        }
    }

    private static bool SafeIsVisible(Func<bool> isVisible)
    {
        try
        {
            return isVisible();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds the toolbar onto the open large map when needed;
    /// rebuilds automatically after map teardown.</summary>
    public void EnsureBuilt(string drawerHotkeyName)
    {
        if (_failed || _toolbar != null)
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

            _toolbar = new GameObject("CCToolbar", typeof(RectTransform));
            _toolbar.transform.SetParent(largeRoot.transform, worldPositionStays: false);
            var toolbarRect = (RectTransform)_toolbar.transform;
            toolbarRect.anchorMin = new Vector2(0.5f, 0f);
            toolbarRect.anchorMax = new Vector2(0.5f, 0f);
            toolbarRect.anchoredPosition = new Vector2(0f, 62f);
            toolbarRect.sizeDelta = new Vector2(720f, 32f);

            var buttons = new (string LabelKey, Func<Action?> Handler)[]
            {
                ("toolbar.atlas", () => AtlasClicked),
                ("toolbar.markers", () => MarkersClicked),
                ("toolbar.routes", () => RoutesClicked),
                ("toolbar.survey", () => SurveyClicked),
                ("toolbar.share", () => ShareClicked),
                ("toolbar.quickpin", () => QuickPinClicked),
                ("toolbar.settings", () => SettingsClicked),
            };
            const float buttonWidth = 98f;
            const float gap = 4f;
            float total = (buttons.Length * buttonWidth) + ((buttons.Length - 1) * gap);
            float x = -total / 2f + (buttonWidth / 2f);
            foreach ((string labelKey, Func<Action?> handler) in buttons)
            {
                GameObject button = gui.CreateButton(
                    AtlasStrings.Get(labelKey), _toolbar.transform,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), buttonWidth, 30f);
                Func<Action?> captured = handler;
                button.GetComponent<Button>().onClick.AddListener(() => captured()?.Invoke());
                x += buttonWidth + gap;
            }

            _tooltipText = gui.CreateText(
                AtlasStrings.Format("hud.atlasTooltip", drawerHotkeyName),
                largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f),
                gui.AveriaSerifBold, 13, new Color(1f, 0.95f, 0.75f, 1f),
                outline: true, Color.black, 700f, 24f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _tooltipText.alignment = TextAnchor.MiddleCenter;
            _tooltipText.gameObject.SetActive(false);
            if (_toolbar.transform.childCount > 0)
            {
                AddHoverHandlers(
                    _toolbar.transform.GetChild(0).gameObject,
                    () => _tooltipText?.gameObject.SetActive(true),
                    () => _tooltipText?.gameObject.SetActive(false));
            }

            _hintText = gui.CreateText(
                "", largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 100f),
                gui.AveriaSerifBold, 15, new Color(1f, 0.95f, 0.75f, 1f),
                outline: true, Color.black, 700f, 24f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.gameObject.SetActive(false);

            _contextButton = gui.CreateButton(
                "", largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 132f), 220f, 32f);
            _contextLabel = _contextButton.GetComponentInChildren<Text>();
            _contextButton.GetComponent<Button>().onClick.AddListener(() =>
            {
                Action? action = _contextAction;
                _pointerOverContext = false;
                SetContext(null, null);
                action?.Invoke();
            });
            AddHoverHandlers(
                _contextButton,
                () => _pointerOverContext = true,
                () => _pointerOverContext = false);
            _contextButton.SetActive(false);
        }
        catch (Exception exception)
        {
            _failed = true;
            _log.LogError($"Map toolbar failed and is disabled for this session (hotkeys still work): {exception}");
        }
    }

    /// <summary>Shows the accelerator hint, or hides it for null/empty.</summary>
    public void SetHint(string? text)
    {
        if (_failed || _hintText == null)
        {
            return;
        }

        try
        {
            bool show = !string.IsNullOrEmpty(text);
            if (show && _hintText.text != text)
            {
                _hintText.text = text;
            }

            if (_hintText.gameObject.activeSelf != show)
            {
                _hintText.gameObject.SetActive(show);
            }
        }
        catch (Exception exception)
        {
            _failed = true;
            _log.LogError($"Map hint failed and is disabled for this session: {exception}");
        }
    }

    /// <summary>Shows the contextual pin action ("Upgrade &amp; Edit" /
    /// "Edit Pin"), or hides it when label is null.</summary>
    public void SetContext(string? label, Action? action)
    {
        if (_failed || _contextButton == null)
        {
            return;
        }

        try
        {
            bool show = !string.IsNullOrEmpty(label) && action is not null;
            _contextAction = show ? action : null;
            if (show && _contextLabel != null && _contextLabel.text != label)
            {
                _contextLabel.text = label;
            }

            if (_contextButton.activeSelf != show)
            {
                _contextButton.SetActive(show);
                if (!show)
                {
                    _pointerOverContext = false;
                }
            }
        }
        catch (Exception exception)
        {
            _failed = true;
            _log.LogError($"Map context action failed and is disabled for this session: {exception}");
        }
    }

    /// <summary>Drops references so a new map root rebuilds everything.</summary>
    public void Reset()
    {
        _toolbar = null;
        _tooltipText = null;
        _hintText = null;
        _contextButton = null;
        _contextLabel = null;
        _contextAction = null;
        _pointerOverContext = false;
    }

    private static void AddHoverHandlers(GameObject target, Action onEnter, Action onExit)
    {
        var trigger = target.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => onEnter());
        trigger.triggers.Add(enter);
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => onExit());
        trigger.triggers.Add(exit);
    }
}
