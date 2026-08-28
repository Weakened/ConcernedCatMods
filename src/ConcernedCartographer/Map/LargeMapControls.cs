using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Discoverability layer on the vanilla large map (#95, #96): the
/// [Atlas] button (with hover tooltip) that opens the Atlas Drawer, a
/// contextual action button for the hovered pin ("Upgrade &amp; Edit" for
/// adoptable vanilla markers, "Edit Pin" for managed ones), and the
/// lightweight "P — Edit with Concerned Cartographer" accelerator hint.
/// Everything parents to Minimap.m_largeRoot, so it exists only while the
/// large map is open and dies with it on teardown; nothing here touches
/// vanilla behavior (right-click delete and all other vanilla input stay
/// untouched). Fail-closed: any UI exception disables the layer for the
/// session — hotkeys remain the functional path.</summary>
internal sealed class LargeMapControls
{
    private readonly ManualLogSource _log;
    private GameObject? _atlasButton;
    private Text? _tooltipText;
    private Text? _hintText;
    private GameObject? _contextButton;
    private Text? _contextLabel;
    private Action? _contextAction;
    private bool _pointerOverContext;
    private bool _failed;

    /// <summary>Raised when the Atlas button is clicked; the runtime routes
    /// it to the same toggle as the drawer hotkey.</summary>
    public Action? AtlasButtonClicked;

    public LargeMapControls(ManualLogSource log)
    {
        _log = log;
    }

    /// <summary>True while the mouse is over the contextual action button,
    /// so the owner keeps the action alive during the click.</summary>
    public bool PointerOverContext => _pointerOverContext;

    /// <summary>Builds the controls onto the open large map when needed.
    /// Cheap after the first call; rebuilds automatically after the map
    /// root was torn down (world switch, logout).</summary>
    public void EnsureBuilt(string drawerHotkeyName)
    {
        if (_failed || _atlasButton != null)
        {
            return;
        }

        try
        {
            GameObject? largeRoot = Minimap.instance != null ? Minimap.instance.m_largeRoot : null;
            if (largeRoot == null || !largeRoot.activeInHierarchy ||
                GUIManager.Instance == null)
            {
                return;
            }

            GUIManager gui = GUIManager.Instance;
            _atlasButton = gui.CreateButton(
                AtlasStrings.Get("hud.atlasButton"),
                largeRoot.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-110f, 90f), 170f, 30f);
            _atlasButton.GetComponent<Button>().onClick.AddListener(() => AtlasButtonClicked?.Invoke());

            _tooltipText = gui.CreateText(
                AtlasStrings.Format("hud.atlasTooltip", drawerHotkeyName),
                largeRoot.transform,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-190f, 130f),
                gui.AveriaSerifBold, 13, new Color(1f, 0.95f, 0.75f, 1f),
                outline: true, Color.black, 360f, 40f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _tooltipText.alignment = TextAnchor.LowerRight;
            _tooltipText.gameObject.SetActive(false);
            AddHoverHandlers(
                _atlasButton,
                () => _tooltipText?.gameObject.SetActive(true),
                () => _tooltipText?.gameObject.SetActive(false));

            _hintText = gui.CreateText(
                "", largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 68f),
                gui.AveriaSerifBold, 16, new Color(1f, 0.95f, 0.75f, 1f),
                outline: true, Color.black, 700f, 26f, addContentSizeFitter: false)
                .GetComponent<Text>();
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.gameObject.SetActive(false);

            _contextButton = gui.CreateButton(
                "", largeRoot.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 102f), 220f, 32f);
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
            _log.LogError($"Large-map controls failed and are disabled for this session (hotkeys still work): {exception}");
        }
    }

    /// <summary>Shows the contextual edit hint, or hides it for null/empty.</summary>
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
            _log.LogError($"Large-map hint failed and is disabled for this session: {exception}");
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
            _log.LogError($"Large-map context action failed and is disabled for this session: {exception}");
        }
    }

    /// <summary>Drops references so a new map root rebuilds the controls.
    /// The old objects are destroyed with the map hierarchy itself.</summary>
    public void Reset()
    {
        _atlasButton = null;
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
