using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedCartographer.Atlas;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedCartographer.Map;

/// <summary>Shared base for every Concerned Cartographer side panel
/// (#100): one wood-panel language, one right-edge dock position (the Pin
/// Workbench placement reference), one UiScale/Escape/map-closed
/// contract, and fail-closed construction. Panels build their content
/// once via <see cref="BuildContent"/> and refresh via
/// <see cref="OnShown"/>. The <see cref="MapUiCoordinator"/> guarantees
/// only one major side surface is visible at a time.</summary>
internal abstract class CcSidePanel
{
    protected const float DockMargin = 30f;

    protected readonly ManualLogSource Log;
    private readonly string _titleKey;
    private readonly float _width;
    private readonly float _height;
    private GameObject? _panel;
    private Text? _title;
    private bool _failed;

    /// <summary>Accessibility scale applied when the panel shows.</summary>
    public float UiScale = 1f;

    private float _appliedScale = 1f;

    protected CcSidePanel(ManualLogSource log, string titleKey, float width, float height)
    {
        Log = log;
        _titleKey = titleKey;
        _width = width;
        _height = height;
    }

    public bool IsVisible => _panel != null && _panel.activeSelf;

    /// <summary>True after a UI failure disabled this panel; owners fall
    /// back (e.g. restore the vanilla rail).</summary>
    public bool HasFailed => _failed;

    protected float Width => _width;

    protected float Height => _height;

    protected GameObject? Panel => _panel;

    public void Show()
    {
        if (!EnsureBuilt())
        {
            return;
        }

        try
        {
            if (!Mathf.Approximately(_appliedScale, UiScale))
            {
                _appliedScale = UiScale;
                _panel!.transform.localScale = Vector3.one * UiScale;
                ((RectTransform)_panel.transform).anchoredPosition = DockPosition(UiScale);
            }

            _panel!.SetActive(true);
            OnShown();

            // Controller focus chain (#100): opening a surface selects its
            // first interactable control, mirroring the drawer/workbench
            // select-on-open pattern.
            UnityEngine.UI.Selectable? firstSelectable =
                _panel.GetComponentInChildren<UnityEngine.UI.Selectable>(includeInactive: false);
            if (firstSelectable != null)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(firstSelectable.gameObject);
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
            try
            {
                OnHidden();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    /// <summary>Escape closes; a large map that disappeared underneath
    /// closes with it. Call every tick.</summary>
    public virtual void HandleFrame()
    {
        if (!IsVisible)
        {
            return;
        }

        try
        {
            // Escape while typing in a field only ends the typing (RC10
            // feedback 14); the next Escape closes the panel.
            if (!Minimap.IsOpen() ||
                (Input.GetKeyDown(KeyCode.Escape) && !CcTextFocus.EscapeShouldOnlyBlur()))
            {
                Hide();
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    protected void SetTitle(string text)
    {
        if (_title != null)
        {
            _title.text = text;
        }
    }

    /// <summary>Panel-specific rows; called once. y starts below the title
    /// (top-anchored local coordinates, x=0 is panel center).</summary>
    protected abstract void BuildContent(GUIManager gui, Font font, Color headerColor, ref float y);

    /// <summary>Refresh hook every time the panel becomes visible.</summary>
    protected virtual void OnShown()
    {
    }

    protected virtual void OnHidden()
    {
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
            return false;
        }

        try
        {
            GUIManager gui = GUIManager.Instance;
            Font font = gui.AveriaSerifBold;
            var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);

            // Draggable like the Pin Workbench (RC8-9): every CC side panel
            // can be moved off the map area the player is working on.
            _panel = gui.CreateWoodpanel(
                GUIManager.CustomGUIFront!.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                DockPosition(1f), _width, _height, draggable: true);

            _title = gui.CreateText(
                AtlasStrings.Get(_titleKey), _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                font, 19, headerColor, outline: true, Color.black, _width - 40f, 30f, addContentSizeFitter: false)
                .GetComponent<Text>();

            float y = -62f;
            BuildContent(gui, font, headerColor, ref y);

            GameObject close = gui.CreateButton(
                AtlasStrings.Get("workbench.close"), _panel.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 30f), 110f, 30f);
            close.GetComponent<Button>().onClick.AddListener(Hide);

            _panel.SetActive(false);
            return true;
        }
        catch (Exception exception)
        {
            Fail(exception);
            return false;
        }
    }

    private Vector2 DockPosition(float scale)
    {
        return new Vector2(-((_width * scale) / 2f) - DockMargin, 0f);
    }

    protected void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        Log.LogError($"{GetType().Name} failed and was disabled for this session (console commands remain available): {exception}");
    }

    // ------------------------------------------------------------------
    // Shared construction helpers
    // ------------------------------------------------------------------

    protected Text AddBody(GUIManager gui, Font font, string text, int size, Color color, ref float y, float height)
    {
        Text body = gui.CreateText(
            text, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y),
            font, size, color, outline: false, Color.black, _width - 40f, height, addContentSizeFitter: false)
            .GetComponent<Text>();
        body.alignment = TextAnchor.UpperLeft;
        y -= height + 6f;
        return body;
    }

    protected Button AddButton(GUIManager gui, string label, float x, float y, float width, float height, Action onClick)
    {
        GameObject button = gui.CreateButton(
            label, _panel!.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x, y), width, height);
        var component = button.GetComponent<Button>();
        component.onClick.AddListener(() =>
        {
            try
            {
                onClick();
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        });
        return component;
    }

    protected Toggle AddToggle(
        GUIManager gui, Font font, Color color, string label, float x, float y,
        Action<bool> onChanged, float labelWidth = 190f)
    {
        GameObject toggle = gui.CreateToggle(_panel!.transform, 24f, 24f);
        var rect = (RectTransform)toggle.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        Text text = gui.CreateText(
            label, _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(x + 18f + (labelWidth / 2f), y),
            font, 13, color, outline: false, Color.black, labelWidth, 26f, addContentSizeFitter: false)
            .GetComponent<Text>();
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        Toggle component = toggle.GetComponentInChildren<Toggle>();
        component.onValueChanged.AddListener(value =>
        {
            try
            {
                if (!_suppressToggleEvents)
                {
                    onChanged(value);
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        });
        return component;
    }

    private bool _suppressToggleEvents;

    /// <summary>Sets a toggle's state without firing its change action.</summary>
    protected void SetToggleSilently(Toggle toggle, bool value)
    {
        _suppressToggleEvents = true;
        try
        {
            toggle.isOn = value;
        }
        finally
        {
            _suppressToggleEvents = false;
        }
    }
}
