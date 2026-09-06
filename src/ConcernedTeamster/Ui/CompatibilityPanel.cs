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
    private const float PanelHeight = 320f;
    private const float RowHeight = 26f;
    private const int MaxLines = 8;

    private readonly ManualLogSource _log;
    private readonly Func<float> _uiScale;
    private bool _failed;
    private GameObject? _panel;
    private Text[] _lines = Array.Empty<Text>();

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
        IReadOnlyList<string> detected = results is null
            ? Array.Empty<string>()
            : CompatibilityStatusPresenter.ComposeDetectedLines(results);

        for (int index = 0; index < _lines.Length; index++)
        {
            if (index == 0 && detected.Count == 0)
            {
                _lines[index].text = CompatibilityStatusPresenter.ComposeNoneDetectedLine();
            }
            else
            {
                _lines[index].text = index < detected.Count ? detected[index] : string.Empty;
            }
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
            _lines[index].horizontalOverflow = HorizontalWrapMode.Wrap;
            y -= RowHeight;
        }

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
