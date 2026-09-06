using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Compatibility;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The compatibility status surface (CT-036), reached from the Cart
/// Status panel's Compat button: the exact same lines the startup log banner
/// prints (<see cref="Domain.Compatibility.CompatibilityStatusPresenter"/>),
/// so the panel can never say something different from what was logged.
/// Read-only — this panel only displays <see cref="CompatibilityAdapter"/>'s
/// probe result. Fail-closed session-disable like every Teamster panel.</summary>
internal sealed class CompatibilityPanel
{
    private const float PanelWidth = 380f;
    private const float PanelHeight = 360f;
    private const float RowHeight = 26f;
    private const int MaxLines = 8;

    private readonly ManualLogSource _log;
    private readonly Func<float> _uiScale;
    private bool _failed;
    private GameObject? _panel;
    private Text[] _lines = Array.Empty<Text>();
    private Text? _overflow;

    public CompatibilityPanel(ManualLogSource log, Func<float> uiScale)
    {
        _log = log;
        _uiScale = uiScale;
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
            if (_panel == null && !EnsurePanel())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                Render();
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

    public void HandleFrame()
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
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Render()
    {
        IReadOnlyList<ModDetectionResult>? results = CompatibilityAdapter.Results;

        if (results is null)
        {
            // Distinguishes "probe has not run this session yet" from
            // "probe ran and found nothing" — the two must never look
            // identical, or a crashed probe would read as a clean bill of
            // health. Not reachable in normal play (the probe runs on the
            // plugin's first Update tick, long before any panel can open),
            // but CompatibilityRegistry.Evaluate's per-probe fail-closed
            // catch keeps this state honest if it ever is.
            _lines[0].text = TeamsterStrings.Get("compat.notYetChecked");
            for (int index = 1; index < _lines.Length; index++)
            {
                _lines[index].text = string.Empty;
            }

            if (_overflow != null)
            {
                _overflow.text = string.Empty;
            }

            return;
        }

        IReadOnlyList<string> detected = CompatibilityStatusPresenter.ComposeDetectedLines(results);
        if (detected.Count == 0)
        {
            _lines[0].text = CompatibilityStatusPresenter.ComposeNoneDetectedLine();
            for (int index = 1; index < _lines.Length; index++)
            {
                _lines[index].text = string.Empty;
            }

            if (_overflow != null)
            {
                _overflow.text = string.Empty;
            }

            return;
        }

        int shown = Math.Min(detected.Count, _lines.Length);
        for (int index = 0; index < _lines.Length; index++)
        {
            _lines[index].text = index < shown ? detected[index] : string.Empty;
        }

        if (_overflow != null)
        {
            int hidden = detected.Count - shown;
            _overflow.text = hidden > 0
                ? TeamsterStrings.Format("compat.overflow", hidden.ToString(System.Globalization.CultureInfo.InvariantCulture))
                : string.Empty;
        }
    }

    private bool EnsurePanel()
    {
        if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
        {
            return false;
        }

        GUIManager gui = GUIManager.Instance;
        Font font = gui.AveriaSerifBold;
        Color headerColor = PanelStyle.Header;
        Color bodyColor = PanelStyle.Body;

        _panel = PanelStyle.CreateScaledWoodpanel(
            gui, GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0f, 120f), PanelWidth, PanelHeight, _uiScale());

        gui.CreateText(
            TeamsterStrings.Get("compat.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        _lines = new Text[MaxLines];
        float y = -66f;
        for (int index = 0; index < MaxLines; index++)
        {
            _lines[index] = gui.CreateText(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - (RowHeight / 2f)),
                font, 14, bodyColor, outline: true, Color.black, PanelWidth - 40f, RowHeight,
                addContentSizeFitter: false).GetComponent<Text>();
            _lines[index].alignment = TextAnchor.UpperLeft;
            _lines[index].verticalOverflow = VerticalWrapMode.Truncate;
            // Overflow (not Wrap), matching every sibling panel's row-text
            // convention (TripHistoryPanel, CargoManifestPanel): a long
            // single line stays on one visual line and may extend past the
            // column, rather than wrapping to a second line that this
            // fixed row height would then clip.
            _lines[index].horizontalOverflow = HorizontalWrapMode.Overflow;
            y -= RowHeight;
        }

        _overflow = gui.CreateText(
            string.Empty, _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - (RowHeight / 2f)),
            font, 13, bodyColor, outline: true, Color.black, PanelWidth - 40f, RowHeight,
            addContentSizeFitter: false).GetComponent<Text>();
        _overflow.alignment = TextAnchor.UpperLeft;
        _overflow.horizontalOverflow = HorizontalWrapMode.Overflow;

        GameObject close = gui.CreateButton(
            TeamsterStrings.Get("ui.close"), _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), 110f, 28f);
        close.GetComponent<Button>().onClick.AddListener(Hide);

        _panel.SetActive(false);
        return true;
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
            "Compatibility panel UI failed and was disabled for this session: " +
            $"{exception.GetType().Name}: {exception.Message}");
    }
}
