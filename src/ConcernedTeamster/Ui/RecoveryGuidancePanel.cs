using System;
using BepInEx.Logging;
using Jotunn.Managers;
using TheConcernedCat.ConcernedTeamster.Adapters;
using TheConcernedCat.ConcernedTeamster.Domain.Localization;
using TheConcernedCat.ConcernedTeamster.Domain.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace TheConcernedCat.ConcernedTeamster.Ui;

/// <summary>The recovery guidance surface (CT-014), reached from the Cart
/// Status panel's Guidance button. Renders the presenter's title and steps
/// as read-only text — its only control is Close; the guidance layer holds
/// no reference to any mutating surface (the audit note lives in the PR and
/// CART_INTERNALS.md). Refreshes at 1 Hz from the pump's read-only
/// diagnosis. Fail-closed session-disable like every Teamster panel.</summary>
internal sealed class RecoveryGuidancePanel
{
    private const float PanelWidth = 380f;
    private const float PanelHeight = 360f;
    private const float RowHeight = 34f;
    private const int StepRowCount = 6;
    private const double RefreshPeriodSeconds = 1.0;

    private readonly ManualLogSource _log;
    private bool _failed;
    private GameObject? _panel;
    private Text? _title;
    private Text?[] _steps = Array.Empty<Text?>();
    private double _nextRefreshTime;

    public RecoveryGuidancePanel(ManualLogSource log)
    {
        _log = log;
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
            if (_panel == null && !Build())
            {
                return;
            }

            bool show = !_panel!.activeSelf;
            _panel.SetActive(show);
            if (show)
            {
                _nextRefreshTime = 0d;
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

    public void HandleFrame(double nowSeconds, CartTelemetryPump? pump)
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
                return;
            }

            if (nowSeconds < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = nowSeconds + RefreshPeriodSeconds;

            Domain.Carts.CartTelemetry? telemetry = null;
            string? cartId = pump?.LatestDescentRisk?.CartId;
            if (cartId is not null && pump?.Telemetry is { } table &&
                table.TryGetValue(cartId, out Domain.Carts.CartTelemetry pulled))
            {
                telemetry = pulled;
            }

            RecoveryGuidanceViewModel viewModel = RecoveryGuidancePresenter.Present(
                pump?.LatestDiagnostic, telemetry, pump?.LoadModel,
                brakeFeatureEnabled: pump?.Brake is not null);
            Render(viewModel);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    private void Render(RecoveryGuidanceViewModel viewModel)
    {
        if (_title != null)
        {
            _title.text = viewModel.Title;
        }

        for (int index = 0; index < _steps.Length; index++)
        {
            Text? row = _steps[index];
            if (row == null)
            {
                continue;
            }

            row.text = index < viewModel.Steps.Count
                ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  ". " + viewModel.Steps[index]
                : string.Empty;
        }
    }

    private bool Build()
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
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-(PanelWidth / 2f) - 380f, -120f), PanelWidth, PanelHeight, draggable: true);

        gui.CreateText(
            TeamsterStrings.Get("recovery.title"), _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f),
            font, 19, headerColor, outline: true, Color.black, PanelWidth - 40f, 30f,
            addContentSizeFitter: false);

        _title = gui.CreateText(
            string.Empty, _panel.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f),
            font, 15, headerColor, outline: false, Color.black, PanelWidth - 40f, 40f,
            addContentSizeFitter: false).GetComponent<Text>();
        if (_title != null)
        {
            _title.alignment = TextAnchor.UpperLeft;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
        }

        _steps = new Text?[StepRowCount];
        float y = -104f;
        for (int index = 0; index < StepRowCount; index++)
        {
            Text? row = gui.CreateText(
                string.Empty, _panel.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - (RowHeight / 2f)),
                font, 14, bodyColor, outline: false, Color.black, PanelWidth - 40f, RowHeight,
                addContentSizeFitter: false).GetComponent<Text>();
            if (row != null)
            {
                row.alignment = TextAnchor.UpperLeft;
                row.verticalOverflow = VerticalWrapMode.Truncate;
            }

            _steps[index] = row;
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
            "Recovery Guidance UI failed and was disabled for this session " +
            $"(telemetry keeps running): {exception.GetType().Name}: {exception.Message}");
    }
}
