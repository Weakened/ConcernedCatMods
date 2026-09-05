using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The route report surface (CT-024): a read-only text panel
/// rendering the headless report presenter — summary, numbered problem
/// sections, and LoadModel-traced recommendations. Owned and fed by the
/// route picker (which itself exists only when the Cartographer capability
/// is Available), so absence composes: no Cartographer → no picker → no
/// report. Fail-closed like every Teamster surface: any UI exception
/// disables the report for the session with one ERROR line.</summary>
internal sealed class RouteReportPanel
{
    private const float PanelWidth = 460f;
    private const float PanelHeight = 500f;
    private const float LineHeight = 21f;
    internal const int MaxLines = 18;

    private readonly ManualLogSource _log;
    private bool _failed;
    private GameObject? _panel;
    private Text? _title;
    private Text[] _lines = Array.Empty<Text>();

    internal RouteReportPanel(ManualLogSource log)
    {
        _log = log;
    }

    internal bool IsVisible => _panel != null && _panel.activeSelf;

    internal void Toggle(RouteReportPresenter.ViewModel viewModel)
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
                Render(viewModel);
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    internal void Hide()
    {
        if (_panel != null && _panel.activeSelf)
        {
            _panel.SetActive(false);
        }
    }

    /// <summary>Binds the presenter output; no-op while hidden. The caller
    /// re-renders on its own refresh cadence so an open report follows
    /// profile completion and cart-mass changes.</summary>
    internal void Render(RouteReportPresenter.ViewModel viewModel)
    {
        if (_failed || !IsVisible || _title == null || _lines.Length != MaxLines)
        {
            return;
        }

        try
        {
            _title.text = viewModel.Title;
            for (int index = 0; index < MaxLines; index++)
            {
                _lines[index].text = index < viewModel.Lines.Count ? viewModel.Lines[index] : string.Empty;
            }
        }
        catch (Exception exception)
        {
            Fail(exception);
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
        var headerColor = new Color(0.9f, 0.8f, 0.6f, 1f);
        var bodyColor = new Color(0.85f, 0.85f, 0.82f, 1f);

        _panel = gui.CreateWoodpanel(
            GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(120f, 0f), PanelWidth, PanelHeight, draggable: true);

        _title = gui.CreateText(
            string.Empty, _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 18, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false).GetComponent<Text>();

        _lines = new Text[MaxLines];
        float y = -56f;
        for (int index = 0; index < MaxLines; index++)
        {
            _lines[index] = gui.CreateText(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - (LineHeight / 2f)),
                font, 14, bodyColor, outline: false, Color.black, PanelWidth - 40f, LineHeight,
                addContentSizeFitter: false).GetComponent<Text>();
            _lines[index].alignment = TextAnchor.UpperLeft;
            _lines[index].verticalOverflow = VerticalWrapMode.Truncate;
            y -= LineHeight;
        }

        GameObject close = gui.CreateButton(
            "Close", _panel.transform,
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), 96f, 30f);
        close.GetComponent<Button>().onClick.AddListener(() => _panel!.SetActive(false));

        _panel.SetActive(false);
        return true;
    }

    private void Fail(Exception exception)
    {
        _failed = true;
        if (_panel != null)
        {
            _panel.SetActive(false);
        }

        _log.LogError(
            "Route report disabled for this session after a UI exception; everything else keeps working: " +
            exception);
    }
}
